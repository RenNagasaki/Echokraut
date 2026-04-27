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
/// End-to-end tests for the v12 schema migration that merges case-only-different
/// character rows. Replays the production scenario observed in echokraut.db where
/// "stille Druidin" (harvest stem) and "Stille Druidin" (runtime display) lived as
/// two separate rows.
/// </summary>
public class SchemaV12MergeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EchokrautDbContext _context;
    private readonly DatabaseService _db;

    public SchemaV12MergeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EchokrautDbContext(options);
        var log = new Mock<ILogService>();
        _db = new DatabaseService(log.Object, _context);

        // Re-instate the pre-v12 case-sensitive index so we can build a duplicate dataset.
        _context.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_characters_name_gender_race_language");
        _context.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX IX_characters_name_gender_race_language ON characters (name, gender, race, language)");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void MergeCaseDuplicates_CollapsesPair_KeepsAllVoiceClips()
    {
        // Loser (lowercase, harvest-style: no voice key, BattleNpc, two voice clips).
        var loser = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "stille Druidin",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Female,
            Language = 2,
            VoiceKey = "",
            ObjectKind = (int)ObjectKind.BattleNpc,
            BodyType = 1,
            RaceStr = "Elezen",
        });
        _db.UpsertContext(loser.Id, "npc");
        _db.LogVoiceClip(new VoiceClipEntity
        {
            CharacterId = loser.Id, NpcBaseId = 1000460,
            OriginalText = "Welcome, traveler.", Language = 2,
            Timestamp = DateTime.UtcNow,
        });
        _db.LogVoiceClip(new VoiceClipEntity
        {
            CharacterId = loser.Id, NpcBaseId = 1000460,
            OriginalText = "Begone.", Language = 2,
            Timestamp = DateTime.UtcNow,
        });

        // Winner (uppercase, runtime-style: voice key set, EventNpc, one voice clip).
        // Direct SQL because the case-insensitive UpsertCharacter would otherwise consolidate
        // these into one row — we want the pre-v12 split state.
        _context.Database.ExecuteSqlRaw(
            @"INSERT INTO characters (name, race, race_str, gender, body_type, voice_key, language, object_kind, world)
              VALUES ('Stille Druidin', {0}, 'Elezen', {1}, 1, 'voice_runtime.wav', 2, {2}, '')",
            (int)NpcRaces.Elezen, (int)Genders.Female, (int)ObjectKind.EventNpc);
        var winnerId = _context.Characters
            .Where(c => c.Name == "Stille Druidin")
            .Select(c => c.Id)
            .First();
        _context.Database.ExecuteSqlRaw(
            @"INSERT INTO voice_clips
                (character_id, npc_base_id, original_text, cleaned_text, voice_key, save_path, zone_name,
                 language, timestamp, text_source, body_type, saved_to_disk, has_player_placeholder,
                 quest_type, map_x, map_y)
              VALUES ({0}, 1000460, 'Greetings.', '', '', '', '', 2, '2026-04-26 22:00:00', 0, 0, 0, 0, 0, 0, 0)",
            winnerId);

        Assert.Equal(2, _context.Characters.Count());
        Assert.Equal(3, _context.VoiceClips.Count());

        // Run the v12 merge directly.
        _db.MergeCaseDuplicateCharacters();

        // The two case-duplicates collapse into one canonical title-cased row.
        var rows = _context.Characters
            .Where(c => EF.Functions.Collate(c.Name, "NOCASE") == "Stille Druidin")
            .ToList();
        Assert.Single(rows);
        Assert.Equal("Stille Druidin", rows[0].Name);

        // All three voice clips survive on the surviving row.
        var clips = _context.VoiceClips.Where(v => v.CharacterId == rows[0].Id).ToList();
        Assert.Equal(3, clips.Count);

        // Voice key is preserved (the runtime row was the winner because its voice_key was non-empty).
        Assert.Equal("voice_runtime.wav", rows[0].VoiceKey);
    }

    [Fact]
    public void MergeCaseDuplicates_DropsConflictingChildClips()
    {
        // Both rows have a clip for the same (npc_base_id, original_text) — the loser's must be
        // dropped, not moved, otherwise the v7 composite unique index would get violated.
        var loser = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "kleine helferlein",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Female,
            Language = 2,
            VoiceKey = "",
            BodyType = 1,
            RaceStr = "Hyur",
        });
        _db.LogVoiceClip(new VoiceClipEntity
        {
            CharacterId = loser.Id, NpcBaseId = 42,
            OriginalText = "Hello.", Language = 2,
            Timestamp = DateTime.UtcNow,
        });

        _context.Database.ExecuteSqlRaw(
            @"INSERT INTO characters (name, race, race_str, gender, body_type, voice_key, language, object_kind, world)
              VALUES ('Kleine Helferlein', {0}, 'Hyur', {1}, 1, '', 2, 0, '')",
            (int)NpcRaces.Hyur, (int)Genders.Female);
        var winnerId = _context.Characters
            .Where(c => c.Name == "Kleine Helferlein")
            .Select(c => c.Id)
            .First();
        _context.Database.ExecuteSqlRaw(
            @"INSERT INTO voice_clips
                (character_id, npc_base_id, original_text, cleaned_text, voice_key, save_path, zone_name,
                 language, timestamp, text_source, body_type, saved_to_disk, has_player_placeholder,
                 quest_type, map_x, map_y)
              VALUES ({0}, 42, 'Hello.', '', '', '', '', 2, '2026-04-26 22:00:00', 0, 0, 0, 0, 0, 0, 0)",
            winnerId);

        _db.MergeCaseDuplicateCharacters();

        // Only one row with that (npc_base_id, original_text) survives.
        var clips = _context.VoiceClips
            .Where(v => v.NpcBaseId == 42 && v.OriginalText == "Hello.")
            .ToList();
        Assert.Single(clips);
    }

    [Fact]
    public void MergeCaseDuplicates_NoDuplicates_DoesNothing()
    {
        _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Thancred", Race = (int)NpcRaces.Hyur, Gender = (int)Genders.Male,
            Language = 1, BodyType = 1, RaceStr = "Hyur", VoiceKey = "",
        });
        _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Y'shtola", Race = (int)NpcRaces.Miqote, Gender = (int)Genders.Female,
            Language = 1, BodyType = 1, RaceStr = "Miqote", VoiceKey = "",
        });

        _db.MergeCaseDuplicateCharacters();

        Assert.Equal(2, _context.Characters.Count());
    }
}
