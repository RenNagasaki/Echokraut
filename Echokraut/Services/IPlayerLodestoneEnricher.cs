using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// One-shot background task that resolves Race/Gender for player characters with Race=Unknown
/// via Lodestone. Used after migrating from the old config (where world wasn't captured) and
/// also as a clean-up for entries left over without a world. On miss the entry is deleted —
/// it'll be re-created from chat the next time the player speaks.
/// </summary>
public interface IPlayerLodestoneEnricher
{
    /// <summary>Run enrichment. Best-effort; never throws to caller.</summary>
    Task RunAsync(CancellationToken ct = default);
}
