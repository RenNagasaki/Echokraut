using System;
using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Lumina.Excel.Sheets;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Stateless NPC identity resolution shared by the harvest, repair, and character-data
/// paths. These must produce the same canonical (race, gender) for a given <c>ENpcBase</c>
/// so a fresh harvest and a post-hoc repair land on the same character row. Each service
/// previously carried a byte-identical copy of this logic — extracted here to keep them in
/// lock-step (DRY).
/// </summary>
public static class NpcIdentityHelper
{
    /// <summary>
    /// Maps the English masculine race name from the Race sheet to the canonical
    /// <see cref="NpcRaces"/> spelling (no apostrophes/spaces) the enum uses.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RaceNameMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Hyuran", "Hyur" },
            { "Miqo'te", "Miqote" },
            { "Au Ra", "AuRa" },
        };

    /// <summary>Applies <see cref="RaceNameMap"/>, returning the input unchanged when unmapped.</summary>
    public static string CanonicalRaceName(string raceName)
        => RaceNameMap.TryGetValue(raceName, out var mapped) ? mapped : raceName;

    /// <summary>
    /// "Wild" races (beast tribes etc.) need the ModelBody gender heuristic; the eight
    /// playable races never do. Returns false for the playable races, true otherwise.
    /// </summary>
    public static bool IsWildRace(NpcRaces race) => race switch
    {
        NpcRaces.Hyur or NpcRaces.AuRa or NpcRaces.Miqote or NpcRaces.Roegadyn or
            NpcRaces.Hrothgar or NpcRaces.Lalafell or NpcRaces.Elezen or NpcRaces.Viera => false,
        _ => true
    };

    /// <summary>
    /// Resolves an NPC's gender from <c>ENpcBase.Gender</c>, applying the ModelBody →
    /// gender-map heuristic for wild races that report Male (which recovers Female NPCs).
    /// </summary>
    public static Genders DetermineGender(ENpcBase npcBase, NpcRaces race, List<NpcGenderRaceMap> modelGenderMap)
    {
        var gender = (Genders)npcBase.Gender;
        if (gender == Genders.Male && IsWildRace(race) && npcBase.ModelBody < 256)
        {
            var localModelBody = (byte)npcBase.ModelBody;
            var match = modelGenderMap.Find(p => p.race == race && p.maleDefault && p.male != localModelBody)
                     ?? modelGenderMap.Find(p => p.race == race && !p.maleDefault && p.female == localModelBody);
            if (match != null)
                gender = Genders.Female;
        }
        return gender;
    }
}
