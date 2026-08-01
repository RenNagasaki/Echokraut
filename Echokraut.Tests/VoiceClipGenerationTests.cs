using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Covers the schema v13 alias_gender column on voice_clip_generations and the corresponding
/// CRUD overloads. Alias variants (1=male, 2=female) are stored as separate rows from the
/// player's own generation (alias_gender=0) so the same clip can carry shareable variants
/// alongside the personal one.
/// </summary>
public class VoiceClipGenerationTests : IDisposable
{
    private readonly DatabaseService _db;
    private readonly EchokrautDbContext _context;
    private readonly SqliteConnection _connection;

    public VoiceClipGenerationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new EchokrautDbContext(options);
        var mockLog = new Mock<ILogService>();
        _db = new DatabaseService(mockLog.Object, _context);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private VoiceClipEntity SeedClip(bool hasPlaceholder = true)
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Tataru",
            Race = (int)NpcRaces.Lalafell,
            Gender = (int)Genders.Female,
            ObjectKind = (int)ObjectKind.EventNpc,
            Language = 1,
        });

        var clip = new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 1234,
            Timestamp = DateTime.UtcNow,
            TextSource = (int)Echotools.Logging.Enums.TextSource.AddonTalk,
            Language = 1,
            VoiceKey = "voice_x",
            OriginalText = "Hello -PlayerName-!",
            CleanedText = "Hello -PlayerName-!",
            HasPlayerPlaceholder = hasPlaceholder,
        };
        _db.LogVoiceClip(clip);

        return _context.VoiceClips.AsNoTracking().First(vc => vc.CharacterId == character.Id);
    }

    [Fact]
    public void Schema_HasAliasGenderColumn()
    {
        // Column exists with default 0 — exercised via insert without explicit value.
        var clip = SeedClip();
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 99, "MyName", "/path/own.wav", voiceKey: "");

        var row = _context.VoiceClipGenerations.AsNoTracking().Single();
        Assert.Equal(0, row.AliasGender);
    }

    [Fact]
    public void LogVoiceClipGeneration_AliasVariantsCoexistWithPersonal()
    {
        var clip = SeedClip();

        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 42, "MyName", "/path/own.wav", voiceKey: "VoiceA");
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "Adventurer", "/path/m.wav", voiceKey: "VoiceA", aliasGender: 1);
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "Adventurer", "/path/f.wav", voiceKey: "VoiceA", aliasGender: 2);

        var rows = _context.VoiceClipGenerations.AsNoTracking()
            .Where(g => g.VoiceClipId == clip.Id)
            .OrderBy(g => g.AliasGender)
            .ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal((0, 42L, "/path/own.wav"), (rows[0].AliasGender, rows[0].PlayerContentId, rows[0].SavePath));
        Assert.Equal((1, 0L, "/path/m.wav"),    (rows[1].AliasGender, rows[1].PlayerContentId, rows[1].SavePath));
        Assert.Equal((2, 0L, "/path/f.wav"),    (rows[2].AliasGender, rows[2].PlayerContentId, rows[2].SavePath));
    }

    [Fact]
    public void GetVoiceClipGeneration_ReturnsRowMatchingAliasGender()
    {
        var clip = SeedClip();
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "Adventurer",  "/path/m.wav", voiceKey: "VoiceA", aliasGender: 1);
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "Adventurerf", "/path/f.wav", voiceKey: "VoiceA", aliasGender: 2);

        var male   = _db.GetVoiceClipGeneration(clip.Id, playerContentId: 0, aliasGender: 1);
        var female = _db.GetVoiceClipGeneration(clip.Id, playerContentId: 0, aliasGender: 2);
        var none   = _db.GetVoiceClipGeneration(clip.Id, playerContentId: 0); // default aliasGender=0

        Assert.NotNull(male);
        Assert.Equal("/path/m.wav", male!.SavePath);
        Assert.NotNull(female);
        Assert.Equal("/path/f.wav", female!.SavePath);
        Assert.Null(none); // no row with aliasGender=0 was inserted
    }

    [Fact]
    public void DeleteVoiceClipGeneration_OnlyRemovesMatchingAliasRow()
    {
        var clip = SeedClip();
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "A", "/m.wav", voiceKey: "VoiceA", aliasGender: 1);
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "B", "/f.wav", voiceKey: "VoiceA", aliasGender: 2);
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 7, "Self", "/own.wav", voiceKey: "VoiceA");

        _db.DeleteVoiceClipGeneration(clip.Id, playerContentId: 0, aliasGender: 1);

        var remaining = _context.VoiceClipGenerations.AsNoTracking()
            .Where(g => g.VoiceClipId == clip.Id)
            .OrderBy(g => g.AliasGender)
            .ToList();

        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, r => r.AliasGender == 1);
        Assert.Contains(remaining, r => r.AliasGender == 2 && r.SavePath == "/f.wav");
        Assert.Contains(remaining, r => r.AliasGender == 0 && r.PlayerContentId == 7L);
    }

    [Fact]
    public void LogVoiceClipGeneration_RepeatWithSameKeyUpdatesInPlace()
    {
        var clip = SeedClip();
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "OldAlias", "/old.wav", voiceKey: "VoiceA", aliasGender: 1);
        _db.LogVoiceClipGeneration(clip.Id, playerContentId: 0, "NewAlias", "/new.wav", voiceKey: "VoiceB", aliasGender: 1);

        var rows = _context.VoiceClipGenerations.AsNoTracking()
            .Where(g => g.VoiceClipId == clip.Id && g.AliasGender == 1)
            .ToList();
        Assert.Single(rows);
        Assert.Equal("/new.wav", rows[0].SavePath);
        Assert.Equal("NewAlias", rows[0].PlayerName);
    }
}
