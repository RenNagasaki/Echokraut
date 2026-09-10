using Echokraut.Enums;

namespace Echokraut.DataClasses;

/// <summary>
/// Flat DTO returned by <c>IDatabaseService.GetAllInstancesForRepair</c>. Carries the
/// joined identity of a <c>character_instance</c> row plus its parent character's
/// canonical identity tuple. Used by <c>NpcAttributionRepairService</c> to detect
/// mis-attributed instances without holding an EF Core change-tracking graph.
/// </summary>
public sealed record AttributionInstanceRow(
    int CharacterId,
    string CharacterName,
    Genders CharacterGender,
    NpcRaces CharacterRace,
    int Language,
    long NpcBaseId);
