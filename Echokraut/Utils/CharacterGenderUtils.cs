using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Echokraut.Enums;
using Echokraut.Helper;
using Echokraut.DataClasses;
using System.Reflection;

namespace Echokraut.Utils;

public static class CharacterGenderUtils
{
    // TODO: Use NPC ID instead of reading the model information :(
    public static unsafe Gender GetCharacterGender(EKEventId eventId, IGameObject? gObj)
    {
        if (gObj == null || gObj.Address == nint.Zero)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "GameObject is null; cannot check gender.", eventId);
            return Gender.None;
        }

        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)gObj.Address;

        // Get actor gender as defined by its struct.
        var actorGender = (Gender)charaStruct->DrawData.CustomizeData.Sex;

        // Player gender overrides will be handled by a different system.
        if (gObj.ObjectKind is ObjectKind.Player)
        {
            return actorGender;
        }

        // Get the actor's model ID to see if we have an ungendered override for it.
        // Actors only have 0/1 genders regardless of their canonical genders, so this
        // needs to be specified by us. If an actor is canonically ungendered, their
        // gender seems to always be left at 0 (male).
        var modelId = charaStruct->CharacterData.ModelSkeletonId_2;
        if (modelId == -1)
        {
            modelId = charaStruct->CharacterData.ModelSkeletonId;
        }

        LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Got model ID {modelId} for {gObj.ObjectKind} \"{gObj.Name}\" (gender read as: {actorGender})", eventId);

        return actorGender;
    }
}
