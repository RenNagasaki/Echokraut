using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>What the game looks like right now, read on the framework thread.</summary>
public sealed record E2eGameSnapshot(
    bool LoggedIn,
    string PlayerName,
    bool InCutscene,
    bool InDuty,
    bool TalkOpen,
    bool BattleTalkOpen);

/// <summary>Reads <see cref="E2eGameSnapshot"/>; mocked in the stage tests.</summary>
public interface IE2eGameProbe
{
    Task<E2eGameSnapshot> SnapshotAsync();
}

public sealed class E2eGameProbe : IE2eGameProbe
{
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IGameGui _gameGui;
    private readonly IGameObjectService _gameObjects;

    public E2eGameProbe(IFramework framework, IClientState clientState, ICondition condition, IGameGui gameGui,
        IGameObjectService gameObjects)
    {
        _framework = framework;
        _clientState = clientState;
        _condition = condition;
        _gameGui = gameGui;
        _gameObjects = gameObjects;
    }

    public Task<E2eGameSnapshot> SnapshotAsync() => _framework.RunOnFrameworkThread(() =>
    {
        _gameObjects.RefreshLocalPlayer();
        return new E2eGameSnapshot(
            _clientState.IsLoggedIn,
            _gameObjects.LocalPlayerName ?? "",
            _condition[ConditionFlag.OccupiedInCutSceneEvent] || _condition[ConditionFlag.WatchingCutscene]
                || _condition[ConditionFlag.WatchingCutscene78],
            _condition[ConditionFlag.BoundByDuty] || _condition[ConditionFlag.BoundByDuty56]
                || _condition[ConditionFlag.BoundByDuty95],
            IsVisible("Talk"),
            IsVisible("_BattleTalk"));
    });

    private bool IsVisible(string addon)
    {
        var ptr = _gameGui.GetAddonByName(addon);
        return !ptr.IsNull && ptr.IsVisible;
    }
}
