using Echokraut.DataClasses;
using Echokraut.Enums;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface INpcDataService
{
    bool IsGenderedRace(NpcRaces race);
    void ReSetVoiceRaces(EchokrautVoice voice, EKEventId? eventId = null);
    void ReSetVoiceGenders(EchokrautVoice voice, EKEventId? eventId = null);
    void MigrateOldData(EchokrautVoice? oldVoice = null, EchokrautVoice? newEkVoice = null);
    void RefreshSelectables(List<EchokrautVoice> voices);
    NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId eventId, IBackendService backend);
}
