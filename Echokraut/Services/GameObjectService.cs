using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Character = Dalamud.Game.ClientState.Objects.Types.ICharacter;

namespace Echokraut.Services;

public class GameObjectService : IGameObjectService
{
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ILogService _logService;
    private readonly ITextProcessingService _textProcessingService;
    private readonly Dictionary<string, bool> _lastUnknownState = new();
    
    private IGameObject? _nextUnknownCharacter;
    private string _localPlayerName = "";

    public IGameObject? LocalPlayer
    {
        get
        {
            var player = _objectTable.LocalPlayer;
            if (player != null) _localPlayerName = player.Name.TextValue;
            return player;
        }
    }

    // Safe to read from any thread — updated whenever LocalPlayer is accessed on the main thread.
    public string LocalPlayerName => _localPlayerName;

    public IGameObject? NextUnknownCharacter => _nextUnknownCharacter;

    public GameObjectService(
        IClientState clientState,
        IObjectTable objectTable,
        ILogService logService,
        ITextProcessingService textProcessingService)
    {
        _clientState = clientState ?? throw new System.ArgumentNullException(nameof(clientState));
        _objectTable = objectTable ?? throw new System.ArgumentNullException(nameof(objectTable));
        _logService = logService ?? throw new System.ArgumentNullException(nameof(logService));
        _textProcessingService = textProcessingService ?? throw new System.ArgumentNullException(nameof(textProcessingService));
    }

    public IGameObject? GetGameObjectByName(SeString? name, EKEventId eventId)
    { 
        if (name is null) return null;
        if (string.IsNullOrWhiteSpace(name.TextValue)) return null;
        if (!_textProcessingService.TryGetEntityName(name, out var parsedName)) return null;
        
        var obj = _objectTable.FirstOrDefault(gObj =>
            _textProcessingService.TryGetEntityName(gObj.Name, out var gObjName) && gObjName == parsedName);
        
        _logService.Info(nameof(GetGameObjectByName), 
            $"Found GameObject: {obj} by name: {name.TextValue} and parsedName: {parsedName}", eventId);
 
        return obj;
    }

    public void TryGetNextUnknownCharacter(EKEventId eventId)
    {
        foreach (var item in _objectTable)
        {
            if (item is not Character character) continue;
            if (character.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion) continue;
            if (string.IsNullOrWhiteSpace(character.Name.TextValue.Trim())) continue;

            if (!_lastUnknownState.ContainsKey(character.Name.TextValue))
                _lastUnknownState.Add(character.Name.TextValue, character.IsTargetable);

            if (character.IsTargetable && !_lastUnknownState[character.Name.TextValue])
            {
                _nextUnknownCharacter = character;
                _lastUnknownState[character.Name.TextValue] = true;
            }

            _lastUnknownState[character.Name.TextValue] = character.IsTargetable;
        }

        _logService.Debug(nameof(TryGetNextUnknownCharacter),
            _nextUnknownCharacter != null
                ? $"Found next unknown character: {_nextUnknownCharacter.Name}"
                : "No new unknown character found",
            eventId);
    }

    public void ClearLastUnknownState()
    {
        _logService.Debug(nameof(ClearLastUnknownState), "Clearing last unknown state", new EKEventId(0, TextSource.None));
        _lastUnknownState.Clear();
        _nextUnknownCharacter = null;
    }
}
