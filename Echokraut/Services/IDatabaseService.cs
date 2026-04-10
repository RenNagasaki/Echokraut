using System;
using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;

namespace Echokraut.Services;

public interface IDatabaseService : IDisposable
{
    // Migration
    bool NeedsMigration(Configuration config);
    void MigrateFromConfig(Configuration config);

    // Characters
    List<CharacterEntity> GetNpcs();
    List<CharacterEntity> GetPlayers();
    List<CharacterEntity> GetAllCharacters();
    CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race, int language = 1);
    CharacterEntity UpsertCharacter(CharacterEntity character);
    void DeleteCharacter(int characterId);

    // Character contexts
    CharacterContextEntity? GetContext(int characterId, string contextType);
    CharacterContextEntity UpsertContext(int characterId, string contextType, bool isEnabled = true, float volume = 1.0f);

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
    void LogVoiceClip(VoiceClipEntity voiceClip);
    void LogOrUpdateVoiceClip(VoiceClipEntity voiceClip);
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
    void DeleteVoiceClip(int voiceClipId);
    void ClearVoiceClips();
    void WipeAll();
    void FlushChanges();

    // Per-player generation tracking
    void LogVoiceClipGeneration(int voiceClipId, long playerContentId, string playerName, string savePath);
    void DeleteVoiceClipGeneration(int voiceClipId, long playerContentId);
    VoiceClipGenerationEntity? GetVoiceClipGeneration(int voiceClipId, long playerContentId);
    int GetGeneratedCountForCharacter(int characterId, long playerContentId, int? questTypeFilter = null);
    /// <summary>
    /// Aggregate count of voice clips and player-generated voice clips across all characters
    /// of a given language and context (npc/player), optionally filtered by quest type.
    /// Single round-trip; intended for status-bar style summaries.
    /// </summary>
    (int totalClips, int generatedClips) GetClipTotalsForLanguage(
        int language, string contextType, long playerContentId, int? questTypeFilter = null);
}
