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
    /// Constructor for testing with a pre-configured DbContext. Runs the same schema-migration
    /// chain as the production constructor so tests against an in-memory SQLite exercise the
    /// real upgrade paths (and any v_n → v_{n+1} migration test can roll specific tables/columns
    /// back manually before re-running). Defaults to <see cref="ClientLanguage.English"/> for
    /// the migration's per-row language stamp; tests that exercise non-English JSON-config
    /// migration pass the matching language explicitly.
    /// </summary>
    public DatabaseService(ILogService log, EchokrautDbContext context,
        ClientLanguage clientLanguage = ClientLanguage.English)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clientLanguage = clientLanguage;

        _context.Database.EnsureCreated();
        RunSchemaMigrations();
        RefreshAllCaches();
    }

    internal const int CurrentSchemaVersion = 4;

    private void InitializeDatabase(Configuration config)
    {
        // Enable WAL mode for better concurrent read performance
        _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON");
        _context.Database.EnsureCreated();

        RunSchemaMigrations();

        // JSON-config → SQLite legacy import is NOT triggered here anymore — it runs from
        // Plugin.cs once the player is logged in (the audio-backfill step that follows the
        // JSON import needs LocalPlayerName / LocalPlayerContentId for placeholder
        // detection, neither of which is available during plugin bootstrap). InitializeDatabase
        // keeps schema work only; data migration is the caller's problem.

        RefreshAllCaches();
    }


    /// <summary>
    /// Runs every schema migration that bridges a previous on-disk version to
    /// <see cref="CurrentSchemaVersion"/>. Each migration is idempotent (CREATE … IF NOT
    /// EXISTS, or PRAGMA-guarded ALTER), so the chain runs the same on a brand-new DB,
    /// a v1 install upgrading, a v2 install upgrading, etc. Add a new
    /// <c>EnsureXxx()</c> method here whenever <see cref="CurrentSchemaVersion"/> bumps.
    /// </summary>
    private void RunSchemaMigrations()
    {
        EnsureSpeakerAliasTable();              // v1 → v2
        EnsureVoiceClipGenerationVoiceKey();    // v2 → v3
        EnsureVoiceClipWavFileName();           // v3 → v4
        RecordSchemaVersion();
    }

    /// <summary>
    /// Records the current schema version after <see cref="DbContext.Database.EnsureCreated"/>
    /// has built the canonical schema. We dropped all v1–v13 incremental migrations after the
    /// plugin's pre-release rewrite, so anyone installing now starts at v1 with the modern
    /// table layout, then walks forward through the post-rewrite migrations.
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
    /// Internal so migration tests can exercise this single step in isolation without
    /// running the full RunSchemaMigrations chain.
    /// </summary>
    internal void EnsureSpeakerAliasTable()
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

    /// <summary>
    /// v3 migration: add <c>voice_key TEXT NOT NULL DEFAULT ''</c> to
    /// <c>voice_clip_generations</c>. SQLite has no <c>ADD COLUMN IF NOT EXISTS</c>, so we
    /// probe the schema via <c>PRAGMA table_info</c> and skip the ALTER when the column is
    /// already present. Fresh installs already have the column via EnsureCreated; this only
    /// fires for v2 → v3 upgrades.
    /// Internal so migration tests can exercise this single step in isolation.
    /// </summary>
    internal void EnsureVoiceClipGenerationVoiceKey()
    {
        if (TableHasColumn("voice_clip_generations", "voice_key")) return;
        _context.Database.ExecuteSqlRaw(
            "ALTER TABLE voice_clip_generations ADD COLUMN voice_key TEXT NOT NULL DEFAULT ''");
    }

    /// <summary>
    /// v4 migration: add <c>wav_file_name TEXT NOT NULL DEFAULT ''</c> to <c>voice_clips</c>
    /// plus an index on <c>(character_id, wav_file_name)</c>. The column lets the legacy
    /// audio-file backfill (run from <c>MigrateFromConfig</c> once) record orphan files
    /// without inventing fake text/cleaned-text content; the index supports the live
    /// runtime's fallback lookup that promotes those orphans to full rows when the same
    /// dialog comes through chat. Same PRAGMA-probe pattern as v3 — SQLite has no
    /// <c>ADD COLUMN IF NOT EXISTS</c>.
    /// Internal so migration tests can exercise this single step in isolation.
    /// </summary>
    internal void EnsureVoiceClipWavFileName()
    {
        if (!TableHasColumn("voice_clips", "wav_file_name"))
            _context.Database.ExecuteSqlRaw(
                "ALTER TABLE voice_clips ADD COLUMN wav_file_name TEXT NOT NULL DEFAULT ''");
        // Index creation is independently idempotent via IF NOT EXISTS — runs even if the
        // column already existed but the index didn't (mid-migration interruption).
        _context.Database.ExecuteSqlRaw(@"
            CREATE INDEX IF NOT EXISTS IX_voice_clips_character_wav_file_name
                ON voice_clips (character_id, wav_file_name)");
    }

    /// <summary>
    /// Returns true when the given table has a column with the given name. Read via
    /// <c>PRAGMA table_info</c>; case-insensitive on the column name (SQLite default).
    /// </summary>
    private bool TableHasColumn(string table, string column)
    {
        using var cmd = _context.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) cmd.Connection.Open();
        using var reader = cmd.ExecuteReader();
        // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
                // Arm the audio backfill — Plugin.cs runs it on the next framework tick
                // where the player is logged in (placeholder detection needs LocalPlayerName).
                config.AudioFilesBackfillPending = true;
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

    // ── Legacy audio-file backfill ──────────────────────────────────────

    /// <summary>Tokens <c>AudioFileService.RemovePlayerNameInText</c> substitutes for the
    /// player's name. After the substituted text passes through
    /// <c>VoiceMessageToFileName</c> (lowercase + strip non-alphanumeric) the angle brackets
    /// are gone and these become bare lowercase identifiers in the on-disk filename, so
    /// "playername" / "playerfirstname" / "playerlastname" appearing in a normalised filename
    /// is a near-certain signal that this clip carried a player-name placeholder.</summary>
    private static readonly string[] PlaceholderNeedles =
        { "playername", "playerfirstname", "playerlastname" };

    /// <summary>
    /// Walks <see cref="Configuration.LocalSaveLocation"/> and creates
    /// <see cref="VoiceClipEntity"/> + <see cref="VoiceClipGenerationEntity"/> rows for every
    /// on-disk audio file whose folder name matches a known character in the DB. Idempotent:
    /// re-running over the same disk state hits the existing rows via the
    /// <c>(character_id, wav_file_name)</c> lookup and updates their save_path/voice_key in
    /// place rather than duplicating. Clears <see cref="Configuration.AudioFilesBackfillPending"/>
    /// when the walk finishes successfully.
    ///
    /// Heuristics (per the user's spec):
    /// - Folder → character match by <c>(name, language)</c>. Multi-match (e.g. multiple
    ///   "???" rows) is skipped — those legacy entries aren't clean enough to match
    ///   reliably from filename alone, and inventing a wrong attribution is worse than
    ///   leaving the file an orphan.
    /// - <c>voice_key</c> for the new generation row defaults to the character's current
    ///   <see cref="CharacterEntity.VoiceKey"/> (the snapshot the JSON-config migration
    ///   wrote). Empty when no voice was assigned to that character.
    /// - Player-placeholder detection: the normalised filename is checked for the
    ///   substitution tokens above (modern clips) AND for the current player's first/last
    ///   name (legacy clips that pre-date the placeholder system, where the literal name
    ///   was baked into the filename). Either match → <c>player_content_id</c> = the
    ///   local player's content id, otherwise 0. False positives heuristically biased
    ///   toward 0 — the user accepts the rare misclassification because clean-up is
    ///   easier later than re-attributing rows after the fact.
    /// </summary>
    public void BackfillAudioFiles(Configuration config, IGameObjectService gameObjects, IAudioFileService audioFiles)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (gameObjects == null) throw new ArgumentNullException(nameof(gameObjects));
        if (audioFiles == null) throw new ArgumentNullException(nameof(audioFiles));

        var eventId = new EKEventId(0, TextSource.None);

        if (string.IsNullOrWhiteSpace(config.LocalSaveLocation) ||
            !Directory.Exists(config.LocalSaveLocation))
        {
            _log.Info(nameof(BackfillAudioFiles),
                $"LocalSaveLocation '{config.LocalSaveLocation}' missing — nothing to back-fill",
                eventId);
            config.AudioFilesBackfillPending = false;
            config.Save();
            return;
        }

        var playerName = gameObjects.LocalPlayerName ?? "";
        var playerContentId = (long)gameObjects.LocalPlayerContentId;

        // Pre-compute normalised player name parts once. Empty parts skip the contains check
        // so a player without first/last split doesn't accidentally match every empty file.
        var nameParts = playerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nameNeedleFull = NormalizeForFilename(playerName);
        var nameNeedleFirst = nameParts.Length > 0 ? NormalizeForFilename(nameParts[0]) : "";
        var nameNeedleLast = nameParts.Length > 1 ? NormalizeForFilename(nameParts[nameParts.Length - 1]) : "";

        int created = 0, updated = 0, skippedAmbiguous = 0, skippedNoCharacter = 0;

        lock (_writeLock)
        {
            try
            {
                foreach (var speakerDir in Directory.EnumerateDirectories(config.LocalSaveLocation))
                {
                    var folderName = Path.GetFileName(speakerDir);
                    // The audio writer uses "NOPERSON" when the speaker name was empty (Amon
                    // entries, etc.). Normalise back so we can look the empty-name character up.
                    var lookupName = folderName == "NOPERSON" ? "" : folderName;

                    var matches = _context.Characters
                        .Where(c => c.Name == lookupName && c.Language == (int)_clientLanguage)
                        .ToList();
                    if (matches.Count == 0)
                    {
                        skippedNoCharacter++;
                        continue;
                    }
                    if (matches.Count > 1)
                    {
                        skippedAmbiguous++;
                        _log.Debug(nameof(BackfillAudioFiles),
                            $"Multi-match for folder '{folderName}' (lang={_clientLanguage}, candidates={matches.Count}) — skipping",
                            eventId);
                        continue;
                    }

                    var character = matches[0];
                    foreach (var wavPath in Directory.EnumerateFiles(speakerDir, "*.wav", SearchOption.TopDirectoryOnly))
                    {
                        var wavFileName = Path.GetFileNameWithoutExtension(wavPath);

                        var existing = _context.VoiceClips.FirstOrDefault(vc =>
                            vc.CharacterId == character.Id && vc.WavFileName == wavFileName);
                        if (existing == null)
                        {
                            existing = new VoiceClipEntity
                            {
                                CharacterId = character.Id,
                                WavFileName = wavFileName,
                                VoiceKey = character.VoiceKey,
                                SavedToDisk = true,
                                SavePath = wavPath,
                                Language = character.Language,
                            };
                            _context.VoiceClips.Add(existing);
                            _context.SaveChanges();
                            created++;
                        }
                        else
                        {
                            existing.SavedToDisk = true;
                            if (string.IsNullOrEmpty(existing.SavePath)) existing.SavePath = wavPath;
                            updated++;
                        }

                        var hasPlaceholder = HasPlayerPlaceholder(wavFileName,
                            nameNeedleFull, nameNeedleFirst, nameNeedleLast);
                        var pcid = hasPlaceholder ? playerContentId : 0L;

                        // LogVoiceClipGeneration is upsert-keyed on
                        // (voice_clip_id, player_content_id, alias_gender) so re-running over
                        // existing rows refreshes timestamp + save_path without duplicating.
                        LogVoiceClipGeneration(
                            existing.Id, pcid, playerName, wavPath,
                            voiceKey: character.VoiceKey,
                            aliasGender: 0);
                    }
                }

                _context.SaveChanges();
                config.AudioFilesBackfillPending = false;
                config.Save();

                _log.Info(nameof(BackfillAudioFiles),
                    $"Audio backfill done. created={created} updated={updated} " +
                    $"skipped_no_character={skippedNoCharacter} skipped_ambiguous={skippedAmbiguous}",
                    eventId);

                RefreshAllCaches();
            }
            catch (Exception ex)
            {
                // Don't clear the pending flag on failure — the user gets another shot on
                // next plugin start. Errors are most likely transient (file lock, permission)
                // or schema-related (the backfill predates a future migration).
                _log.Error(nameof(BackfillAudioFiles), $"Backfill failed: {ex}", eventId);
                throw;
            }
        }
    }

    /// <summary>
    /// Mirrors the normalisation <c>AudioFileService.VoiceMessageToFileName</c> applies when
    /// it builds the on-disk filename: lowercase, strip non-alphanumeric. Identical
    /// transformation on both sides means a name baked into a filename or a placeholder
    /// token can be located via plain substring search.
    /// </summary>
    private static string NormalizeForFilename(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static bool HasPlayerPlaceholder(string wavFileName,
        string nameNeedleFull, string nameNeedleFirst, string nameNeedleLast)
    {
        // wavFileName is already lowercase + alphanumeric-only because that's the format
        // VoiceMessageToFileName produced when it was written.
        foreach (var token in PlaceholderNeedles)
            if (wavFileName.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        if (nameNeedleFull.Length > 2 && wavFileName.Contains(nameNeedleFull, StringComparison.OrdinalIgnoreCase)) return true;
        if (nameNeedleFirst.Length > 2 && wavFileName.Contains(nameNeedleFirst, StringComparison.OrdinalIgnoreCase)) return true;
        if (nameNeedleLast.Length > 2 && wavFileName.Contains(nameNeedleLast, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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

    public CharacterContextEntity EnsureContext(int characterId, string contextType)
    {
        lock (_writeLock)
        {
            var existing = _context.CharacterContexts
                .FirstOrDefault(cc => cc.CharacterId == characterId && cc.ContextType == contextType);

            // Existing rows are returned untouched on purpose: IsEnabled/Volume are user
            // preferences, not data — re-running the harvest must not reset them.
            if (existing != null) return existing;

            existing = new CharacterContextEntity
            {
                CharacterId = characterId,
                ContextType = contextType,
                IsEnabled = true,
                Volume = 1.0f,
            };
            _context.CharacterContexts.Add(existing);
            _context.SaveChanges();
            if (!BulkMode) RefreshCharacterCaches();
            return existing;
        }
    }

    public CharacterContextEntity UpsertContext(int characterId, string contextType,
        bool isEnabled, float volume)
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

            // Orphan-resolve fallback: clips created by the legacy audio backfill have a
            // wav_file_name but empty text. The text-based lookup above misses them, so
            // a live encounter would otherwise duplicate the row. If the caller supplied a
            // wav_file_name, try matching on (character_id, wav_file_name) — that's the
            // hash the backfill recorded. On a hit, fill in the now-known text + base id
            // so subsequent text-based lookups find the same row directly.
            if (existing == null && !string.IsNullOrEmpty(voiceClip.WavFileName))
            {
                existing = _context.VoiceClips
                    .FirstOrDefault(vc => vc.CharacterId == voiceClip.CharacterId
                        && vc.WavFileName == voiceClip.WavFileName);
                if (existing != null)
                {
                    existing.NpcBaseId = voiceClip.NpcBaseId;
                    existing.OriginalText = voiceClip.OriginalText;
                    existing.TextSource = voiceClip.TextSource;
                    existing.Language = voiceClip.Language;
                    // Remaining fields (CleanedText, BodyType, voice key, …) get filled by
                    // the existing-update branch below — same code path as a live re-encounter.
                }
            }

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
                // (the orphan path relies on this — backfill-set SavePath survives the live
                // promote because the live caller passes empty SavePath).
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

    public void LogVoiceClipGeneration(int voiceClipId, long playerContentId, string playerName, string savePath, string voiceKey, int aliasGender = 0)
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
                existing.VoiceKey = voiceKey ?? "";
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
                    VoiceKey = voiceKey ?? "",
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

    public int? FindCharacterIdByNpcBaseId(uint npcBaseId, int language)
    {
        if (npcBaseId == 0) return null;
        lock (_writeLock)
        {
            if (_disposed) return null;
            var baseId = (long)npcBaseId;
            // Join character_instances → characters and filter by language. Order by
            // last_seen desc so a corrupted DB with duplicate instances under multiple
            // characters returns the row that's been seen most recently — the attribution
            // repair tool is what cleans the duplicates up properly.
            var hit = _context.CharacterInstances
                .AsNoTracking()
                .Where(ci => ci.NpcBaseId == baseId && ci.Character != null && ci.Character.Language == language)
                .OrderByDescending(ci => ci.LastSeen)
                .Select(ci => (int?)ci.CharacterId)
                .FirstOrDefault();
            return hit;
        }
    }

    public List<AttributionInstanceRow> GetAllInstancesForRepair()
    {
        lock (_writeLock)
        {
            if (_disposed) return new List<AttributionInstanceRow>();
            // Single round-trip via Join so we don't load every CharacterEntity nav into
            // the change tracker. Projection is flat — the consumer doesn't need EF state.
            var rows = (from ci in _context.CharacterInstances.AsNoTracking()
                        join c in _context.Characters.AsNoTracking() on ci.CharacterId equals c.Id
                        select new
                        {
                            ci.CharacterId,
                            CharacterName = c.Name,
                            CharacterGender = c.Gender,
                            CharacterRace = c.Race,
                            c.Language,
                            ci.NpcBaseId
                        })
                .ToList();
            return rows
                .Select(r => new AttributionInstanceRow(
                    r.CharacterId,
                    r.CharacterName ?? string.Empty,
                    (Genders)r.CharacterGender,
                    (NpcRaces)r.CharacterRace,
                    r.Language,
                    r.NpcBaseId))
                .ToList();
        }
    }

    public (int moved, int mergedAndDeleted) ReassignAttribution(int oldCharacterId, int newCharacterId, uint npcBaseId)
    {
        if (oldCharacterId == newCharacterId || oldCharacterId <= 0 || newCharacterId <= 0 || npcBaseId == 0)
            return (0, 0);

        lock (_writeLock)
        {
            if (_disposed) return (0, 0);
            var baseId = (long)npcBaseId;
            using var tx = _context.Database.BeginTransaction();

            MoveInstanceRow(oldCharacterId, newCharacterId, baseId);
            var (moved, merged) = MoveOrMergeVoiceClips(oldCharacterId, newCharacterId, baseId);

            _context.SaveChanges();
            tx.Commit();

            // Caches: character_instances feeds GetMutedBaseIds via the muted-cache, refresh
            // is the cheapest correct option.
            RefreshMutedCache();
            return (moved, merged);
        }
    }

    /// <summary>
    /// Step 1 of <see cref="ReassignAttribution"/>: move (or merge) the single
    /// <c>character_instance</c> row for (oldCharacterId, baseId) onto newCharacterId.
    /// </summary>
    private void MoveInstanceRow(int oldCharacterId, int newCharacterId, long baseId)
    {
        // PK is composite (CharacterId, NpcBaseId), so we delete + reinsert under the new
        // CharacterId. If the canonical character already has an instance for this baseId
        // (defensive — shouldn't happen for a well-formed dry-run), the old one is dropped.
        var oldInstance = _context.CharacterInstances
            .FirstOrDefault(ci => ci.CharacterId == oldCharacterId && ci.NpcBaseId == baseId);
        if (oldInstance == null) return;

        var canonicalInstance = _context.CharacterInstances
            .FirstOrDefault(ci => ci.CharacterId == newCharacterId && ci.NpcBaseId == baseId);

        if (canonicalInstance == null)
        {
            var moved = new CharacterInstanceEntity
            {
                CharacterId = newCharacterId,
                NpcBaseId = oldInstance.NpcBaseId,
                FirstSeen = oldInstance.FirstSeen,
                LastSeen = oldInstance.LastSeen,
                IsMuted = oldInstance.IsMuted,
                ZoneName = oldInstance.ZoneName,
                MapX = oldInstance.MapX,
                MapY = oldInstance.MapY,
            };
            _context.CharacterInstances.Remove(oldInstance);
            _context.SaveChanges();
            _context.CharacterInstances.Add(moved);
        }
        else
        {
            // Canonical already had an instance; keep canonical's row and drop the orphan.
            // Propagate the mute flag — user intent shouldn't be silently lost.
            if (oldInstance.IsMuted) canonicalInstance.IsMuted = true;
            _context.CharacterInstances.Remove(oldInstance);
        }
        _context.SaveChanges();
    }

    /// <summary>
    /// Step 2 of <see cref="ReassignAttribution"/>: move voice_clips for (oldCharacterId,
    /// baseId) to newCharacterId. On unique-index collision (canonical row already has the
    /// same OriginalText for the same baseId), the canonical row wins and the orphan +
    /// its generations are deleted via cascade.
    /// </summary>
    private (int moved, int mergedAndDeleted) MoveOrMergeVoiceClips(int oldCharacterId, int newCharacterId, long baseId)
    {
        var orphanClips = _context.VoiceClips
            .Where(vc => vc.CharacterId == oldCharacterId && vc.NpcBaseId == baseId)
            .ToList();

        int moved = 0;
        int mergedAndDeleted = 0;
        foreach (var orphan in orphanClips)
        {
            var existing = _context.VoiceClips.FirstOrDefault(vc =>
                vc.CharacterId == newCharacterId
                && vc.NpcBaseId == baseId
                && vc.OriginalText == orphan.OriginalText);
            if (existing == null)
            {
                orphan.CharacterId = newCharacterId;
                moved++;
            }
            else
            {
                _context.VoiceClips.Remove(orphan);
                mergedAndDeleted++;
            }
        }
        return (moved, mergedAndDeleted);
    }

    public bool DeleteCharacterIfEmpty(int characterId)
    {
        if (characterId <= 0) return false;
        lock (_writeLock)
        {
            if (_disposed) return false;

            var hasInstances = _context.CharacterInstances.Any(ci => ci.CharacterId == characterId);
            if (hasInstances) return false;
            var hasClips = _context.VoiceClips.Any(vc => vc.CharacterId == characterId);
            if (hasClips) return false;

            var character = _context.Characters.FirstOrDefault(c => c.Id == characterId);
            if (character == null) return false;

            // Cascade rules drop dependent rows (contexts, speaker_aliases, etc.); explicit
            // Remove on the parent is enough.
            _context.Characters.Remove(character);
            _context.SaveChanges();

            // The npc / player list caches reference CharacterEntity by Id, so a deleted
            // character would leak into the VCM until the next plugin start without this.
            RefreshCharacterCaches();
            return true;
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

            // Dispose the context (it owns + closes its connection). Wrapped separately so a
            // failure here never skips ClearAllPools below.
            try { _context.Dispose(); }
            catch { /* in-memory / already disposed */ }

            // THIS is what actually releases the on-disk SQLite file handle: Microsoft.Data.Sqlite
            // pools connections, so disposing the context only returns the physical connection to
            // the pool — the .db (+ WAL -wal/-shm) stays locked until the pool is cleared. Must run
            // even if the context dispose threw, otherwise the file stays locked until the game
            // process exits (can't delete/reset the DB on a plugin reload).
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); }
            catch { /* nothing more we can do */ }
        }
    }
}
