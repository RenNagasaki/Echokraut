using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Anamnesis.Memory;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Extensions;
using Echokraut.Helper;
using Echokraut.TextToTalk.Utils;
using Character = Dalamud.Game.ClientState.Objects.Types.ICharacter;

namespace Echokraut.Utils;

public static class ObjectTableUtils
{
    static List<Character> knownCharacters = new List<Character>();

    public static IGameObject? GetGameObjectByName(IClientState clientState, IObjectTable objects, SeString? name, EKEventId eventId)
    {
        // Names are complicated; the name SeString can come from chat, meaning it can
        // include the cross-world icon or friend group icons or whatever else.
        if (name is null) return null;
        if (!TalkUtils.TryGetEntityName(name, out var parsedName)) return null;
        if (string.IsNullOrEmpty(name.TextValue)) return null;
        return objects.FirstOrDefault(gObj =>
            TalkUtils.TryGetEntityName(gObj.Name, out var gObjName) && gObjName == parsedName);
    }

    private static IGameObject? TryGetUnnamedObject(IClientState clientState, IObjectTable objects, SeString? name, EKEventId eventId)
    {
        IGameObject? gameObject = null;
        if (objects.Count() != knownCharacters.Count)
        {
            var newKnownCharacters = new List<Character>();
            foreach (var item in objects)
            {
                Character character = item as Character;

                if (character == null) continue;
                newKnownCharacters.Add(character);
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Looking at ??? character: {character.Name}", eventId);
                if (character == null || character == clientState.LocalPlayer || name.TextValue != "???" || knownCharacters.Contains(character)) continue;

                gameObject = character;
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Found ??? character: {character.Name}", eventId);
            }

            knownCharacters = newKnownCharacters;
        }

        return gameObject;
    }
}
