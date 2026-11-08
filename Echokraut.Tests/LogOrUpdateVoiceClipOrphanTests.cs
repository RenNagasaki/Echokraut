using System.Linq;
using Echokraut.DataClasses.Database;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Exercises <see cref="DatabaseService.LogOrUpdateVoiceClip"/>'s orphan-resolve
/// fallback. The legacy audio-file backfill creates voice_clips rows with only
/// <c>WavFileName</c> + <c>SavePath</c> + <c>SavedToDisk=true</c> — the text fields
/// stay empty because the backfill has no source for them. When the live runtime
/// later encounters that NPC saying that line, the existing-by-text lookup misses,
/// but the (CharacterId, WavFileName) fallback finds the orphan and promotes it
/// in place rather than inserting a duplicate.
/// </summary>
public class LogOrUpdateVoiceClipOrphanTests : System.IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EchokrautDbContext _context;
    private readonly DatabaseService _db;
    private readonly CharacterEntity _character;

    public LogOrUpdateVoiceClipOrphanTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EchokrautDbContext(options);
        _db = new DatabaseService(new Mock<ILogService>().Object, _context);

        _character = new CharacterEntity { Name = "Y'shtola", Language = 1, Gender = 1, Race = 3 };
        _context.Characters.Add(_character);
        _context.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    private VoiceClipEntity SeedOrphan(string wavFileName, string savePath = "/legacy.wav") =>
        SeedRawClip(_character.Id, wavFileName, savePath);

    private VoiceClipEntity SeedRawClip(int characterId, string wavFileName, string savePath)
    {
        var clip = new VoiceClipEntity
        {
            CharacterId = characterId,
            WavFileName = wavFileName,
            SavePath = savePath,
            SavedToDisk = true,
            VoiceKey = "Female_Miqote_Y'shtola.wav",
            // OriginalText / CleanedText / NpcBaseId all left at their defaults — that's
            // exactly what the backfill produces.
        };
        _context.VoiceClips.Add(clip);
        _context.SaveChanges();
        return clip;
    }

    // ── Orphan promotion ────────────────────────────────────────────────

    [Fact]
    public void OrphanWithMatchingWavFileName_GetsPromotedNotDuplicated()
    {
        var orphan = SeedOrphan("halloweltgrussdich");

        // Live encounter for the same dialog. Same wav_file_name (the runtime computed it
        // from RemovePlayerNameInText + VoiceMessageToFileName); the existing-by-text lookup
        // misses (orphan has empty text), but the (CharacterId, WavFileName) fallback hits.
        _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = _character.Id,
            NpcBaseId = 4242,
            OriginalText = "Hallo Welt, grüß dich.",
            CleanedText = "Hallo Welt, grüß dich.",
            WavFileName = "halloweltgrussdich",
            VoiceKey = "Female_Miqote_Y'shtola.wav",
            BodyType = 0,
            HasPlayerPlaceholder = false,
            TextSource = 4,
            Language = 2,
        });

        // Single row, not two. The orphan was updated in place.
        var rows = _context.VoiceClips.AsNoTracking().ToList();
        Assert.Single(rows);
        Assert.Equal(orphan.Id, rows[0].Id);

        // Text + base id + source/language all populated from the live encounter.
        Assert.Equal(4242, rows[0].NpcBaseId);
        Assert.Equal("Hallo Welt, grüß dich.", rows[0].OriginalText);
        Assert.Equal("Hallo Welt, grüß dich.", rows[0].CleanedText);
        Assert.Equal(4, rows[0].TextSource);
        Assert.Equal(2, rows[0].Language);
    }

    [Fact]
    public void OrphanPromotion_PreservesBackfillSavePath()
    {
        // The orphan landed with SavedToDisk=true + a real SavePath from the backfill scan.
        // The live encounter doesn't have a fresh SavePath yet (audio file isn't generated
        // at this point in the live pipeline). The promote must NOT clobber the existing
        // SavePath with empty — same "don't overwrite with empty" rule that the regular
        // update path uses.
        SeedOrphan("samefile", savePath: "C:/legacy/samefile.wav");

        _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = _character.Id,
            NpcBaseId = 1,
            OriginalText = "live encounter",
            WavFileName = "samefile",
            // SavedToDisk = false, SavePath = "" — defaults
        });

        var row = _context.VoiceClips.AsNoTracking().Single();
        Assert.True(row.SavedToDisk);
        Assert.Equal("C:/legacy/samefile.wav", row.SavePath);
    }

    [Fact]
    public void OrphanPromotion_AllowsLaterTextBasedRelookups()
    {
        // After the promote, subsequent live encounters of the same dialog hit the
        // (CharacterId, NpcBaseId, OriginalText) lookup first — they don't even need the
        // WavFileName fallback anymore. Verifies the post-promote row is "fully formed"
        // and discoverable by the primary path.
        SeedOrphan("eineweitereorphandatei");

        _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = _character.Id,
            NpcBaseId = 99,
            OriginalText = "Eine weitere Orphan-Datei.",
            CleanedText = "Eine weitere Orphan-Datei.",
            WavFileName = "eineweitereorphandatei",
        });

        // Re-encounter the same dialog (text-only path, no WavFileName supplied). Must still
        // find the row via the primary text lookup.
        var rePersisted = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = _character.Id,
            NpcBaseId = 99,
            OriginalText = "Eine weitere Orphan-Datei.",
            CleanedText = "Eine weitere Orphan-Datei.",
            VoiceKey = "Female_Miqote_Y'shtola.wav",
        });

        Assert.Single(_context.VoiceClips.AsNoTracking().ToList());
        Assert.Equal(99, rePersisted.NpcBaseId);
    }

    // ── Fallback boundary cases ─────────────────────────────────────────

    [Fact]
    public void NoFallbackTriggeredWhenWavFileNameEmpty()
    {
        // Harvest path passes WavFileName = "" — the fallback must not fire and snap onto
        // any wav-empty row in the DB (every freshly inserted clip starts at "").
        SeedOrphan("");

        _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = _character.Id,
            NpcBaseId = 1,
            OriginalText = "wholly new dialog",
            WavFileName = "",
        });

        // Two rows, not one — the orphan with WavFileName="" doesn't match because the
        // fallback only triggers when the input has a non-empty WavFileName.
        Assert.Equal(2, _context.VoiceClips.AsNoTracking().Count());
    }

    [Fact]
    public void NoFallbackTriggeredWhenWavFileNameDiffersAcrossCharacters()
    {
        // Different character with the same wav_file_name (rare collision; e.g., two NPCs
        // with very similar dialogue). The fallback's lookup is keyed on CharacterId so
        // it doesn't cross over.
        var otherChar = new CharacterEntity { Name = "Krile", Language = 1, Gender = 1, Race = 5 };
        _context.Characters.Add(otherChar);
        _context.SaveChanges();
        SeedRawClip(otherChar.Id, "samewavhash", savePath: "/krile/samewavhash.wav");

        _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = _character.Id,         // Y'shtola, NOT Krile
            NpcBaseId = 1,
            OriginalText = "yshtola line",
            WavFileName = "samewavhash",
        });

        // Two rows — Krile's orphan stays untouched, Y'shtola gets a fresh row.
        var rows = _context.VoiceClips.AsNoTracking().OrderBy(r => r.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(otherChar.Id, rows[0].CharacterId);
        Assert.Equal("", rows[0].OriginalText);                 // Krile orphan untouched
        Assert.Equal(_character.Id, rows[1].CharacterId);
        Assert.Equal("yshtola line", rows[1].OriginalText);
    }
}
