using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Echokraut.Services;

public class DatabaseService : IDatabaseService
{
    private readonly ILogService _log;
    private readonly EchokrautDbContext _context;
    private readonly object _writeLock = new();

    // In-memory caches for hot-path reads
    private volatile List<CharacterEntity> _cachedNpcs = new();
    private volatile List<CharacterEntity> _cachedPlayers = new();
    private volatile List<VoiceEntity> _cachedVoices = new();
    private volatile List<PhoneticCorrectionEntity> _cachedPhonetics = new();
    private volatile HashSet<uint> _cachedMutedBaseIds = new();

    public DatabaseService(ILogService log, string configDirectory, Configuration config)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        var dbPath = Path.Combine(configDirectory, "echokraut.db");
        _context = new EchokrautDbContext(dbPath);

        try
        {
            InitializeDatabase(config);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Constructor for testing with a pre-configured DbContext.
    /// </summary>
    public DatabaseService(ILogService log, EchokrautDbContext context)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _context = context ?? throw new ArgumentNullException(nameof(context));

        _context.Database.EnsureCreated();
        RefreshAllCaches();
    }

    private const int CurrentSchemaVersion = 12;

    private void InitializeDatabase(Configuration config)
    {
        // Enable WAL mode for better concurrent read performance
        _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON");
        _context.Database.EnsureCreated();

        RunSchemaMigrations();

        if (NeedsMigration(config))
        {
            _log.Info(nameof(InitializeDatabase), "Migrating data from JSON config to SQLite...",
                new EKEventId(0, TextSource.None));
            MigrateFromConfig(config);
        }

        RefreshAllCaches();
    }

    private void RunSchemaMigrations()
    {
        // Create schema_version table if it doesn't exist
        _context.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)");

        var version = 0;
        try
        {
            using var cmd = _context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
            if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                _context.Database.GetDbConnection().Open();
            var result = cmd.ExecuteScalar();
            if (result != null)
                version = Convert.ToInt32(result);
        }
        catch { /* Table may not exist yet on first run */ }

        if (version < 1)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v1: initial schema",
                new EKEventId(0, TextSource.None));
            // v1 is the initial schema created by EnsureCreated — just record it
            SetSchemaVersion(1);
        }

        // Migrations v2-v4 operate on the old dialog_encounters table.
        // On fresh installs, EnsureCreated creates voice_clips directly — skip these.
        var hasOldTable = false;
        try
        {
            using var checkCmd = _context.Database.GetDbConnection().CreateCommand();
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='dialog_encounters'";
            var checkResult = checkCmd.ExecuteScalar();
            hasOldTable = checkResult != null;
        }
        catch { /* ignore */ }

        if (version < 2 && hasOldTable)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v2: cascade delete encounters on character deletion, make character_id non-nullable",
                new EKEventId(0, TextSource.None));

            // Delete orphaned encounters (character_id is NULL)
            _context.Database.ExecuteSqlRaw(
                "DELETE FROM dialog_encounters WHERE character_id IS NULL");

            // SQLite doesn't support ALTER COLUMN or ALTER CONSTRAINT.
            // Recreate the table with the new schema.
            _context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS dialog_encounters_new (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    character_id INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
                    npc_base_id INTEGER NOT NULL DEFAULT 0,
                    timestamp TEXT NOT NULL,
                    text_source INTEGER NOT NULL,
                    language INTEGER NOT NULL,
                    voice_key TEXT NOT NULL DEFAULT '',
                    original_text TEXT NOT NULL DEFAULT '',
                    cleaned_text TEXT NOT NULL DEFAULT '',
                    saved_to_disk INTEGER NOT NULL DEFAULT 0,
                    body_type INTEGER NOT NULL DEFAULT 0
                )");

            _context.Database.ExecuteSqlRaw(@"
                INSERT INTO dialog_encounters_new
                    (id, character_id, npc_base_id, timestamp, text_source, language,
                     voice_key, original_text, cleaned_text, saved_to_disk, body_type)
                SELECT id, character_id, npc_base_id, timestamp, text_source, language,
                       voice_key, original_text, cleaned_text, saved_to_disk, body_type
                FROM dialog_encounters
                WHERE character_id IS NOT NULL");

            _context.Database.ExecuteSqlRaw("DROP TABLE dialog_encounters");
            _context.Database.ExecuteSqlRaw("ALTER TABLE dialog_encounters_new RENAME TO dialog_encounters");

            // Recreate indexes
            _context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_enc_character ON dialog_encounters(character_id)");
            _context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_enc_timestamp ON dialog_encounters(timestamp)");
            _context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_enc_source ON dialog_encounters(text_source)");

            SetSchemaVersion(2);
        }

        if (version < 3 && hasOldTable)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v3: add zone_name, map_x, map_y to dialog_encounters",
                new EKEventId(0, TextSource.None));

            _context.Database.ExecuteSqlRaw("ALTER TABLE dialog_encounters ADD COLUMN zone_name TEXT NOT NULL DEFAULT ''");
            _context.Database.ExecuteSqlRaw("ALTER TABLE dialog_encounters ADD COLUMN map_x REAL NOT NULL DEFAULT 0");
            _context.Database.ExecuteSqlRaw("ALTER TABLE dialog_encounters ADD COLUMN map_y REAL NOT NULL DEFAULT 0");

            SetSchemaVersion(3);
        }

        if (version < 4 && hasOldTable)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v4: add last_seen, zone_name, map_x, map_y to character_instances",
                new EKEventId(0, TextSource.None));

            _context.Database.ExecuteSqlRaw("ALTER TABLE character_instances ADD COLUMN last_seen TEXT NOT NULL DEFAULT '0001-01-01'");
            _context.Database.ExecuteSqlRaw("ALTER TABLE character_instances ADD COLUMN zone_name TEXT NOT NULL DEFAULT ''");
            _context.Database.ExecuteSqlRaw("ALTER TABLE character_instances ADD COLUMN map_x REAL NOT NULL DEFAULT 0");
            _context.Database.ExecuteSqlRaw("ALTER TABLE character_instances ADD COLUMN map_y REAL NOT NULL DEFAULT 0");

            // Backfill last_seen from first_seen
            _context.Database.ExecuteSqlRaw("UPDATE character_instances SET last_seen = first_seen");

            SetSchemaVersion(4);
        }

        if (version < 5)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v5: rename dialog_encounters table to voice_clips",
                new EKEventId(0, TextSource.None));

            // Only rename if old table exists (upgrades); new installs already have voice_clips
            try { _context.Database.ExecuteSqlRaw("ALTER TABLE dialog_encounters RENAME TO voice_clips"); }
            catch { /* Table already named voice_clips on fresh install */ }

            SetSchemaVersion(5);
        }

        if (version < 6)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v6: add save_path to voice_clips",
                new EKEventId(0, TextSource.None));

            try { _context.Database.ExecuteSqlRaw("ALTER TABLE voice_clips ADD COLUMN save_path TEXT NOT NULL DEFAULT ''"); }
            catch { /* already exists on fresh install */ }

            SetSchemaVersion(6);
        }

        if (version < 7)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v7: add is_adult_voice, is_elder_voice to voices; add language to characters",
                new EKEventId(0, TextSource.None));

            // On fresh installs, EnsureCreated already creates these columns — use ALTER only on upgrades
            try { _context.Database.ExecuteSqlRaw("ALTER TABLE voices ADD COLUMN is_adult_voice INTEGER NOT NULL DEFAULT 1"); } catch { /* already exists */ }
            try { _context.Database.ExecuteSqlRaw("ALTER TABLE voices ADD COLUMN is_elder_voice INTEGER NOT NULL DEFAULT 0"); } catch { /* already exists */ }
            try { _context.Database.ExecuteSqlRaw("ALTER TABLE characters ADD COLUMN language INTEGER NOT NULL DEFAULT 1"); } catch { /* already exists */ }

            // Update unique index to include language (only needed on upgrade — fresh installs have the 4-column index)
            _context.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_characters_Name_Gender_Race");
            try { _context.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IX_characters_Name_Gender_Race_Language ON characters (name, gender, race, language)"); } catch { /* already exists */ }

            // Composite index for voice clip upsert lookup performance
            try { _context.Database.ExecuteSqlRaw("CREATE INDEX IX_voice_clips_CharacterId_NpcBaseId_OriginalText ON voice_clips (character_id, npc_base_id, original_text)"); } catch { /* already exists */ }

            SetSchemaVersion(7);
        }

        if (version < 8)
        {
            _log.Info(nameof(RunSchemaMigrations),
                "Applying schema v8: voice_clip_generations table, has_player_placeholder column",
                new EKEventId(0, TextSource.None));

            // Create the per-player generation tracking table
            _context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS voice_clip_generations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    voice_clip_id INTEGER NOT NULL,
                    player_content_id INTEGER NOT NULL DEFAULT 0,
                    player_name TEXT NOT NULL DEFAULT '',
                    save_path TEXT NOT NULL DEFAULT '',
                    generated_at TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY (voice_clip_id) REFERENCES voice_clips(id) ON DELETE CASCADE
                )");

            // Indexes
            try { _context.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IX_voice_clip_generations_VoiceClipId_PlayerContentId ON voice_clip_generations (voice_clip_id, player_content_id)"); }
            catch { /* already exists */ }
            try { _context.Database.ExecuteSqlRaw(
                "CREATE INDEX IX_voice_clip_generations_PlayerContentId ON voice_clip_generations (player_content_id)"); }
            catch { /* already exists */ }

            // Add has_player_placeholder to voice_clips
            try { _context.Database.ExecuteSqlRaw(
                "ALTER TABLE voice_clips ADD COLUMN has_player_placeholder INTEGER NOT NULL DEFAULT 0"); }
            catch { /* already exists on fresh install */ }

            // Migrate existing saved_to_disk data → voice_clip_generations (player-independent, content_id=0)
            _context.Database.ExecuteSqlRaw(@"
                INSERT OR IGNORE INTO voice_clip_generations (voice_clip_id, player_content_id, player_name, save_path, generated_at)
                SELECT id, 0, '', save_path, timestamp
                FROM voice_clips
                WHERE saved_to_disk = 1");

            // Backfill has_player_placeholder for any already-harvested clips with placeholders
            _context.Database.ExecuteSqlRaw(@"
                UPDATE voice_clips SET has_player_placeholder = 1
                WHERE original_text LIKE '%-PlayerFirstName-%'
                   OR original_text LIKE '%-PlayerLastName-%'
                   OR original_text LIKE '%-PlayerName-%'");

            SetSchemaVersion(8);
        }

        if (version < 9)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v9: quest_type column on voice_clips",
                new EKEventId(0, TextSource.None));
            try { _context.Database.ExecuteSqlRaw(
                "ALTER TABLE voice_clips ADD COLUMN quest_type INTEGER NOT NULL DEFAULT 0"); }
            catch { /* already exists on fresh install */ }
            try { _context.Database.ExecuteSqlRaw(
                "CREATE INDEX IX_voice_clips_QuestType ON voice_clips (quest_type)"); }
            catch { /* already exists */ }
            SetSchemaVersion(9);
        }

        if (version < 10)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v10: drop do_not_delete column from characters",
                new EKEventId(0, TextSource.None));
            try { _context.Database.ExecuteSqlRaw(
                "ALTER TABLE characters DROP COLUMN do_not_delete"); }
            catch { /* already dropped on fresh install or pre-v9 install never had it persisted */ }
            SetSchemaVersion(10);
        }

        if (version < 11)
        {
            _log.Info(nameof(RunSchemaMigrations), "Applying schema v11: world column on characters + lodestone_lookups cache table",
                new EKEventId(0, TextSource.None));
            try { _context.Database.ExecuteSqlRaw(
                "ALTER TABLE characters ADD COLUMN world TEXT NOT NULL DEFAULT ''"); }
            catch { /* already exists on fresh install */ }
            try { _context.Database.ExecuteSqlRaw(
                @"CREATE TABLE IF NOT EXISTS lodestone_lookups (
                    name TEXT NOT NULL,
                    world TEXT NOT NULL,
                    race INTEGER NOT NULL DEFAULT 0,
                    gender INTEGER NOT NULL DEFAULT 0,
                    fetched_at TEXT NOT NULL,
                    found INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY (name, world)
                )"); }
            catch { /* already exists */ }
            SetSchemaVersion(11);
        }

        if (version < 12)
        {
            _log.Info(nameof(RunSchemaMigrations),
                "Applying schema v12: merge case-duplicate character rows, recreate name index COLLATE NOCASE",
                new EKEventId(0, TextSource.None));
            MergeCaseDuplicateCharacters();
            try { _context.Database.ExecuteSqlRaw(
                "DROP INDEX IF EXISTS IX_characters_name_gender_race_language"); }
            catch { /* not present */ }
            try { _context.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IX_characters_name_gender_race_language ON characters (name COLLATE NOCASE, gender, race, language)"); }
            catch { /* already created with NOCASE on fresh install */ }
            SetSchemaVersion(12);
        }

        if (version < 13)
        {
            _log.Info(nameof(RunSchemaMigrations),
                "Applying schema v13: alias_gender column on voice_clip_generations + recreate unique index",
                new EKEventId(0, TextSource.None));
            try { _context.Database.ExecuteSqlRaw(
                "ALTER TABLE voice_clip_generations ADD COLUMN alias_gender INTEGER NOT NULL DEFAULT 0"); }
            catch { /* already exists on fresh install */ }
            try { _context.Database.ExecuteSqlRaw(
                "DROP INDEX IF EXISTS IX_voice_clip_generations_VoiceClipId_PlayerContentId"); }
            catch { /* not present */ }
            try { _context.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX IX_voice_clip_generations_VoiceClipId_PlayerContentId_AliasGender ON voice_clip_generations (voice_clip_id, player_content_id, alias_gender)"); }
            catch { /* already created on fresh install */ }
            SetSchemaVersion(13);
        }
    }

    /// <summary>
    /// Pre-v12 the characters unique index was case-sensitive on (name, gender, race, language),
    /// so harvested German adjective stems like "stille Druidin" coexisted with the runtime
    /// display "Stille Druidin" as two distinct rows. Merges them: voice clips, instances,
    /// contexts get re-parented to the winner; conflicting child rows on the loser side are
    /// dropped. Winner = row with non-empty voice_key, then the one whose name already starts
    /// uppercase, then highest object_kind, then lowest id. After merge the winner's name is
    /// title-cased so the final state is canonical.
    /// </summary>
    internal void MergeCaseDuplicateCharacters()
    {
        // 1. Collect (winnerId, loserId) pairs and the canonical name to apply to the winner.
        var merges = new List<(int winnerId, int loserId, string canonicalName)>();
        var winnerNames = new Dictionary<int, string>();
        var conn = _context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
WITH grouped AS (
    SELECT id, name, gender, race, language, voice_key, object_kind,
           ROW_NUMBER() OVER (
               PARTITION BY LOWER(name), gender, race, language
               ORDER BY CASE WHEN voice_key != '' THEN 0 ELSE 1 END,
                        CASE WHEN substr(name, 1, 1) = upper(substr(name, 1, 1)) THEN 0 ELSE 1 END,
                        object_kind DESC,
                        id ASC
           ) AS rnk,
           COUNT(*) OVER (PARTITION BY LOWER(name), gender, race, language) AS grp_size
      FROM characters
)
SELECT g.id AS loser_id, g.name AS loser_name,
       w.id AS winner_id, w.name AS winner_name
  FROM grouped g
  JOIN grouped w
    ON LOWER(w.name) = LOWER(g.name)
   AND w.gender = g.gender AND w.race = g.race AND w.language = g.language
   AND w.rnk = 1
 WHERE g.grp_size > 1 AND g.rnk > 1";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var loserId = reader.GetInt32(0);
                var winnerId = reader.GetInt32(2);
                var winnerName = reader.GetString(3);
                merges.Add((winnerId, loserId, winnerName));
                winnerNames[winnerId] = winnerName;
            }
        }

        if (merges.Count == 0) return;

        foreach (var (winnerId, loserId, _) in merges)
        {
            // voice_clips: move when winner has no entry for the same (npc_base_id, original_text);
            //              drop the rest (they would violate the composite index).
            _context.Database.ExecuteSqlInterpolated(
                $@"UPDATE voice_clips
                      SET character_id = {winnerId}
                    WHERE character_id = {loserId}
                      AND NOT EXISTS (
                        SELECT 1 FROM voice_clips v2
                         WHERE v2.character_id = {winnerId}
                           AND v2.npc_base_id = voice_clips.npc_base_id
                           AND v2.original_text = voice_clips.original_text
                      )");
            _context.Database.ExecuteSqlInterpolated(
                $"DELETE FROM voice_clips WHERE character_id = {loserId}");

            // character_instances: composite PK (character_id, npc_base_id) — same conflict rule.
            _context.Database.ExecuteSqlInterpolated(
                $@"UPDATE character_instances
                      SET character_id = {winnerId}
                    WHERE character_id = {loserId}
                      AND NOT EXISTS (
                        SELECT 1 FROM character_instances ci2
                         WHERE ci2.character_id = {winnerId}
                           AND ci2.npc_base_id = character_instances.npc_base_id
                      )");
            _context.Database.ExecuteSqlInterpolated(
                $"DELETE FROM character_instances WHERE character_id = {loserId}");

            // character_contexts: unique (character_id, context_type).
            _context.Database.ExecuteSqlInterpolated(
                $@"UPDATE character_contexts
                      SET character_id = {winnerId}
                    WHERE character_id = {loserId}
                      AND NOT EXISTS (
                        SELECT 1 FROM character_contexts cc2
                         WHERE cc2.character_id = {winnerId}
                           AND cc2.context_type = character_contexts.context_type
                      )");
            _context.Database.ExecuteSqlInterpolated(
                $"DELETE FROM character_contexts WHERE character_id = {loserId}");

            _context.Database.ExecuteSqlInterpolated(
                $"DELETE FROM characters WHERE id = {loserId}");
        }

        // Force the winner's name to title case so the canonical form survives.
        foreach (var (winnerId, currentName) in winnerNames)
        {
            if (string.IsNullOrEmpty(currentName)) continue;
            var titled = char.ToUpperInvariant(currentName[0]) + currentName.Substring(1);
            if (titled == currentName) continue;
            _context.Database.ExecuteSqlInterpolated(
                $"UPDATE characters SET name = {titled} WHERE id = {winnerId}");
        }

        _log.Info(nameof(MergeCaseDuplicateCharacters),
            $"Merged {merges.Count} case-duplicate character row(s)",
            new EKEventId(0, TextSource.None));
    }

    private void SetSchemaVersion(int version)
    {
        _context.Database.ExecuteSqlRaw("DELETE FROM schema_version");
        _context.Database.ExecuteSqlRaw("INSERT INTO schema_version (version) VALUES ({0})", version);
    }

    // ── Migration ───────────────────────────────────────────

    public bool NeedsMigration(Configuration config)
    {
        lock (_writeLock)
        {
            var hasDbData = _context.Characters.Any() || _context.Voices.Any();
            var hasConfigData = config.MappedNpcs.Count > 0
                                || config.MappedPlayers.Count > 0
                                || config.EchokrautVoices.Count > 0
                                || config.PhoneticCorrections.Count > 0;

            return !hasDbData && hasConfigData;
        }
    }

    public void MigrateFromConfig(Configuration config)
    {
        lock (_writeLock)
        {
            var supportsTransactions = _context.Database.ProviderName?.Contains("Sqlite") == true;
            var transaction = supportsTransactions ? _context.Database.BeginTransaction() : null;
            try
            {
                // Migrate voices first (characters reference voice_key)
                foreach (var voice in config.EchokrautVoices)
                {
                    var entity = new VoiceEntity
                    {
                        BackendVoice = voice.BackendVoice ?? "",
                        VoiceName = voice.voiceName ?? "",
                        IsDefault = voice.IsDefault,
                        IsEnabled = voice.IsEnabled,
                        UseAsRandom = voice.UseAsRandom,
                        IsAdultVoice = voice.IsAdultVoice,
                        IsChildVoice = voice.IsChildVoice,
                        IsElderVoice = voice.IsElderVoice,
                        Volume = voice.Volume,
                        Note = voice.Note ?? ""
                    };
                    _context.Voices.Add(entity);
                    _context.SaveChanges();

                    foreach (var g in voice.AllowedGenders)
                        _context.VoiceAllowedGenders.Add(new VoiceAllowedGenderEntity
                        {
                            VoiceId = entity.Id,
                            Gender = (int)g
                        });

                    foreach (var r in voice.AllowedRaces)
                        _context.VoiceAllowedRaces.Add(new VoiceAllowedRaceEntity
                        {
                            VoiceId = entity.Id,
                            Race = (int)r
                        });
                }

                _context.SaveChanges();

                // Migrate NPC mappings
                MigrateCharacterList(config.MappedNpcs, "npc");

                // Migrate player mappings
                MigrateCharacterList(config.MappedPlayers, "player");

                // Migrate phonetic corrections
                foreach (var pc in config.PhoneticCorrections)
                {
                    _context.PhoneticCorrections.Add(new PhoneticCorrectionEntity
                    {
                        OriginalText = pc.OriginalText ?? "",
                        CorrectedText = pc.CorrectedText ?? ""
                    });
                }

                // Migrate muted dialogues into character_instances
                // These are just base IDs without character association — we'll create placeholder instances
                foreach (var baseId in config.MutedNpcDialogues)
                {
                    // Find or create a character for this muted instance
                    var existing = _context.CharacterInstances
                        .FirstOrDefault(ci => ci.NpcBaseId == (long)baseId);
                    if (existing != null)
                    {
                        existing.IsMuted = true;
                    }
                    // If no character instance exists, we can't create one without character info
                    // These will be recreated when the NPC is encountered again
                }

                _context.SaveChanges();
                transaction?.Commit();

                // Clear config data after successful migration
                config.MappedNpcs.Clear();
                config.MappedPlayers.Clear();
                config.EchokrautVoices.Clear();
                config.PhoneticCorrections.Clear();
                config.MutedNpcDialogues.Clear();
                config.Save();

                _log.Info(nameof(MigrateFromConfig), "Migration complete.",
                    new EKEventId(0, TextSource.None));

                RefreshAllCaches();
            }
            catch (Exception ex)
            {
                transaction?.Rollback();
                _log.Error(nameof(MigrateFromConfig), $"Migration failed: {ex}",
                    new EKEventId(0, TextSource.None));
                throw;
            }
        }
    }

    private void MigrateCharacterList(List<NpcMapData> mappings, string contextType)
    {
        foreach (var npc in mappings)
        {
            var character = new CharacterEntity
            {
                Name = npc.Name ?? "",
                Race = (int)npc.Race,
                RaceStr = npc.RaceStr ?? "",
                Gender = (int)npc.Gender,
                BodyType = (int)npc.BodyType,
                VoiceKey = npc.voice ?? "",
                ObjectKind = (int)npc.ObjectKind
            };
            _context.Characters.Add(character);
            _context.SaveChanges();

            // Create context for the primary type
            _context.CharacterContexts.Add(new CharacterContextEntity
            {
                CharacterId = character.Id,
                ContextType = contextType,
                IsEnabled = npc.IsEnabled,
                Volume = npc.Volume
            });

            // If NPC has bubbles, create a bubble context too
            if (npc.HasBubbles && contextType == "npc")
            {
                _context.CharacterContexts.Add(new CharacterContextEntity
                {
                    CharacterId = character.Id,
                    ContextType = "bubble",
                    IsEnabled = npc.IsEnabledBubble,
                    Volume = npc.VolumeBubble
                });
            }
        }

        _context.SaveChanges();
    }

    // ── Characters ──────────────────────────────────────────

    public List<CharacterEntity> GetNpcs() => _cachedNpcs;
    public List<CharacterEntity> GetPlayers() => _cachedPlayers;

    public List<CharacterEntity> GetAllCharacters()
    {
        lock (_writeLock)
        {
            return _context.Characters.AsNoTracking().ToList();
        }
    }

    public CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race, int language)
    {
        lock (_writeLock)
        {
            // Match the unique index's COLLATE NOCASE so harvest stems ("stille") and runtime
            // display names ("Stille") resolve to the same row.
            return _context.Characters
                .Include(c => c.Contexts)
                .FirstOrDefault(c =>
                    EF.Functions.Collate(c.Name, "NOCASE") == name
                    && c.Gender == (int)gender && c.Race == (int)race && c.Language == language);
        }
    }

    public CharacterEntity UpsertCharacter(CharacterEntity character)
    {
        lock (_writeLock)
        {
            var existing = _context.Characters
                .FirstOrDefault(c =>
                    EF.Functions.Collate(c.Name, "NOCASE") == character.Name
                    && c.Gender == character.Gender
                    && c.Race == character.Race
                    && c.Language == character.Language);

            if (existing != null)
            {
                existing.RaceStr = character.RaceStr;
                existing.BodyType = character.BodyType;
                // Don't overwrite an existing voice with an empty one
                if (!string.IsNullOrEmpty(character.VoiceKey))
                    existing.VoiceKey = character.VoiceKey;
                existing.ObjectKind = character.ObjectKind;
            }
            else
            {
                _context.Characters.Add(character);
            }

            _context.SaveChanges();

            if (!BulkMode) RefreshCharacterCaches();
            return existing ?? character;
        }
    }

    public void DeleteCharacter(int characterId)
    {
        lock (_writeLock)
        {
            var entity = _context.Characters.Find(characterId);
            if (entity != null)
            {
                _context.Characters.Remove(entity);
                _context.SaveChanges();
                if (!BulkMode) RefreshCharacterCaches();
            }
        }
    }

    // ── Character Contexts ──────────────────────────────────

    public CharacterContextEntity? GetContext(int characterId, string contextType)
    {
        lock (_writeLock)
        {
            return _context.CharacterContexts
                .FirstOrDefault(cc => cc.CharacterId == characterId && cc.ContextType == contextType);
        }
    }

    public CharacterContextEntity UpsertContext(int characterId, string contextType,
        bool isEnabled = true, float volume = 1.0f)
    {
        lock (_writeLock)
        {
            var existing = _context.CharacterContexts
                .FirstOrDefault(cc => cc.CharacterId == characterId && cc.ContextType == contextType);

            if (existing != null)
            {
                existing.IsEnabled = isEnabled;
                existing.Volume = volume;
            }
            else
            {
                existing = new CharacterContextEntity
                {
                    CharacterId = characterId,
                    ContextType = contextType,
                    IsEnabled = isEnabled,
                    Volume = volume
                };
                _context.CharacterContexts.Add(existing);
            }

            _context.SaveChanges();
            if (!BulkMode) RefreshCharacterCaches();
            return existing;
        }
    }

    // ── Character Instances ─────────────────────────────────

    public CharacterInstanceEntity GetOrCreateInstance(int characterId, uint npcBaseId,
        string zoneName = "", float mapX = 0, float mapY = 0)
    {
        lock (_writeLock)
        {
            var existing = _context.CharacterInstances
                .FirstOrDefault(ci => ci.CharacterId == characterId && ci.NpcBaseId == (long)npcBaseId);

            if (existing != null)
            {
                // Update location and last_seen on re-encounter
                existing.LastSeen = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(zoneName))
                {
                    existing.ZoneName = zoneName;
                    existing.MapX = mapX;
                    existing.MapY = mapY;
                }
                _context.SaveChanges();
                return existing;
            }

            existing = new CharacterInstanceEntity
            {
                CharacterId = characterId,
                NpcBaseId = (long)npcBaseId,
                FirstSeen = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow,
                ZoneName = zoneName,
                MapX = mapX,
                MapY = mapY
            };
            _context.CharacterInstances.Add(existing);
            _context.SaveChanges();
            return existing;
        }
    }

    public List<CharacterInstanceEntity> GetInstancesForCharacter(int characterId)
    {
        lock (_writeLock)
        {
            return _context.CharacterInstances
                .Where(ci => ci.CharacterId == characterId)
                .ToList();
        }
    }

    public void MuteInstance(uint npcBaseId)
    {
        lock (_writeLock)
        {
            var instances = _context.CharacterInstances
                .Where(ci => ci.NpcBaseId == (long)npcBaseId)
                .ToList();

            foreach (var inst in instances)
                inst.IsMuted = true;

            _context.SaveChanges();
            RefreshMutedCache();
        }
    }

    public void UnmuteInstance(uint npcBaseId)
    {
        lock (_writeLock)
        {
            var instances = _context.CharacterInstances
                .Where(ci => ci.NpcBaseId == (long)npcBaseId)
                .ToList();

            foreach (var inst in instances)
                inst.IsMuted = false;

            _context.SaveChanges();
            RefreshMutedCache();
        }
    }

    public void ClearInstanceMutes()
    {
        lock (_writeLock)
        {
            var muted = _context.CharacterInstances.Where(ci => ci.IsMuted).ToList();
            foreach (var inst in muted)
                inst.IsMuted = false;

            _context.SaveChanges();
            RefreshMutedCache();
        }
    }

    public HashSet<uint> GetMutedBaseIds() => _cachedMutedBaseIds;

    // ── Voices ──────────────────────────────────────────────

    public List<VoiceEntity> GetVoices() => _cachedVoices;

    public VoiceEntity? GetVoiceByKey(string backendVoice)
    {
        lock (_writeLock)
        {
            return _context.Voices
                .Include(v => v.AllowedGenders)
                .Include(v => v.AllowedRaces)
                .FirstOrDefault(v => v.BackendVoice == backendVoice);
        }
    }

    public VoiceEntity UpsertVoice(VoiceEntity voice)
    {
        lock (_writeLock)
        {
            var existing = _context.Voices
                .Include(v => v.AllowedGenders)
                .Include(v => v.AllowedRaces)
                .FirstOrDefault(v => v.BackendVoice == voice.BackendVoice);

            if (existing != null)
            {
                existing.VoiceName = voice.VoiceName;
                existing.IsDefault = voice.IsDefault;
                existing.IsEnabled = voice.IsEnabled;
                existing.UseAsRandom = voice.UseAsRandom;
                existing.IsAdultVoice = voice.IsAdultVoice;
                existing.IsChildVoice = voice.IsChildVoice;
                existing.IsElderVoice = voice.IsElderVoice;
                existing.Volume = voice.Volume;
                existing.Note = voice.Note;

                // Update junction tables
                existing.AllowedGenders.Clear();
                existing.AllowedGenders.AddRange(voice.AllowedGenders.Select(g =>
                    new VoiceAllowedGenderEntity { VoiceId = existing.Id, Gender = g.Gender }));

                existing.AllowedRaces.Clear();
                existing.AllowedRaces.AddRange(voice.AllowedRaces.Select(r =>
                    new VoiceAllowedRaceEntity { VoiceId = existing.Id, Race = r.Race }));
            }
            else
            {
                _context.Voices.Add(voice);
            }

            _context.SaveChanges();
            RefreshVoiceCache();
            return existing ?? voice;
        }
    }

    public void DeleteVoice(string backendVoice)
    {
        lock (_writeLock)
        {
            var entity = _context.Voices.FirstOrDefault(v => v.BackendVoice == backendVoice);
            if (entity != null)
            {
                _context.Voices.Remove(entity);
                _context.SaveChanges();
                RefreshVoiceCache();
            }
        }
    }

    // ── Phonetic Corrections ────────────────────────────────

    public List<PhoneticCorrectionEntity> GetPhoneticCorrections() => _cachedPhonetics;

    public void UpsertPhoneticCorrection(string originalText, string correctedText)
    {
        lock (_writeLock)
        {
            var existing = _context.PhoneticCorrections
                .FirstOrDefault(p => p.OriginalText == originalText);

            if (existing != null)
            {
                existing.CorrectedText = correctedText;
            }
            else
            {
                _context.PhoneticCorrections.Add(new PhoneticCorrectionEntity
                {
                    OriginalText = originalText,
                    CorrectedText = correctedText
                });
            }

            _context.SaveChanges();
            RefreshPhoneticCache();
        }
    }

    public void DeletePhoneticCorrection(string originalText)
    {
        lock (_writeLock)
        {
            var entity = _context.PhoneticCorrections
                .FirstOrDefault(p => p.OriginalText == originalText);
            if (entity != null)
            {
                _context.PhoneticCorrections.Remove(entity);
                _context.SaveChanges();
                RefreshPhoneticCache();
            }
        }
    }

    // ── Dialog Encounters ───────────────────────────────────

    /// <summary>
    /// When true, VoiceClipLogged events are suppressed. Use during batch operations
    /// (e.g. harvest) to prevent UI threads from querying the DB concurrently.
    /// Call NotifyVoiceClipLogged() once after the batch completes.
    /// </summary>
    public bool SuppressEvents { get; set; }

    /// <inheritdoc/>
    public bool BulkMode { get; set; }

    /// <inheritdoc/>
    public void RefreshCaches()
    {
        lock (_writeLock)
        {
            RefreshCharacterCaches();
            RefreshVoiceCache();
            RefreshPhoneticCache();
            RefreshMutedCache();
        }
    }

    public void NotifyVoiceClipLogged() => VoiceClipLogged?.Invoke();

    public void ClearChangeTracker()
    {
        lock (_writeLock)
        {
            _context.ChangeTracker.Clear();
        }
    }

    public event Action? VoiceClipLogged;

    public void LogVoiceClip(VoiceClipEntity voiceClip)
    {
        lock (_writeLock)
        {
            _context.VoiceClips.Add(voiceClip);
            _context.SaveChanges();
            if (!SuppressEvents) VoiceClipLogged?.Invoke();
        }
    }

    public VoiceClipEntity LogOrUpdateVoiceClip(VoiceClipEntity voiceClip)
    {
        lock (_writeLock)
        {
            var existing = _context.VoiceClips
                .FirstOrDefault(vc => vc.CharacterId == voiceClip.CharacterId
                    && vc.NpcBaseId == voiceClip.NpcBaseId
                    && vc.OriginalText == voiceClip.OriginalText);

            VoiceClipEntity result;
            if (existing != null)
            {
                existing.Timestamp = voiceClip.Timestamp;
                existing.VoiceKey = voiceClip.VoiceKey;
                existing.CleanedText = voiceClip.CleanedText;
                existing.BodyType = voiceClip.BodyType;
                existing.HasPlayerPlaceholder = voiceClip.HasPlayerPlaceholder;
                if (voiceClip.QuestType != 0)
                    existing.QuestType = voiceClip.QuestType;
                existing.ZoneName = voiceClip.ZoneName;
                existing.MapX = voiceClip.MapX;
                existing.MapY = voiceClip.MapY;
                // Don't overwrite SavedToDisk/SavePath with empty values on re-encounter
                if (voiceClip.SavedToDisk || !string.IsNullOrEmpty(voiceClip.SavePath))
                {
                    existing.SavedToDisk = voiceClip.SavedToDisk;
                    existing.SavePath = voiceClip.SavePath;
                }
                result = existing;
            }
            else
            {
                _context.VoiceClips.Add(voiceClip);
                result = voiceClip;
            }

            // In batch mode (SuppressEvents), skip per-item SaveChanges — caller flushes in batches
            if (!SuppressEvents)
            {
                _context.SaveChanges();
                VoiceClipLogged?.Invoke();
            }

            return result;
        }
    }

    /// <summary>
    /// Flush pending DB changes. Call periodically during batch operations.
    /// </summary>
    public void FlushChanges()
    {
        lock (_writeLock)
        {
            _context.SaveChanges();
        }
    }

    public List<VoiceClipEntity> GetVoiceClips(int limit = 1000, int offset = 0,
        string? npcNameFilter = null, string? textFilter = null,
        int? textSourceFilter = null, bool? savedFilter = null)
    {
        lock (_writeLock)
        {
            return ApplyEncounterFilters(npcNameFilter, textFilter, textSourceFilter, savedFilter)
                .Include(e => e.Character)
                .OrderByDescending(e => e.Timestamp)
                .Skip(offset)
                .Take(limit)
                .ToList();
        }
    }

    public int GetVoiceClipCount(string? npcNameFilter = null, string? textFilter = null,
        int? textSourceFilter = null, bool? savedFilter = null)
    {
        lock (_writeLock)
        {
            return ApplyEncounterFilters(npcNameFilter, textFilter, textSourceFilter, savedFilter).Count();
        }
    }

    public List<CharacterEntity> GetCharactersWithVoiceClips()
    {
        lock (_writeLock)
        {
            var characterIds = _context.VoiceClips
                .Select(e => e.CharacterId)
                .Distinct()
                .ToList();

            return _context.Characters
                .Include(c => c.Contexts)
                .Where(c => characterIds.Contains(c.Id))
                .OrderBy(c => c.Name)
                .ToList();
        }
    }

    public List<VoiceClipEntity> GetVoiceClipsForCharacter(int characterId, int limit = 1000, int offset = 0)
    {
        lock (_writeLock)
        {
            return _context.VoiceClips
                .Include(e => e.Character)
                .Where(e => e.CharacterId == characterId)
                .OrderByDescending(e => e.Timestamp)
                .Skip(offset)
                .Take(limit)
                .ToList();
        }
    }

    public int GetVoiceClipCountForCharacter(int characterId, int? questTypeFilter = null)
    {
        lock (_writeLock)
        {
            var q = _context.VoiceClips.Where(e => e.CharacterId == characterId);
            if (questTypeFilter.HasValue)
                q = q.Where(e => e.QuestType == questTypeFilter.Value);
            return q.Count();
        }
    }

    public HashSet<int> GetCharacterIdsWithQuestType(int questType)
    {
        lock (_writeLock)
        {
            return _context.VoiceClips
                .Where(vc => vc.QuestType == questType)
                .Select(vc => vc.CharacterId)
                .Distinct()
                .ToHashSet();
        }
    }

    public int GetSavedVoiceClipCountForCharacter(int characterId)
    {
        lock (_writeLock)
        {
            return _context.VoiceClips.Count(e => e.CharacterId == characterId && e.SavedToDisk);
        }
    }

    public void UpdateVoiceClipSaved(int voiceClipId, bool savedToDisk, string savePath)
    {
        lock (_writeLock)
        {
            var entity = _context.VoiceClips.Find(voiceClipId);
            if (entity != null)
            {
                entity.SavedToDisk = savedToDisk;
                entity.SavePath = savePath;
                _context.SaveChanges();
            }
        }
    }

    public void UpdateVoiceClipVoiceKey(int voiceClipId, string voiceKey)
    {
        lock (_writeLock)
        {
            var entity = _context.VoiceClips.Find(voiceClipId);
            if (entity != null && entity.VoiceKey != voiceKey)
            {
                entity.VoiceKey = voiceKey;
                _context.SaveChanges();
            }
        }
    }

    public void DeleteVoiceClip(int voiceClipId)
    {
        lock (_writeLock)
        {
            var entity = _context.VoiceClips.Find(voiceClipId);
            if (entity != null)
            {
                _context.VoiceClips.Remove(entity);
                _context.SaveChanges();
            }
        }
    }

    public void ClearVoiceClips()
    {
        lock (_writeLock)
        {
            _context.VoiceClips.RemoveRange(_context.VoiceClips);
            _context.SaveChanges();
        }
    }

    public event Action? DatabaseWiped;

    public void WipeAll()
    {
        lock (_writeLock)
        {
            // Order matters for FK constraints: dependent rows first.
            _context.VoiceClipGenerations.RemoveRange(_context.VoiceClipGenerations);
            _context.VoiceClips.RemoveRange(_context.VoiceClips);
            _context.CharacterInstances.RemoveRange(_context.CharacterInstances);
            _context.CharacterContexts.RemoveRange(_context.CharacterContexts);
            _context.Characters.RemoveRange(_context.Characters);
            _context.PhoneticCorrections.RemoveRange(_context.PhoneticCorrections);
            // Voices have child tables (allowed_races / allowed_genders) — EF cascades via relationship.
            _context.Voices.RemoveRange(_context.Voices);
            _context.SaveChanges();
            _context.ChangeTracker.Clear();

            _cachedVoices = new List<VoiceEntity>();
            _cachedPhonetics = new List<PhoneticCorrectionEntity>();
            _cachedMutedBaseIds = new HashSet<uint>();
        }
        if (!SuppressEvents)
        {
            VoiceClipLogged?.Invoke();
            DatabaseWiped?.Invoke();
        }
    }

    // ── Per-player generation tracking ──────────────────────────

    public void LogVoiceClipGeneration(int voiceClipId, long playerContentId, string playerName, string savePath, int aliasGender = 0)
    {
        lock (_writeLock)
        {
            var existing = _context.VoiceClipGenerations
                .FirstOrDefault(g => g.VoiceClipId == voiceClipId
                                  && g.PlayerContentId == playerContentId
                                  && g.AliasGender == aliasGender);
            if (existing != null)
            {
                existing.PlayerName = playerName;
                existing.SavePath = savePath;
                existing.GeneratedAt = DateTime.UtcNow;
            }
            else
            {
                _context.VoiceClipGenerations.Add(new VoiceClipGenerationEntity
                {
                    VoiceClipId = voiceClipId,
                    PlayerContentId = playerContentId,
                    PlayerName = playerName,
                    SavePath = savePath,
                    GeneratedAt = DateTime.UtcNow,
                    AliasGender = aliasGender,
                });
            }
            _context.SaveChanges();
        }
    }

    public void DeleteVoiceClipGeneration(int voiceClipId, long playerContentId, int aliasGender = 0)
    {
        lock (_writeLock)
        {
            var existing = _context.VoiceClipGenerations
                .FirstOrDefault(g => g.VoiceClipId == voiceClipId
                                  && g.PlayerContentId == playerContentId
                                  && g.AliasGender == aliasGender);
            if (existing != null)
            {
                _context.VoiceClipGenerations.Remove(existing);
                _context.SaveChanges();
            }
        }
    }

    public VoiceClipGenerationEntity? GetVoiceClipGeneration(int voiceClipId, long playerContentId, int aliasGender = 0)
    {
        lock (_writeLock)
        {
            return _context.VoiceClipGenerations
                .FirstOrDefault(g => g.VoiceClipId == voiceClipId
                                  && g.PlayerContentId == playerContentId
                                  && g.AliasGender == aliasGender);
        }
    }

    public int GetGeneratedCountForCharacter(int characterId, long playerContentId, int? questTypeFilter = null)
    {
        lock (_writeLock)
        {
            var q = _context.VoiceClipGenerations
                .Where(g => g.VoiceClip != null
                    && g.VoiceClip.CharacterId == characterId
                    && g.PlayerContentId == (g.VoiceClip.HasPlayerPlaceholder ? playerContentId : 0));
            if (questTypeFilter.HasValue)
                q = q.Where(g => g.VoiceClip!.QuestType == questTypeFilter.Value);
            return q.Count();
        }
    }

    public (int totalClips, int generatedClips) GetClipTotalsForLanguage(
        int language, string contextType, long playerContentId, int? questTypeFilter = null)
    {
        lock (_writeLock)
        {
            // Characters of this language that have a context of the requested type.
            var charIds = _context.Characters
                .Where(c => c.Language == language && c.Contexts.Any(ctx => ctx.ContextType == contextType))
                .Select(c => c.Id);

            var clipsQ = _context.VoiceClips.Where(vc => charIds.Contains(vc.CharacterId));
            if (questTypeFilter.HasValue)
                clipsQ = clipsQ.Where(vc => vc.QuestType == questTypeFilter.Value);
            var totalClips = clipsQ.Count();

            var genQ = _context.VoiceClipGenerations
                .Where(g => g.VoiceClip != null
                    && charIds.Contains(g.VoiceClip.CharacterId)
                    && g.PlayerContentId == (g.VoiceClip.HasPlayerPlaceholder ? playerContentId : 0));
            if (questTypeFilter.HasValue)
                genQ = genQ.Where(g => g.VoiceClip!.QuestType == questTypeFilter.Value);
            var generatedClips = genQ.Count();

            return (totalClips, generatedClips);
        }
    }

    private IQueryable<VoiceClipEntity> ApplyEncounterFilters(
        string? npcNameFilter, string? textFilter, int? textSourceFilter, bool? savedFilter)
    {
        IQueryable<VoiceClipEntity> query = _context.VoiceClips;

        if (!string.IsNullOrEmpty(npcNameFilter))
            query = query.Where(e => e.Character != null &&
                e.Character.Name.Contains(npcNameFilter));

        if (!string.IsNullOrEmpty(textFilter))
            query = query.Where(e => e.OriginalText.Contains(textFilter) ||
                e.CleanedText.Contains(textFilter));

        if (textSourceFilter.HasValue)
            query = query.Where(e => e.TextSource == textSourceFilter.Value);

        if (savedFilter.HasValue)
            query = query.Where(e => e.SavedToDisk == savedFilter.Value);

        return query;
    }

    // ── Cache Management ────────────────────────────────────

    private void RefreshAllCaches()
    {
        RefreshCharacterCaches();
        RefreshVoiceCache();
        RefreshPhoneticCache();
        RefreshMutedCache();
    }

    private void RefreshCharacterCaches()
    {
        _cachedNpcs = _context.Characters
            .Include(c => c.Contexts)
            .Where(c => c.Contexts.Any(ctx => ctx.ContextType == "npc"))
            .AsNoTracking()
            .ToList();

        _cachedPlayers = _context.Characters
            .Include(c => c.Contexts)
            .Where(c => c.Contexts.Any(ctx => ctx.ContextType == "player"))
            .AsNoTracking()
            .ToList();
    }

    private void RefreshVoiceCache()
    {
        _cachedVoices = _context.Voices
            .Include(v => v.AllowedGenders)
            .Include(v => v.AllowedRaces)
            .AsNoTracking()
            .ToList();
    }

    private void RefreshPhoneticCache()
    {
        _cachedPhonetics = _context.PhoneticCorrections
            .AsNoTracking()
            .ToList();
    }

    private void RefreshMutedCache()
    {
        _cachedMutedBaseIds = _context.CharacterInstances
            .Where(ci => ci.IsMuted)
            .Select(ci => (uint)ci.NpcBaseId)
            .ToHashSet();
    }

    // ── Lodestone lookup cache ──────────────────────────────

    public LodestoneLookupEntity? GetLodestoneLookup(string name, string world)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world)) return null;
        lock (_writeLock)
        {
            return _context.LodestoneLookups
                .FirstOrDefault(l => l.Name == name && l.World == world);
        }
    }

    public void UpsertLodestoneLookup(string name, string world, NpcRaces race, Genders gender, bool found)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(world)) return;
        lock (_writeLock)
        {
            var existing = _context.LodestoneLookups
                .FirstOrDefault(l => l.Name == name && l.World == world);
            if (existing != null)
            {
                existing.Race = (int)race;
                existing.Gender = (int)gender;
                existing.FetchedAt = DateTime.UtcNow;
                existing.Found = found;
            }
            else
            {
                _context.LodestoneLookups.Add(new LodestoneLookupEntity
                {
                    Name = name,
                    World = world,
                    Race = (int)race,
                    Gender = (int)gender,
                    FetchedAt = DateTime.UtcNow,
                    Found = found,
                });
            }
            _context.SaveChanges();
        }
    }

    // ── Dispose ─────────────────────────────────────────────

    public void Dispose()
    {
        try
        {
            var connection = _context.Database.GetDbConnection();
            _context.Dispose();
            connection.Close();
            connection.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        catch { /* In-memory databases may already be disposed */ }
    }
}
