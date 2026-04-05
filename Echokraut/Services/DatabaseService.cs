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

    private const int CurrentSchemaVersion = 6;

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
    }

    private void SetSchemaVersion(int version)
    {
        _context.Database.ExecuteSqlRaw("DELETE FROM schema_version");
        _context.Database.ExecuteSqlRaw("INSERT INTO schema_version (version) VALUES ({0})", version);
    }

    // ── Migration ───────────────────────────────────────────

    public bool NeedsMigration(Configuration config)
    {
        var hasDbData = _context.Characters.Any() || _context.Voices.Any();
        var hasConfigData = config.MappedNpcs.Count > 0
                            || config.MappedPlayers.Count > 0
                            || config.EchokrautVoices.Count > 0
                            || config.PhoneticCorrections.Count > 0;

        return !hasDbData && hasConfigData;
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
                DoNotDelete = npc.DoNotDelete,
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

    public CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race, int language = 1)
    {
        lock (_writeLock)
        {
            return _context.Characters
                .Include(c => c.Contexts)
                .FirstOrDefault(c => c.Name == name && c.Gender == (int)gender && c.Race == (int)race && c.Language == language);
        }
    }

    public CharacterEntity UpsertCharacter(CharacterEntity character)
    {
        lock (_writeLock)
        {
            var existing = _context.Characters
                .FirstOrDefault(c => c.Name == character.Name
                                     && c.Gender == character.Gender
                                     && c.Race == character.Race
                                     && c.Language == character.Language);

            if (existing != null)
            {
                existing.RaceStr = character.RaceStr;
                existing.BodyType = character.BodyType;
                existing.VoiceKey = character.VoiceKey;
                existing.DoNotDelete = character.DoNotDelete;
                existing.ObjectKind = character.ObjectKind;
            }
            else
            {
                _context.Characters.Add(character);
            }

            _context.SaveChanges();
            RefreshCharacterCaches();
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
                RefreshCharacterCaches();
            }
        }
    }

    // ── Character Contexts ──────────────────────────────────

    public CharacterContextEntity? GetContext(int characterId, string contextType)
    {
        return _context.CharacterContexts
            .FirstOrDefault(cc => cc.CharacterId == characterId && cc.ContextType == contextType);
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
            RefreshCharacterCaches();
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
        return _context.CharacterInstances
            .Where(ci => ci.CharacterId == characterId)
            .ToList();
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
        return _context.Voices
            .Include(v => v.AllowedGenders)
            .Include(v => v.AllowedRaces)
            .FirstOrDefault(v => v.BackendVoice == backendVoice);
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

    public void NotifyVoiceClipLogged() => VoiceClipLogged?.Invoke();

    public void ClearChangeTracker()
    {
        lock (_writeLock)
        {
            _context.ChangeTracker.Clear();
        }
    }

    public event Action? VoiceClipLogged;

    public void LogVoiceClip(VoiceClipEntity encounter)
    {
        lock (_writeLock)
        {
            _context.VoiceClips.Add(encounter);
            _context.SaveChanges();
            if (!SuppressEvents) VoiceClipLogged?.Invoke();
        }
    }

    public void LogOrUpdateVoiceClip(VoiceClipEntity encounter)
    {
        lock (_writeLock)
        {
            var existing = _context.VoiceClips
                .FirstOrDefault(e => e.CharacterId == encounter.CharacterId
                    && e.NpcBaseId == encounter.NpcBaseId
                    && e.OriginalText == encounter.OriginalText);

            if (existing != null)
            {
                existing.Timestamp = encounter.Timestamp;
                existing.VoiceKey = encounter.VoiceKey;
                existing.CleanedText = encounter.CleanedText;
                existing.ZoneName = encounter.ZoneName;
                existing.MapX = encounter.MapX;
                existing.MapY = encounter.MapY;
                // Don't overwrite SavedToDisk/SavePath with empty values on re-encounter
                if (encounter.SavedToDisk || !string.IsNullOrEmpty(encounter.SavePath))
                {
                    existing.SavedToDisk = encounter.SavedToDisk;
                    existing.SavePath = encounter.SavePath;
                }
            }
            else
            {
                _context.VoiceClips.Add(encounter);
            }

            _context.SaveChanges();
            if (!SuppressEvents) VoiceClipLogged?.Invoke();
        }
    }

    public List<VoiceClipEntity> GetVoiceClips(int limit = 1000, int offset = 0,
        string? npcNameFilter = null, string? textFilter = null,
        int? textSourceFilter = null, bool? savedFilter = null)
    {
        return ApplyEncounterFilters(npcNameFilter, textFilter, textSourceFilter, savedFilter)
            .Include(e => e.Character)
            .OrderByDescending(e => e.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public int GetVoiceClipCount(string? npcNameFilter = null, string? textFilter = null,
        int? textSourceFilter = null, bool? savedFilter = null)
    {
        return ApplyEncounterFilters(npcNameFilter, textFilter, textSourceFilter, savedFilter).Count();
    }

    public List<CharacterEntity> GetCharactersWithVoiceClips()
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

    public List<VoiceClipEntity> GetVoiceClipsForCharacter(int characterId, int limit = 1000, int offset = 0)
    {
        return _context.VoiceClips
            .Include(e => e.Character)
            .Where(e => e.CharacterId == characterId)
            .OrderByDescending(e => e.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public int GetVoiceClipCountForCharacter(int characterId)
    {
        return _context.VoiceClips.Count(e => e.CharacterId == characterId);
    }

    public int GetSavedVoiceClipCountForCharacter(int characterId)
    {
        return _context.VoiceClips.Count(e => e.CharacterId == characterId && e.SavedToDisk);
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

    public void DeleteVoiceClip(int encounterId)
    {
        lock (_writeLock)
        {
            var entity = _context.VoiceClips.Find(encounterId);
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
