using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using Echokraut.TextToTalk.Utils;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Character = Dalamud.Game.ClientState.Objects.Types.ICharacter;

namespace Echokraut.Helper.DataHelper;

public static class DalamudHelper
{
    public static IGameObject? GetGameObjectByName(IClientState clientState, IObjectTable objects, SeString? name, EKEventId eventId)
    {
        // Names are complicated; the name SeString can come from chat, meaning it can
        // include the cross-world icon or friend group icons or whatever else.
        if (name is null) return null;
        if (string.IsNullOrWhiteSpace(name.TextValue)) return null;
        if (!TalkTextHelper.TryGetEntityName(name, out var parsedName)) return null;
        var obj =  objects.FirstOrDefault(gObj =>
            TalkTextHelper.TryGetEntityName(gObj.Name, out var gObjName) && gObjName == parsedName);
        LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Found Gameobject: {obj} by name: {name.TextValue} and parsedName: {parsedName}", eventId);

        return obj;
    }
    public static IGameObject? TryGetUnnamedObject(IClientState clientState, IObjectTable objects, SeString? name, EKEventId eventId)
    {
        IGameObject? gameObject = null;
        foreach (var item in objects)
        {
            var character = item as Character;

            if (character == null) continue;
            LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Looking at ??? character: {character.Name} - {character.Position.X}/{character.Position.Y}/{character.Position.Z}", eventId);

            gameObject = character;
        }

        return gameObject;
    }
}
