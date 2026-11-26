using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface IJsonDataService
{
    Dictionary<int, NpcRaces> ModelsToRaceMap { get; }
    List<NpcGenderRaceMap> ModelGenderMap { get; }
    List<string> Emoticons { get; }

    /// <summary>
    /// Read-only view onto the loaded voice-name maps (canonical voice → list of in-game
    /// speaker variants). Consumed by the voice extractor as a fallback when a shortname
    /// from a TEXT_VOICEMAN_…_&lt;SHORTNAME&gt; key doesn't match any ENpcResident name —
    /// e.g. <c>"ICEHEART"</c> resolves to Ysayle through the <c>{voiceName: "Iceheart",
    /// speakers: ["Ysayle", "Iceheart"]}</c> entry.
    /// </summary>
    IReadOnlyList<VoiceMap> VoiceMaps { get; }

    string GetNpcName(string npcName);
    void Reload(ClientLanguage language);
}
