using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;

namespace Echokraut.Services;

public interface IGameObjectService
{
    IGameObject? GetGameObjectByName(SeString? name, EKEventId eventId);
    void TryGetNextUnknownCharacter(EKEventId eventId);
    void ClearLastUnknownState();
    IGameObject? LocalPlayer { get; }
    string LocalPlayerName { get; }
    ulong LocalPlayerContentId { get; }
    bool LocalPlayerIsMale { get; }
    IGameObject? NextUnknownCharacter { get; }
    /// <summary>
    /// BaseIds (ENpcBase row IDs) of every NPC currently spawned in the object table.
    /// Used by the live alias-resolver to filter multi-match alias candidates down to
    /// "physically present in the current cutscene". Excludes player characters.
    /// </summary>
    HashSet<uint> GetSpawnedNpcBaseIds();
}
