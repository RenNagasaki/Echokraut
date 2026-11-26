using System.IO;
using System.Linq;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Exercises <see cref="DatabaseService.BackfillAudioFiles"/> against a temp directory
/// populated with synthetic .wav files. The backfill is the second half of the legacy
/// migration story (the first being <see cref="DatabaseService.MigrateFromConfig"/>) —
/// it scans <see cref="Configuration.LocalSaveLocation"/>, matches folder names against
/// existing characters, and creates voice_clips + voice_clip_generations rows for each
/// matched file.
///
/// Tests cover:
/// - Happy path: folder matches a unique character, file becomes a voice_clip + generation
/// - Multi-character collision: skipped silently (per user spec — those rows aren't clean)
/// - Folder with no matching character: skipped
/// - Player-placeholder detection via the substituted-token markers in the filename
///   AND via the literal player name (legacy format)
/// - voice_key on the generation row comes from the character's current VoiceKey
/// - Idempotency: re-running over the same disk state doesn't duplicate rows
/// - The Pending flag clears after a successful run
/// </summary>
public class BackfillAudioFilesTests : System.IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EchokrautDbContext _context;
    private readonly DatabaseService _db;
    private readonly string _tempRoot;
    private readonly Configuration _config;
    private readonly Mock<IGameObjectService> _gameObjects = new();
    private readonly Mock<IAudioFileService> _audioFiles = new();

    public BackfillAudioFilesTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EchokrautDbContext(options);
        _db = new DatabaseService(new Mock<ILogService>().Object, _context, ClientLanguage.German);

        _tempRoot = Path.Combine(Path.GetTempPath(), "ek-backfill-tests-" + System.Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);

        _config = new Configuration
        {
            LocalSaveLocation = _tempRoot,
            AudioFilesBackfillPending = true,
        };
        // Configuration.Save() needs PluginInterface; we don't have one in tests. Rely on
        // BackfillAudioFiles tolerating a null pluginInterface (Configuration.Save no-ops in
        // that case — see Configuration.cs).
    }

    public void Dispose()
    {
        _db.Dispose();
        _context.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private CharacterEntity SeedCharacter(string name, string voiceKey = "VoiceA",
        int gender = 0, int race = 1)
    {
        var c = new CharacterEntity
        {
            Name = name,
            Gender = gender,
            Race = race,
            Language = (int)ClientLanguage.German,
            VoiceKey = voiceKey,
        };
        _context.Characters.Add(c);
        _context.SaveChanges();
        return c;
    }

    private void WriteFile(string speakerFolder, string baseName)
    {
        var dir = Path.Combine(_tempRoot, speakerFolder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, baseName + ".wav");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 }); // not real WAV; backfill never reads contents
    }

    private void SetupPlayer(string name, ulong contentId)
    {
        _gameObjects.Setup(g => g.LocalPlayerName).Returns(name);
        _gameObjects.Setup(g => g.LocalPlayerContentId).Returns(contentId);
    }

    // ── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public void Backfill_CreatesVoiceClipAndGenerationForMatchedFile()
    {
        var character = SeedCharacter("Y'shtola", voiceKey: "Female_Miqote_Y'shtola.wav");
        WriteFile("Y'shtola", "wirsindgekommenumzuhelfen");
        SetupPlayer("Test Player", 0x1234567890ABCDEFUL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        var clip = _context.VoiceClips.AsNoTracking().Single(vc => vc.CharacterId == character.Id);
        Assert.Equal("wirsindgekommenumzuhelfen", clip.WavFileName);
        Assert.Equal("Female_Miqote_Y'shtola.wav", clip.VoiceKey);
        Assert.True(clip.SavedToDisk);
        Assert.True(clip.SavePath.EndsWith("wirsindgekommenumzuhelfen.wav"));
        Assert.Equal("", clip.OriginalText);    // backfill doesn't invent text content
        Assert.Equal("", clip.CleanedText);

        var gen = _context.VoiceClipGenerations.AsNoTracking()
            .Single(g => g.VoiceClipId == clip.Id);
        Assert.Equal("Female_Miqote_Y'shtola.wav", gen.VoiceKey);
        Assert.Equal(0L, gen.PlayerContentId);  // no placeholder in this filename
        Assert.Equal(0, gen.AliasGender);
    }

    [Fact]
    public void Backfill_ClearsPendingFlagAfterSuccess()
    {
        SeedCharacter("Y'shtola");
        WriteFile("Y'shtola", "halloweltgrussdich");
        SetupPlayer("Test Player", 0x1234UL);

        Assert.True(_config.AudioFilesBackfillPending);
        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);
        Assert.False(_config.AudioFilesBackfillPending);
    }

    [Fact]
    public void Backfill_SkipsFolderWithNoMatchingCharacter()
    {
        // No SeedCharacter call — DB is empty.
        WriteFile("UnknownNpc", "filewithnoowner");
        SetupPlayer("Test Player", 0x1234UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        Assert.Empty(_context.VoiceClips.AsNoTracking().ToList());
        Assert.Empty(_context.VoiceClipGenerations.AsNoTracking().ToList());
    }

    [Fact]
    public void Backfill_SkipsAmbiguousMultiMatchFolders()
    {
        // Two characters with the same name + language but different gender/race — the
        // fixture's ???-type rows land like this in real prod data, but ??? isn't a legal
        // Windows folder name, so use a stand-in. The matcher's tie-break is "skip" per
        // user spec ("die sind so oder so nicht sauber"); we'd rather leave the file an
        // orphan than attach it to a coin-flip wrong character.
        SeedCharacter("Generic NPC", gender: 0, race: 1);
        SeedCharacter("Generic NPC", gender: 1, race: 3);
        WriteFile("Generic NPC", "irgendeinfile");
        SetupPlayer("Test Player", 0x1234UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        Assert.Empty(_context.VoiceClips.AsNoTracking().ToList());
    }

    // ── Player-placeholder detection ────────────────────────────────────

    [Fact]
    public void Backfill_DetectsPlayerPlaceholderViaSubstitutedToken()
    {
        // Modern plugin code substitutes the player name with <PLAYERNAME> before generating
        // the filename, which becomes "playername" once VoiceMessageToFileName strips the
        // angle brackets and lowercases. Files containing that token belong to the local
        // player's content id.
        var character = SeedCharacter("Y'shtola");
        WriteFile("Y'shtola", "halloplayernamewillkommenzuhause");
        SetupPlayer("Test Player", 0x1234567890ABCDEFUL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        var gen = _context.VoiceClipGenerations.AsNoTracking().Single();
        Assert.Equal((long)0x1234567890ABCDEFUL, gen.PlayerContentId);
    }

    [Fact]
    public void Backfill_DetectsPlayerPlaceholderViaLiteralFirstName()
    {
        // Legacy plugin versions baked the literal player name into the filename (no
        // <PLAYERNAME> substitution). The backfill's fallback heuristic catches those by
        // comparing the normalised filename against the current player's first/last name.
        // Trade-off: false positives for files that legitimately mention the player are
        // misclassified — accepted per user spec; can be cleaned up later.
        var character = SeedCharacter("Y'shtola");
        WriteFile("Y'shtola", "willkommennoctusdaheim"); // contains "noctus"
        SetupPlayer("Noctus Flatulentus", 0xAAAA1111UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        var gen = _context.VoiceClipGenerations.AsNoTracking().Single();
        Assert.Equal((long)0xAAAA1111UL, gen.PlayerContentId);
    }

    [Fact]
    public void Backfill_LeavesPlaceholderlessFilesAtPlayerContentZero()
    {
        var character = SeedCharacter("Y'shtola");
        WriteFile("Y'shtola", "irgendeinedialogzeile"); // no player name, no placeholder token
        SetupPlayer("Noctus Flatulentus", 0xAAAA1111UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        var gen = _context.VoiceClipGenerations.AsNoTracking().Single();
        Assert.Equal(0L, gen.PlayerContentId);
    }

    // ── voice_key resolution ────────────────────────────────────────────

    [Fact]
    public void Backfill_TakesVoiceKeyFromCharactersCurrentAssignment()
    {
        // Even if multiple files for the same speaker exist, every generation row gets the
        // character's current voice_key (the snapshot the JSON migration wrote). Files
        // generated under a previous voice get attributed to the new one — accepted because
        // we have no per-file historical information to recover the old value.
        var character = SeedCharacter("Krile", voiceKey: "Female_Lalafell_Krile.wav");
        WriteFile("Krile", "ersterdialog");
        WriteFile("Krile", "zweiterdialog");
        SetupPlayer("Test Player", 0x1234UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        var gens = _context.VoiceClipGenerations.AsNoTracking().ToList();
        Assert.Equal(2, gens.Count);
        Assert.All(gens, g => Assert.Equal("Female_Lalafell_Krile.wav", g.VoiceKey));
    }

    // ── Idempotency ─────────────────────────────────────────────────────

    [Fact]
    public void Backfill_IsIdempotentOverSameDiskState()
    {
        var character = SeedCharacter("Y'shtola");
        WriteFile("Y'shtola", "samefile");
        SetupPlayer("Test Player", 0x1234UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);
        _config.AudioFilesBackfillPending = true; // re-arm so the second run actually fires
        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        // Both runs should land on the same row, not duplicate. The
        // (character_id, wav_file_name) lookup hits the existing row on the second pass;
        // LogVoiceClipGeneration upserts on (voice_clip_id, player_content_id, alias_gender).
        Assert.Single(_context.VoiceClips.AsNoTracking().ToList());
        Assert.Single(_context.VoiceClipGenerations.AsNoTracking().ToList());
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void Backfill_HandlesMissingLocalSaveLocation()
    {
        // User wiped the directory between plugin runs. Don't crash — just clear the flag
        // and move on (nothing to scan, nothing to migrate).
        Directory.Delete(_tempRoot, recursive: true);
        SetupPlayer("Test Player", 0x1234UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        Assert.False(_config.AudioFilesBackfillPending);
    }

    [Fact]
    public void Backfill_RestoresEmptyNameCharacterFromNopersonFolder()
    {
        // AudioFileService.GetSpeakerAudioPath substitutes "NOPERSON" for empty speaker
        // names (the Amon-style entries). The backfill normalises that back to "" so the
        // empty-name CharacterEntity gets its files attributed.
        var character = SeedCharacter("", voiceKey: "Male_Unknown_Amon.wav");
        WriteFile("NOPERSON", "amonsprachzubenutzer");
        SetupPlayer("Test Player", 0x1234UL);

        _db.BackfillAudioFiles(_config, _gameObjects.Object, _audioFiles.Object);

        var clip = _context.VoiceClips.AsNoTracking().Single();
        Assert.Equal(character.Id, clip.CharacterId);
    }
}
