using System.Linq;
using Echokraut.DataClasses.Database;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// <see cref="DatabaseService.RetokenizePlayerNames"/> — the one-shot cleanup for databases
/// written before the live path tokenised player names. Rows carrying the literal name are
/// restored to their placeholder form and merged into the harvested twin they duplicate.
/// </summary>
public class RetokenizePlayerNamesTests : System.IDisposable
{
    private const string PlayerName = "Jon Doe";

    private readonly SqliteConnection _connection;
    private readonly EchokrautDbContext _context;
    private readonly DatabaseService _db;
    private readonly CharacterEntity _character;

    public RetokenizePlayerNamesTests()
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
        System.GC.SuppressFinalize(this);
    }

    private VoiceClipEntity SeedClip(string text, long npcBaseId = 1001, bool savedToDisk = false)
    {
        var clip = new VoiceClipEntity
        {
            CharacterId = _character.Id,
            NpcBaseId = npcBaseId,
            OriginalText = text,
            CleanedText = text,
            HasPlayerPlaceholder = TalkTextHelper.ContainsPlayerPlaceholder(text),
            SavedToDisk = savedToDisk,
            SavePath = savedToDisk ? "/audio/line.wav" : "",
        };
        _context.VoiceClips.Add(clip);
        _context.SaveChanges();
        return clip;
    }

    // ── Rewrite in place ────────────────────────────────────────────────

    [Fact]
    public void LiveRowWithoutTwin_IsRewrittenInPlace()
    {
        var clip = SeedClip("Well met, Jon!");

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.Equal(1, result.Rewritten);
        Assert.Equal(0, result.MergedAndDeleted);

        var reloaded = _context.VoiceClips.Single();
        Assert.Equal("Well met, -PlayerFirstName-!", reloaded.OriginalText);
        Assert.Equal("Well met, -PlayerFirstName-!", reloaded.CleanedText);
        // The flag drives which player_content_id bucket the generation row lands in — it has to
        // follow the text, or the Voice Clip Manager keeps reporting the clip as not generated.
        Assert.True(reloaded.HasPlayerPlaceholder);
    }

    [Fact]
    public void DryRun_ReportsWorkButWritesNothing()
    {
        SeedClip("Well met, Jon!");

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: true);

        Assert.Equal(1, result.Rewritten);
        Assert.True(result.HasWork);
        Assert.NotEmpty(result.Samples);

        _context.ChangeTracker.Clear();
        Assert.Equal("Well met, Jon!", _context.VoiceClips.Single().OriginalText);
    }

    [Fact]
    public void RowsWithoutThePlayerName_AreLeftAlone()
    {
        SeedClip("The aetheryte hums quietly.");

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.False(result.HasWork);
        Assert.Equal("The aetheryte hums quietly.", _context.VoiceClips.Single().OriginalText);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        SeedClip("Well met, Jon!");

        _db.RetokenizePlayerNames(PlayerName, dryRun: false);
        var second = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.False(second.HasWork);
        Assert.Single(_context.VoiceClips);
    }

    // ── Merge with the harvested twin ───────────────────────────────────

    [Fact]
    public void LiveRowDuplicatingAHarvestedRow_IsMergedAway()
    {
        var harvested = SeedClip("Well met, -PlayerFirstName-!");
        var live = SeedClip("Well met, Jon!", savedToDisk: true);

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.Equal(1, result.MergedAndDeleted);
        Assert.Equal(0, result.Rewritten);

        _context.ChangeTracker.Clear();
        var remaining = _context.VoiceClips.Single();
        Assert.Equal(harvested.Id, remaining.Id);
        Assert.Null(_context.VoiceClips.Find(live.Id));
        // The live row was the one that knew about the audio on disk — that knowledge survives.
        Assert.True(remaining.SavedToDisk);
        Assert.Equal("/audio/line.wav", remaining.SavePath);
    }

    [Fact]
    public void Merge_MovesGenerationRowsToTheSurvivor()
    {
        var harvested = SeedClip("Well met, -PlayerFirstName-!");
        var live = SeedClip("Well met, Jon!");
        _db.LogVoiceClipGeneration(live.Id, 42, PlayerName, "/audio/line.wav", "Female_Miqote_NPC001.wav");

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.Equal(1, result.GenerationsMoved);
        Assert.Equal(0, result.GenerationsDropped);

        _context.ChangeTracker.Clear();
        var gen = _context.VoiceClipGenerations.Single();
        Assert.Equal(harvested.Id, gen.VoiceClipId);
    }

    [Fact]
    public void Merge_DropsAGenerationRowThatWouldCollide()
    {
        var harvested = SeedClip("Well met, -PlayerFirstName-!");
        var live = SeedClip("Well met, Jon!");
        // Both rows carry a generation for the same player + alias slot. They describe the same
        // file (the filename hash is identical for both spellings), so one of them must go
        // rather than blow up the unique index.
        _db.LogVoiceClipGeneration(harvested.Id, 42, PlayerName, "/audio/line.wav", "voiceA.wav");
        _db.LogVoiceClipGeneration(live.Id, 42, PlayerName, "/audio/line.wav", "voiceA.wav");

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.Equal(0, result.GenerationsMoved);
        Assert.Equal(1, result.GenerationsDropped);

        _context.ChangeTracker.Clear();
        var gen = _context.VoiceClipGenerations.Single();
        Assert.Equal(harvested.Id, gen.VoiceClipId);
    }

    [Fact]
    public void Merge_OnlyAppliesWithinTheSameCharacterAndBaseId()
    {
        // Same sentence, different NPC instance — these are genuinely two clips and must both
        // survive, tokenised.
        SeedClip("Well met, -PlayerFirstName-!", npcBaseId: 1001);
        SeedClip("Well met, Jon!", npcBaseId: 2002);

        var result = _db.RetokenizePlayerNames(PlayerName, dryRun: false);

        Assert.Equal(1, result.Rewritten);
        Assert.Equal(0, result.MergedAndDeleted);
        Assert.Equal(2, _context.VoiceClips.Count());
    }

    [Fact]
    public void EmptyPlayerName_DoesNothing()
    {
        SeedClip("Well met, Jon!");

        var result = _db.RetokenizePlayerNames("", dryRun: false);

        Assert.Equal(0, result.Scanned);
        Assert.False(result.HasWork);
        Assert.Equal("Well met, Jon!", _context.VoiceClips.Single().OriginalText);
    }
}
