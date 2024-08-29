using System.Collections.Generic;
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
    private static Dictionary<string, bool> lastUnknownState = new Dictionary<string, bool>();
    public static IGameObject? nextUnknownCharacter = null;

    public static IGameObject? GetGameObjectByName(IClientState clientState, IObjectTable objects, SeString? name, EKEventId eventId)
    {
        // Names are complicated; the name SeString can come from chat, meaning it can
        // include the cross-world icon or friend group icons or whatever else.
        if (name is null) return null;
        if (string.IsNullOrWhiteSpace(name.TextValue)) return null;
        if (!TalkTextHelper.TryGetEntityName(name, out var parsedName)) return null;
        var obj =  objects.FirstOrDefault(gObj =>
            TalkTextHelper.TryGetEntityName(gObj.Name, out var gObjName) && gObjName == parsedName);
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Found Gameobject: {obj} by name: {name.TextValue} and parsedName: {parsedName}", eventId);

        return obj;
    }
    public static void TryGetNextUnkownCharacter(IClientState clientState, IObjectTable objects, EKEventId eventId)
    {
        IGameObject? gameObject = null;
        foreach (var item in objects)
        {
            var character = item as Character;

            if (character == null || character.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion || string.IsNullOrWhiteSpace(character.Name.TextValue.Trim())) continue;

            if (!lastUnknownState.ContainsKey(character.Name.TextValue))
                lastUnknownState.Add(character.Name.TextValue, character.IsTargetable);

            if (character.IsTargetable && !lastUnknownState[character.Name.TextValue])
            {
                nextUnknownCharacter = character;
                lastUnknownState[character.Name.TextValue] = true;
            }

            lastUnknownState[character.Name.TextValue] = character.IsTargetable;
        }
    }

    public static void ClearLastUnknownState()
    {
        lastUnknownState.Clear();
        nextUnknownCharacter = null;
    }
}
