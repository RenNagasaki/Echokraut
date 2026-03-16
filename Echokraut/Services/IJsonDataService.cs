using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface IJsonDataService
{
    Dictionary<int, NpcRaces> ModelsToRaceMap { get; }
    List<NpcGenderRaceMap> ModelGenderMap { get; }
    List<string> Emoticons { get; }

    string GetNpcName(string npcName);
    void Reload(ClientLanguage language);
}
