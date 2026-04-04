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

        InitializeDatabase(config);
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

    private void InitializeDatabase(Configuration config)
    {
        // Enable WAL mode for better concurrent read performance
        _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL");
        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON");
        _context.Database.EnsureCreated();

        if (NeedsMigration(config))
        {
            _log.Info(nameof(InitializeDatabase), "Migrating data from JSON config to SQLite...",
                new EKEventId(0, TextSource.None));
            MigrateFromConfig(config);
        }

        RefreshAllCaches();
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
                        IsChildVoice = voice.IsChildVoice,
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
                BodyType = npc.IsChild ? (int)Enums.BodyType.Child : (int)Enums.BodyType.Adult,
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

    public CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race)
    {
        return _context.Characters
            .Include(c => c.Contexts)
            .FirstOrDefault(c => c.Name == name && c.Gender == (int)gender && c.Race == (int)race);
    }

    public CharacterEntity UpsertCharacter(CharacterEntity character)
    {
        lock (_writeLock)
        {
            var existing = _context.Characters
                .FirstOrDefault(c => c.Name == character.Name
                                     && c.Gender == character.Gender
                                     && c.Race == character.Race);

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

    public CharacterInstanceEntity GetOrCreateInstance(int characterId, uint npcBaseId)
    {
        lock (_writeLock)
        {
            var existing = _context.CharacterInstances
                .FirstOrDefault(ci => ci.CharacterId == characterId && ci.NpcBaseId == (long)npcBaseId);

            if (existing != null)
                return existing;

            existing = new CharacterInstanceEntity
            {
                CharacterId = characterId,
                NpcBaseId = (long)npcBaseId,
                FirstSeen = DateTime.UtcNow
            };
            _context.CharacterInstances.Add(existing);
            _context.SaveChanges();
            return existing;
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
                existing.IsChildVoice = voice.IsChildVoice;
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

    public void LogEncounter(DialogEncounterEntity encounter)
    {
        lock (_writeLock)
        {
            _context.DialogEncounters.Add(encounter);
            _context.SaveChanges();
        }
    }

    public List<DialogEncounterEntity> GetEncounters(int limit = 1000, int offset = 0)
    {
        return _context.DialogEncounters
            .Include(e => e.Character)
            .OrderByDescending(e => e.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public int GetEncounterCount() => _context.DialogEncounters.Count();

    public void ClearEncounters()
    {
        lock (_writeLock)
        {
            _context.DialogEncounters.RemoveRange(_context.DialogEncounters);
            _context.SaveChanges();
        }
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
        _context.Dispose();
    }
}
