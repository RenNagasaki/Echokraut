using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Echokraut.DataClasses;

namespace Echokraut.Services;

public interface IGameObjectService
{
    IGameObject? GetGameObjectByName(SeString? name, EKEventId eventId);
    void TryGetNextUnknownCharacter(EKEventId eventId);
    void ClearLastUnknownState();
    IGameObject? LocalPlayer { get; }
    string LocalPlayerName { get; }
    IGameObject? NextUnknownCharacter { get; }
}
