using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface INpcDataService
{
    List<NpcMapData> MappedNpcs { get; }
    List<NpcMapData> MappedPlayers { get; }
    bool IsGenderedRace(NpcRaces race);
    void ReSetVoiceRaces(EchokrautVoice voice, EKEventId? eventId = null);
    void ReSetVoiceGenders(EchokrautVoice voice, EKEventId? eventId = null);
    void MigrateOldData(EchokrautVoice? oldVoice = null, EchokrautVoice? newEkVoice = null);
    void RefreshSelectables(List<EchokrautVoice> voices);
    NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId eventId, IBackendService backend);
    void SaveCharacter(NpcMapData data);
    void RemoveCharacter(NpcMapData data);
    List<EchokrautVoice> GetEchokrautVoices();
    void SaveVoice(EchokrautVoice voice);
    List<PhoneticCorrection> GetPhoneticCorrections();
    void UpsertPhoneticCorrection(string originalText, string correctedText);
    void DeletePhoneticCorrection(string originalText);
    bool IsDialogueMuted(uint baseId);
    void MuteDialogue(uint baseId);
    void UnmuteDialogue(uint baseId);
}
