using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <inheritdoc/>
public class PlayerLodestoneEnricher : IPlayerLodestoneEnricher
{
    private readonly ILogService _log;
    private readonly INpcDataService _npcData;
    private readonly ILodestoneService _lodestone;
    private readonly ILuminaService _lumina;
    private readonly IObjectTable _objectTable;

    public PlayerLodestoneEnricher(
        ILogService log,
        INpcDataService npcData,
        ILodestoneService lodestone,
        ILuminaService lumina,
        IObjectTable objectTable)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _lodestone = lodestone ?? throw new ArgumentNullException(nameof(lodestone));
        _lumina = lumina ?? throw new ArgumentNullException(nameof(lumina));
        _objectTable = objectTable ?? throw new ArgumentNullException(nameof(objectTable));
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            var unknown = _npcData.MappedPlayers.Where(p => p.Race == NpcRaces.Unknown).ToList();
            if (unknown.Count == 0) return;

            // Resolve user's HomeWorld as fallback for entries that have no world stored.
            var homeWorld = "";
            var localPlayer = _objectTable.LocalPlayer;
            if (localPlayer != null)
                homeWorld = _lumina.GetWorldEnglishName(localPlayer.HomeWorld.RowId);

            _log.Info(nameof(RunAsync),
                $"Lodestone enrichment: {unknown.Count} player(s) with Race=Unknown; " +
                $"fallback world = '{homeWorld}'", eventId);

            var hits = 0;
            var deletes = 0;
            foreach (var p in unknown)
            {
                ct.ThrowIfCancellationRequested();
                var world = !string.IsNullOrWhiteSpace(p.World) ? p.World : homeWorld;
                if (string.IsNullOrWhiteSpace(world))
                {
                    // No world at all — can't look up. Leave the entry; user can manually fix.
                    continue;
                }

                var result = await _lodestone.LookupAsync(p.Name, world, ct);
                if (result != null)
                {
                    var oldName = p.Name;
                    var oldGender = p.Gender;
                    var oldRace = p.Race;
                    p.Race = result.Race;
                    p.RaceStr = result.RaceStr;
                    p.Gender = result.Gender;
                    if (string.IsNullOrWhiteSpace(p.World)) p.World = world;
                    _npcData.SaveCharacterWithOldIdentity(p, oldName, oldGender, oldRace);
                    hits++;
                }
                else
                {
                    _npcData.RemoveCharacter(p);
                    deletes++;
                }
            }

            _log.Info(nameof(RunAsync),
                $"Lodestone enrichment done: {hits} resolved, {deletes} removed (will re-appear from chat)",
                eventId);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Warning(nameof(RunAsync), $"Enrichment task failed: {ex.Message}", eventId);
        }
    }
}
