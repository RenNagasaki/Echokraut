using System.Threading;
using System.Threading.Tasks;
using Echokraut.Enums;

namespace Echokraut.Services;

/// <summary>
/// Resolves player Race/Gender via the Lodestone (Square Enix's character search).
/// Cached in DB with a 30-day TTL; misses are negatively cached so we don't keep retrying.
/// </summary>
public interface ILodestoneService
{
    /// <summary>
    /// Look up a player by (Name, World). World must already be the English server name.
    /// Returns null if not found / lookup failed; otherwise (race, raceStr, gender).
    /// Hits the local DB cache first, then queries Lodestone if stale or missing.
    /// </summary>
    Task<LodestoneResult?> LookupAsync(string name, string world, CancellationToken ct = default);
}

public record LodestoneResult(NpcRaces Race, string RaceStr, Genders Gender);
