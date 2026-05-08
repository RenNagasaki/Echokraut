using System;
using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;

namespace Echokraut.Services;

public interface IDatabaseService : IDisposable
{
    // Migration (one-shot JSON-config → SQLite import, idempotent after first run)
    bool NeedsMigration(Configuration config);
    void MigrateFromConfig(Configuration config);
    /// <summary>
    /// Walks <see cref="Configuration.LocalSaveLocation"/> and creates voice_clip_generations
    /// rows for legacy on-disk audio files that have no matching DB record. One-shot
    /// follow-up to <see cref="MigrateFromConfig"/>; the caller (Plugin.cs) gates the
    /// invocation on <see cref="Configuration.AudioFilesBackfillPending"/> and the player
    /// being logged in. Idempotent — safe to call repeatedly.
    /// </summary>
    void BackfillAudioFiles(Configuration config, IGameObjectService gameObjects, IAudioFileService audioFiles);

    // Characters
    List<CharacterEntity> GetNpcs();
    List<CharacterEntity> GetPlayers();
    List<CharacterEntity> GetAllCharacters();
    CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race, int language);
    CharacterEntity UpsertCharacter(CharacterEntity character);
    void DeleteCharacter(int characterId);

    // Character contexts
    CharacterContextEntity? GetContext(int characterId, string contextType);

    /// <summary>
    /// Ensures a context row exists for (characterId, contextType). Creates with defaults
    /// (IsEnabled=true, Volume=1.0) if missing; returns the existing row UNCHANGED if it
    /// already exists. Use this from data-import paths (harvest) where IsEnabled/Volume are
    /// user preferences, not data — re-importing must not stomp settings the user changed.
    /// </summary>
    CharacterContextEntity EnsureContext(int characterId, string contextType);

    /// <summary>
    /// Creates or fully overwrites the (IsEnabled, Volume) of a context row. Use this from
    /// UI save paths (NpcDataService) where the caller has explicit values to persist.
    /// </summary>
    CharacterContextEntity UpsertContext(int characterId, string contextType, bool isEnabled, float volume);

    // Character instances
    CharacterInstanceEntity GetOrCreateInstance(int characterId, uint npcBaseId,
        string zoneName = "", float mapX = 0, float mapY = 0);
    List<CharacterInstanceEntity> GetInstancesForCharacter(int characterId);
    void MuteInstance(uint npcBaseId);
    void UnmuteInstance(uint npcBaseId);
    void ClearInstanceMutes();
    HashSet<uint> GetMutedBaseIds();

    // Voices
    List<VoiceEntity> GetVoices();
    VoiceEntity? GetVoiceByKey(string backendVoice);
    VoiceEntity UpsertVoice(VoiceEntity voice);
    void DeleteVoice(string backendVoice);

    // Phonetic corrections
    List<PhoneticCorrectionEntity> GetPhoneticCorrections();
    void UpsertPhoneticCorrection(string originalText, string correctedText);
    void DeletePhoneticCorrection(string originalText);

    // Dialog encounters
    bool SuppressEvents { get; set; }
    /// <summary>
    /// When true, Upsert*/Delete* methods skip the cache-refresh step. Used by bulk
    /// operations (harvest) to avoid the O(N²) reload cost. Caller MUST invoke
    /// <see cref="RefreshCaches"/> after the bulk run finishes.
    /// </summary>
    bool BulkMode { get; set; }
    /// <summary>Re-loads all in-memory caches from the database. Call once after a bulk run.</summary>
    void RefreshCaches();
    void NotifyVoiceClipLogged();
    void ClearChangeTracker();
    event Action? VoiceClipLogged;

    /// <summary>
    /// Fires after <see cref="WipeAll"/> has cleared all rows. Subscribers can repopulate
    /// data they're responsible for (e.g. <c>BackendService</c> re-discovers voices from
    /// the running TTS backend).
    /// </summary>
    event Action? DatabaseWiped;
    void LogVoiceClip(VoiceClipEntity voiceClip);
    /// <summary>
    /// Upserts a voice clip and returns the persisted entity. In live mode the returned
    /// entity has its <see cref="VoiceClipEntity.Id"/> populated; in batch mode (SuppressEvents)
    /// the Id may still be 0 until the caller flushes.
    /// </summary>
    VoiceClipEntity LogOrUpdateVoiceClip(VoiceClipEntity voiceClip);
    List<VoiceClipEntity> GetVoiceClips(int limit = 1000, int offset = 0,
        string? npcNameFilter = null, string? textFilter = null,
        int? textSourceFilter = null, bool? savedFilter = null);
    int GetVoiceClipCount(string? npcNameFilter = null, string? textFilter = null,
        int? textSourceFilter = null, bool? savedFilter = null);
    List<CharacterEntity> GetCharactersWithVoiceClips();
    List<VoiceClipEntity> GetVoiceClipsForCharacter(int characterId, int limit = 1000, int offset = 0);
    int GetVoiceClipCountForCharacter(int characterId, int? questTypeFilter = null);
    HashSet<int> GetCharacterIdsWithQuestType(int questType);
    int GetSavedVoiceClipCountForCharacter(int characterId);
    void UpdateVoiceClipSaved(int voiceClipId, bool savedToDisk, string savePath);
    void UpdateVoiceClipVoiceKey(int voiceClipId, string voiceKey);
    void DeleteVoiceClip(int voiceClipId);
    void ClearVoiceClips();
    void WipeAll();
    void FlushChanges();

    // Per-player generation tracking. aliasGender: 0 = real player generation,
    // 1 = shareable male alias variant, 2 = shareable female alias variant.
    // Alias variants always pass playerContentId = 0 — they're not bound to a specific player.
    void LogVoiceClipGeneration(int voiceClipId, long playerContentId, string playerName, string savePath, string voiceKey, int aliasGender = 0);
    void DeleteVoiceClipGeneration(int voiceClipId, long playerContentId, int aliasGender = 0);
    VoiceClipGenerationEntity? GetVoiceClipGeneration(int voiceClipId, long playerContentId, int aliasGender = 0);
    int GetGeneratedCountForCharacter(int characterId, long playerContentId, int? questTypeFilter = null);
    /// <summary>
    /// Aggregate count of voice clips and player-generated voice clips across all characters
    /// of a given language and context (npc/player), optionally filtered by quest type.
    /// Single round-trip; intended for status-bar style summaries.
    /// </summary>
    (int totalClips, int generatedClips) GetClipTotalsForLanguage(
        int language, string contextType, long playerContentId, int? questTypeFilter = null);

    // Lodestone lookup cache
    LodestoneLookupEntity? GetLodestoneLookup(string name, string world);
    void UpsertLodestoneLookup(string name, string world, NpcRaces race, Genders gender, bool found);

    // Speaker aliases — harvest-discovered (-Fakename-) → character mappings, queried at
    // runtime when the dialog-box speaker name doesn't match a character row directly.
    // Complements VoiceNames{LANG}.json which holds community-curated voice families.
    void UpsertSpeakerAlias(int characterId, int language, string alias);
    /// <summary>
    /// Resolve a dialog-box speaker name (case-insensitive) to a character ID via the
    /// alias table. Returns <c>null</c> when no match OR when multiple character rows share
    /// the same alias (typical for the anonymous <c>???</c> marker) — ambiguity-aware
    /// callers should use <see cref="FindCharacterIdsByAlias"/> instead and apply their own
    /// disambiguation (e.g. physical-presence check via spawned NPCs in the object table).
    /// Cached in memory after first build.
    /// </summary>
    int? FindCharacterIdByAlias(string alias, int language);
    /// <summary>
    /// Returns every character ID that registers the given fakename in the given language.
    /// Multi-valued by design: anonymous markers like "???" map to many characters, and the
    /// caller is expected to disambiguate at runtime. Returns an empty list when no match.
    /// </summary>
    List<int> FindCharacterIdsByAlias(string alias, int language);
    List<CharacterSpeakerAliasEntity> GetSpeakerAliases(int characterId);
}
