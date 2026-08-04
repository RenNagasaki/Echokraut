using Echotools.Logging.Services;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
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
    private ulong _localPlayerContentId;
    private bool _localPlayerIsMale = true;

    public IGameObject? LocalPlayer
    {
        get
        {
            var player = _objectTable.LocalPlayer;
            if (player != null)
            {
                _localPlayerName = player.Name.TextValue;
                unsafe
                {
                    var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
                    _localPlayerIsMale = chara->DrawData.CustomizeData.Sex == 0;
                    _localPlayerContentId = chara->ContentId;
                }
            }
            return player;
        }
    }

    // Safe to read from any thread — updated whenever LocalPlayer is accessed on the main thread.
    public string LocalPlayerName => _localPlayerName;
    public ulong LocalPlayerContentId => _localPlayerContentId;
    public bool LocalPlayerIsMale => _localPlayerIsMale;

    public long GetEffectivePlayerContentId(bool hasPlayerPlaceholder)
        => hasPlayerPlaceholder ? (long)_localPlayerContentId : 0L;

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

    public HashSet<uint> GetSpawnedNpcBaseIds()
    {
        // Walk the object table and collect every non-player BaseId. The live alias resolver
        // intersects this set with character_instance.NpcBaseId values to disambiguate
        // multi-match aliases (typical for "???" speakers). Empty set when the table can't
        // be enumerated — caller falls through to existing "??? " heuristics.
        var result = new HashSet<uint>();
        try
        {
            foreach (var obj in _objectTable)
            {
                if (obj == null) continue;
                if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc) continue;
                if (obj.DataId == 0) continue;
                result.Add(obj.DataId);
            }
        }
        catch
        {
            // ObjectTable can throw if the game state is in flux (zone change, cutscene
            // transition). Returning an empty set is correct fallthrough behavior.
        }
        return result;
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
