using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Game;
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
    /// <summary>Client language at construction time. Used as the override for legacy JSON
    /// migration: pre-DB plugin versions didn't track per-entry language so all NpcMapData
    /// entries deserialize as English (the default), which then gets filtered out by the VCM's
    /// language filter on non-English clients. The migration forces this language on every
    /// imported entry to match the user's actual session.</summary>
    private readonly ClientLanguage _clientLanguage;

    // In-memory caches for hot-path reads
    private volatile List<CharacterEntity> _cachedNpcs = new();
    private volatile List<CharacterEntity> _cachedPlayers = new();
    private volatile List<VoiceEntity> _cachedVoices = new();
    private volatile List<PhoneticCorrectionEntity> _cachedPhonetics = new();
    private volatile HashSet<uint> _cachedMutedBaseIds = new();
    /// <summary>(language, normalized-alias) → list of characterIds. Built from
    /// <c>character_speaker_aliases</c>. Multi-valued because some fakenames (notably the
    /// anonymous <c>???</c> marker) are shared across many characters; the live runtime
    /// disambiguates via physical-presence and already-spoken tracking. Normalized =
    /// trimmed + lowercased so the runtime compare is allocation-free.</summary>
    private volatile Dictionary<(int lang, string alias), List<int>> _cachedAliasMap = new();

    /// <summary>
    /// True after <see cref="Dispose"/> has begun. Native addons keep firing OnUpdate during
    /// KamiToolKit's async ATK detach window — those late frames can call DB methods after
    /// the DbContext has already been torn down. Read methods that touch <c>_context</c> check
    /// this flag (under <c>_writeLock</c>) and return null/empty instead of throwing
    /// ObjectDisposedException, so the addon spam dies quietly during plugin reload.
    /// </summary>
    private volatile bool _disposed;

    public DatabaseService(ILogService log, string configDirectory, Configuration config,
        ClientLanguage clientLanguage)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clientLanguage = clientLanguage;
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

    private const int CurrentSchemaVersion = 2;

    private void InitializeDatabase(Configuration config)
    {
        // Enable WAL mode for better concurrent read performance
        _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON");
        _context.Database.EnsureCreated();

        // v2: introduce character_speaker_aliases. EnsureCreated only creates missing tables
        // when the database file is brand new — for existing v1 installs we explicitly add
        // the table + indexes via raw SQL. Idempotent so it's safe to re-run.
        EnsureSpeakerAliasTable();

        RecordSchemaVersion();

        // One-shot legacy import: pull MappedNpcs / MappedPlayers / EchokrautVoices /
        // PhoneticCorrections / MutedNpcDialogues out of the JSON config into SQLite. Idempotent
        // after the first successful run (the migration clears the source lists).
        if (NeedsMigration(config))
        {
            _log.Info(nameof(InitializeDatabase),
                "Migrating data from JSON config to SQLite...",
                new EKEventId(0, TextSource.None));
            MigrateFromConfig(config);
        }

        RefreshAllCaches();
    }


    /// <summary>
    /// Records the current schema version after <see cref="DbContext.Database.EnsureCreated"/>
    /// has built the canonical schema. We dropped all v1–v13 incremental migrations after the
    /// plugin's pre-release rewrite, so anyone installing now starts at v1 with the modern
    /// table layout. If a real upgrade ever ships, add migrations on top of this baseline.
    /// </summary>
    private void RecordSchemaVersion()
    {
        _context.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)");
        _context.Database.ExecuteSqlRaw("DELETE FROM schema_version");
        _context.Database.ExecuteSqlRaw(
            "INSERT INTO schema_version (version) VALUES ({0})", CurrentSchemaVersion);
    }

    /// <summary>
    /// v2 migration: ensure <c>character_speaker_aliases</c> exists for users coming from
    /// v1 (where <see cref="DbContext.Database.EnsureCreated"/> built the schema BEFORE this
    /// table was part of the model). Uses <c>CREATE TABLE IF NOT EXISTS</c> + <c>CREATE INDEX
    /// IF NOT EXISTS</c> so re-running is a no-op. Fresh installs already have the table from
    /// EnsureCreated; this just paves over v1.
    /// </summary>
    private void EnsureSpeakerAliasTable()
    {
        _context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS character_speaker_aliases (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                character_id INTEGER NOT NULL,
                language INTEGER NOT NULL,
                alias TEXT NOT NULL,
                FOREIGN KEY (character_id) REFERENCES characters(id) ON DELETE CASCADE
            )");
        _context.Database.ExecuteSqlRaw(@"
            CREATE UNIQUE INDEX IF NOT EXISTS IX_character_speaker_aliases_character_language_alias
                ON character_speaker_aliases (character_id, language, alias)");
        _context.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_character_speaker_aliases_language_alias
                ON character_speaker_aliases (language, alias)");
    }

    // ── JSON-config → SQLite migration ──────────────────────

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
                // Voices first — characters reference voice_key.
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
                            Gender = (int)g,
                        });
                    foreach (var r in voice.AllowedRaces)
                        _context.VoiceAllowedRaces.Add(new VoiceAllowedRaceEntity
                        {
                            VoiceId = entity.Id,
                            Race = (int)r,
                        });
                }
                _context.SaveChanges();

                MigrateCharacterList(config.MappedNpcs, "npc");
                MigrateCharacterList(config.MappedPlayers, "player");

                foreach (var pc in config.PhoneticCorrections)
                {
                    _context.PhoneticCorrections.Add(new PhoneticCorrectionEntity
                    {
                        OriginalText = pc.OriginalText ?? "",
                        CorrectedText = pc.CorrectedText ?? "",
                    });
                }

                // Muted dialogues are bare base IDs; flip the flag on any matching instance,
                // skip the rest (they get recreated naturally when the NPC is encountered).
                foreach (var baseId in config.MutedNpcDialogues)
                {
                    var existing = _context.CharacterInstances
                        .FirstOrDefault(ci => ci.NpcBaseId == (long)baseId);
                    if (existing != null) existing.IsMuted = true;
                }

                _context.SaveChanges();
                transaction?.Commit();

                // Clear source lists so NeedsMigration returns false on subsequent starts.
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

    /// <summary>
    /// Migrates a list of <see cref="NpcMapData"/> into <c>characters</c> + <c>character_contexts</c>.
    /// De-duplicates on the same case-insensitive (Name, Gender, Race, Language) key the v1
    /// schema's UNIQUE index uses — pre-DB JSON configs from older plugin versions sometimes
    /// contain multiple entries that collapse to the same key (case-only differences, repeated
    /// entries from older versions). Without dedup the migration crashed on UNIQUE constraint.
    /// Conflict resolution: prefer the entry with a non-empty voice key, otherwise keep the first.
    /// </summary>
    private void MigrateCharacterList(List<NpcMapData> mappings, string contextType)
    {
        // Pre-DB plugin versions didn't track per-entry language — every NpcMapData
        // deserializes from those legacy configs as ClientLanguage.English (the property
        // default). On a non-English client the VCM's language filter then hides every
        // migrated row. We override Language with the user's actual session language so
        // imported entries match what they're hearing in-game. Edge case (multi-language
        // users): they'd need to re-harvest in the missing language.
        var migratedLanguage = (int)_clientLanguage;

        var deduped = new Dictionary<(string, int, int, int), NpcMapData>();
        foreach (var npc in mappings)
        {
            var key = ((npc.Name ?? "").ToLowerInvariant(),
                       (int)npc.Gender, (int)npc.Race, migratedLanguage);
            if (deduped.TryGetValue(key, out var existing))
            {
                if (string.IsNullOrEmpty(existing.voice) && !string.IsNullOrEmpty(npc.voice))
                    deduped[key] = npc;
                continue;
            }
            deduped[key] = npc;
        }

        foreach (var npc in deduped.Values)
        {
            var character = new CharacterEntity
            {
                Name = npc.Name ?? "",
                Race = (int)npc.Race,
                RaceStr = npc.RaceStr ?? "",
                Gender = (int)npc.Gender,
                BodyType = (int)npc.BodyType,
                Language = migratedLanguage,
                VoiceKey = npc.voice ?? "",
                ObjectKind = (int)npc.ObjectKind,
                World = npc.World ?? "",
            };
            _context.Characters.Add(character);
            _context.SaveChanges();

            _context.CharacterContexts.Add(new CharacterContextEntity
            {
                CharacterId = character.Id,
                ContextType = contextType,
                IsEnabled = npc.IsEnabled,
                Volume = npc.Volume,
            });

            if (npc.HasBubbles && contextType == "npc")
            {
                _context.CharacterContexts.Add(new CharacterContextEntity
                {
                    CharacterId = character.Id,
                    ContextType = "bubble",
                    IsEnabled = npc.IsEnabledBubble,
                    Volume = npc.VolumeBubble,
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
            if (_disposed) return new List<CharacterEntity>();
            return _context.Characters.AsNoTracking().ToList();
        }
    }

    public CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race, int language)
    {
        lock (_writeLock)
        {
            if (_disposed) return null;
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
            if (_disposed) return null;
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
            if (_disposed) return new List<CharacterInstanceEntity>();
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
            // Clear the change tracker before every Upsert. BackendService.MapVoices calls
            // this in a tight loop right after AllTalk starts (one Upsert per backend voice),
            // and EF Core's identity map for the (VoiceId, Gender) and (VoiceId, Race)
            // composite-key children doesn't always survive iteration N's SaveChanges +
            // FK fixup → iteration N+1 collides with "another instance with the same key
            // value is already being tracked". Clearing here is safe because every method
            // on this service does its own lock-bracketed read-modify-write — no caller
            // depends on long-lived tracked state across calls.
            _context.ChangeTracker.Clear();

            // Dedupe child collections in-place. Voice filenames occasionally carry the same
            // race / gender token twice (legacy community zips, "Male_Roegadyn-Roegadyn_..."
            // shapes, etc.) which would push the same composite key (VoiceId, Race) onto the
            // tracker twice and crash on Add. Source-side fix lives in NpcDataService.ReSet*
            // — this is belt-and-suspenders for any other entry path (migration, hand-built).
            voice.AllowedGenders = voice.AllowedGenders
                .GroupBy(g => g.Gender).Select(g => g.First()).ToList();
            voice.AllowedRaces = voice.AllowedRaces
                .GroupBy(r => r.Race).Select(r => r.First()).ToList();

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

                // Update junction tables — Clear() on the navigation marks existing children
                // as removed (cascade-delete configured), and the new children re-attach with
                // the resolved Voice.Id. The ChangeTracker.Clear() above ensures no stale
                // (VoiceId, Gender) entries from a prior iteration's Update branch linger.
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
            RefreshSpeakerAliasCache();
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
            if (_disposed) return new List<VoiceClipEntity>();
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
            _context.CharacterSpeakerAliases.RemoveRange(_context.CharacterSpeakerAliases);
            _context.CharacterInstances.RemoveRange(_context.CharacterInstances);
            _context.CharacterContexts.RemoveRange(_context.CharacterContexts);
            _context.Characters.RemoveRange(_context.Characters);
            _context.PhoneticCorrections.RemoveRange(_context.PhoneticCorrections);
            // Voices have child tables (allowed_races / allowed_genders) — EF cascades via relationship.
            _context.Voices.RemoveRange(_context.Voices);
            _context.SaveChanges();
            _context.ChangeTracker.Clear();

            // Reclaim disk space. SQLite's DELETE only marks pages as free — the .db file stays
            // at its previous high-water mark until a VACUUM rewrites it. Without this, a user
            // who wiped a 150 MB DB sees an empty plugin but the file on disk is still 150 MB.
            // Must run OUTSIDE a transaction (EF Core's SaveChanges already committed). Holding
            // _writeLock keeps our own writers from racing — Dalamud is single-process so we
            // don't worry about external connections.
            try
            {
                _context.Database.ExecuteSqlRaw("VACUUM");
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(WipeAll),
                    $"VACUUM failed; the .db file size will not shrink until next plugin start: {ex.Message}",
                    new EKEventId(0, TextSource.None));
            }

            // Reset every cache, not just voices/phonetics/muted. NpcDataService.LoadFromDatabase
            // is subscribed to VoiceClipLogged and bails early when DB count matches the in-memory
            // count. With stale _cachedNpcs/_cachedPlayers, GetNpcs()/GetPlayers() would still
            // return the pre-wipe lists, the count comparison would pass (NNN==NNN), and the VC
            // Manager would keep showing the wiped NPCs until plugin reload.
            _cachedNpcs = new List<CharacterEntity>();
            _cachedPlayers = new List<CharacterEntity>();
            _cachedVoices = new List<VoiceEntity>();
            _cachedPhonetics = new List<PhoneticCorrectionEntity>();
            _cachedMutedBaseIds = new HashSet<uint>();
            _cachedAliasMap = new Dictionary<(int, string), List<int>>();
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
            if (_disposed) return 0;
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
        RefreshSpeakerAliasCache();
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

    private void RefreshSpeakerAliasCache()
    {
        // Single round-trip: pull (language, alias, character_id) rows, group by normalized
        // alias (lowercase + trim). One key → list of character_ids; the live resolver
        // disambiguates multi-value entries (typical for "???" / generic fakenames).
        var fresh = new Dictionary<(int, string), List<int>>();
        foreach (var row in _context.CharacterSpeakerAliases.AsNoTracking())
        {
            var key = (row.Language, NormalizeAlias(row.Alias));
            if (key.Item2.Length == 0) continue;
            if (!fresh.TryGetValue(key, out var list))
                fresh[key] = list = new List<int>();
            if (!list.Contains(row.CharacterId)) list.Add(row.CharacterId);
        }
        _cachedAliasMap = fresh;
    }

    private static string NormalizeAlias(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty : s.Trim().ToLowerInvariant();

    // ── Speaker aliases (harvest-discovered (-Fakename-) → character) ───────

    public void UpsertSpeakerAlias(int characterId, int language, string alias)
    {
        if (characterId <= 0 || string.IsNullOrWhiteSpace(alias)) return;
        var trimmed = alias.Trim();
        lock (_writeLock)
        {
            var existing = _context.CharacterSpeakerAliases
                .FirstOrDefault(a => a.CharacterId == characterId
                                  && a.Language == language
                                  && EF.Functions.Collate(a.Alias, "NOCASE") == trimmed);
            if (existing == null)
            {
                _context.CharacterSpeakerAliases.Add(new CharacterSpeakerAliasEntity
                {
                    CharacterId = characterId,
                    Language = language,
                    Alias = trimmed,
                });
                if (!BulkMode)
                {
                    _context.SaveChanges();
                    RefreshSpeakerAliasCache();
                }
            }
        }
    }

    public int? FindCharacterIdByAlias(string alias, int language)
    {
        // Convenience wrapper for the unambiguous case. Returns null for 0 or >1 matches —
        // callers that need to handle ambiguity must use FindCharacterIdsByAlias and apply
        // their own disambiguation (e.g. live-runtime physical-presence check).
        var ids = FindCharacterIdsByAlias(alias, language);
        return ids.Count == 1 ? ids[0] : (int?)null;
    }

    public List<int> FindCharacterIdsByAlias(string alias, int language)
    {
        if (string.IsNullOrWhiteSpace(alias)) return new List<int>();
        var key = (language, NormalizeAlias(alias));
        return _cachedAliasMap.TryGetValue(key, out var list)
            ? new List<int>(list)  // defensive copy — caller might mutate, cache is shared
            : new List<int>();
    }

    public List<CharacterSpeakerAliasEntity> GetSpeakerAliases(int characterId)
    {
        lock (_writeLock)
        {
            if (_disposed) return new List<CharacterSpeakerAliasEntity>();
            return _context.CharacterSpeakerAliases
                .Where(a => a.CharacterId == characterId)
                .AsNoTracking()
                .ToList();
        }
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
        // Serialize against in-flight reads so a method already inside _writeLock finishes
        // before we tear down _context. Then set _disposed inside the same critical section so
        // any read that takes the lock AFTER us sees the flag and bails instead of throwing.
        lock (_writeLock)
        {
            if (_disposed) return;
            _disposed = true;
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
}
