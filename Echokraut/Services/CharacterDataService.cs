using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Generic;

namespace Echokraut.Services;

public class CharacterDataService : ICharacterDataService
{
    private readonly ILogService _logService;
    private readonly IJsonDataService _jsonData;
    private readonly ILuminaService _lumina;

    private static readonly Dictionary<string, string> NpcRacesMap = new()
    {
        { "Hyuran", "Hyur" }
    };

    public CharacterDataService(ILogService logService, IJsonDataService jsonData, ILuminaService lumina)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
        _lumina = lumina ?? throw new ArgumentNullException(nameof(lumina));
    }

    public unsafe Genders GetCharacterGender(EKEventId eventId, IGameObject? speaker, NpcRaces race, out byte modelBody)
    {
        modelBody = 0;
        if (speaker == null || speaker.Address == nint.Zero)
        {
            _logService.Debug(nameof(GetCharacterGender), "GameObject is null; cannot check gender.", eventId);
            return Genders.None;
        }

        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var actorGender = (Genders)charaStruct->DrawData.CustomizeData.Sex;

        _logService.Info(nameof(GetCharacterGender), $"Gender found on GameObject: {actorGender}", eventId);

        if (speaker.ObjectKind is ObjectKind.Player)
        {
            return actorGender;
        }

        if (actorGender == Genders.Male && IsWildRace(race))
        {
            var npcBase = _lumina.GetENpcBase(speaker.BaseId, eventId);
            if (npcBase != null && npcBase.Value.ModelBody < 256)
            {
                modelBody = (byte)npcBase.Value.ModelBody;
                var localModelBody = modelBody;
                var npcGenderMap = _jsonData.ModelGenderMap.Find(p =>
                    p.race == race && p.maleDefault && p.male != localModelBody);

                if (npcGenderMap == null)
                {
                    npcGenderMap = _jsonData.ModelGenderMap.Find(p =>
                        p.race == race && !p.maleDefault && p.female == localModelBody);
                }

                if (npcGenderMap != null)
                    actorGender = Genders.Female;
            }
        }

        _logService.Info(nameof(GetCharacterGender),
            $"Got ModelBody: {modelBody} for {speaker.ObjectKind} \"{speaker.Name}\" - ID:{speaker.BaseId} (gender read as: {actorGender})",
            eventId);
        
        return actorGender;
    }

    public unsafe NpcRaces GetSpeakerRace(EKEventId eventId, IGameObject? speaker, out string raceStr, out int modelId)
    {
        var raceEnum = NpcRaces.Unknown;
        modelId = 0;
        
        if (speaker is null || speaker.Address == nint.Zero)
        {
            raceStr = raceEnum.ToString();
            return raceEnum;
        }

        var charaStruct = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)speaker.Address;
        var speakerRace = charaStruct->DrawData.CustomizeData.Race;
        var race = _lumina.GetRace(speakerRace, eventId);

        if (race is not null)
        {
            raceStr = GetRaceEng(race.Value.Masculine.ExtractText());
            if (Enum.TryParse(raceStr, out raceEnum))
            {
                modelId = charaStruct->ModelContainer.ModelSkeletonId;
                _logService.Info(nameof(GetSpeakerRace),
                    $"Race found on GameObject: {raceStr} with ModelId: {modelId}", eventId);
            }
        }
        else
        {
            raceStr = raceEnum.ToString();
        }

        return raceEnum;
    }

    private static bool IsWildRace(NpcRaces race)
    {
        return race switch
        {
            NpcRaces.Hyur or NpcRaces.AuRa or NpcRaces.Miqote or NpcRaces.Roegadyn or
            NpcRaces.Hrothgar or NpcRaces.Lalafell or NpcRaces.Elezen or NpcRaces.Viera => false,
            _ => true
        };
    }

    private static string GetRaceEng(string race)
    {
        return NpcRacesMap.TryGetValue(race, out var mappedRace) ? mappedRace : race;
    }
}
