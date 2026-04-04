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
    CharacterEntity? FindCharacter(string name, Genders gender, NpcRaces race);
    CharacterEntity UpsertCharacter(CharacterEntity character);
    void DeleteCharacter(int characterId);

    // Character contexts
    CharacterContextEntity? GetContext(int characterId, string contextType);
    CharacterContextEntity UpsertContext(int characterId, string contextType, bool isEnabled = true, float volume = 1.0f);

    // Character instances
    CharacterInstanceEntity GetOrCreateInstance(int characterId, uint npcBaseId);
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
    void LogEncounter(DialogEncounterEntity encounter);
    List<DialogEncounterEntity> GetEncounters(int limit = 1000, int offset = 0);
    int GetEncounterCount();
    void ClearEncounters();
}
