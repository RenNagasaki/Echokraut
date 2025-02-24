using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Echokraut.Enums;
using Echokraut.DataClasses;
using System.Reflection;
using System;
using System.Collections.Generic;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.API;

namespace Echokraut.Helper.Data;

public static class CharacterDataHelper
{
    public static Dictionary<string, string> NpcRacesMap = new Dictionary<string, string>()
    {
        { "Hyuran", "Hyur" }

    };

    public static bool IsWildRace(NpcRaces race)
    {
        switch (race)
        {
            case NpcRaces.Hyur:
            case NpcRaces.AuRa:
            case NpcRaces.Miqote:
            case NpcRaces.Roegadyn:
            case NpcRaces.Hrothgar:
            case NpcRaces.Lalafell:
            case NpcRaces.Elezen:
            case NpcRaces.Viera:
                return false;
                break;

        }

        return true;
    }

    public static unsafe Genders GetCharacterGender(IDataManager dataManager, EKEventId eventId, IGameObject? speaker, NpcRaces race, out uint? modelBody)
    {
        modelBody = new uint?();
        if (speaker == null || speaker.Address == nint.Zero)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "GameObject is null; cannot check gender.", eventId);
            return Genders.None;
        }

        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;

        // Get actor gender as defined by its struct.
        var actorGender = (Genders)charaStruct->DrawData.CustomizeData.Sex;
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Gender found on GameObject: {actorGender}", eventId);

        // Player gender overrides will be handled by a different system.
        if (speaker.ObjectKind is ObjectKind.Player)
        {
            return actorGender;
        }

        if (actorGender == Genders.Male && IsWildRace(race))
        {
            modelBody = LuminaHelper.GetENpcBase(speaker.DataId)?.ModelBody;
            var modBody = modelBody;
            var npcGenderMap = JsonLoaderHelper.ModelGenderMap.Find(p => p.race == race && p.maleDefault && p.male != modBody);
            if (npcGenderMap == null)
                npcGenderMap = JsonLoaderHelper.ModelGenderMap.Find(p => p.race == race && !p.maleDefault && p.female == modBody);
            else
                actorGender = Genders.Female;

            if (npcGenderMap != null)
                actorGender = Genders.Female;
        }

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Got ModelBody: {modelBody ?? 0} for {speaker.ObjectKind} \"{speaker.Name}\" - ID:{speaker.DataId} (gender read as: {actorGender})", eventId);
        return actorGender;
    }

    public static unsafe NpcRaces GetSpeakerRace(IDataManager dataManager, EKEventId eventId, IGameObject? speaker, out string raceStr, out int modelId)
    {
        var raceEnum = NpcRaces.Unknown;
        modelId = 0;
        try
        {
            if (speaker is null || speaker.Address == nint.Zero)
            {
                raceStr = raceEnum.ToString();
                return raceEnum;
            }

            var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
            var speakerRace = charaStruct->DrawData.CustomizeData.Race;
            var race = LuminaHelper.GetRace(speakerRace);

            if (!(race is null))
            {
                raceStr = GetRaceEng(race.Value.Masculine.ExtractText());
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found Race: {raceStr}", eventId);
                if (!Enum.TryParse(raceStr.Replace(" ", ""), out raceEnum))
                {
                    var modelData = charaStruct->ModelContainer.ModelSkeletonId;
                    var modelData2 = charaStruct->ModelContainer.ModelSkeletonId_2;

                    var activeData = modelData;
                    if (activeData == -1)
                        activeData = modelData2;

                    modelId = activeData;
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"ModelId for Race matching: {activeData}", eventId);
                    var activeNpcRace = NpcRaces.Unknown;
                    try
                    {
                        if (JsonLoaderHelper.ModelsToRaceMap.TryGetValue(activeData, out activeNpcRace))
                            raceEnum = activeNpcRace;
                        else
                        {
                            raceEnum = NpcRaces.Unknown;
                        }
                    }
                    catch (Exception ex)
                    {
                        raceEnum = NpcRaces.Unknown;
                    }
                    raceStr = activeData.ToString();
                }
            }

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Determined Race: {raceEnum}", eventId);
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while determining race: {ex}", eventId);
        }

        raceStr = raceEnum.ToString();
        return raceEnum;
    }

    public static string GetRaceEng(string nationalRace)
    {
        var engRace = nationalRace.Replace("'", "");

        if (NpcRacesMap.ContainsKey(engRace))
            engRace = NpcRacesMap[engRace];

        return engRace;
    }

}
