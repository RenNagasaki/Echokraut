using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Services;

public class NpcDataService : INpcDataService
{
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IJsonDataService _jsonData;

    public NpcDataService(ILogService log, Configuration config, IJsonDataService jsonData)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
    }

    public bool IsGenderedRace(NpcRaces race)
    {
        if (((int)race > 0 && (int)race < 9) || _jsonData.ModelGenderMap.Find(p => p.race == race) != null)
            return true;

        return false;
    }

    public void ReSetVoiceRaces(EchokrautVoice voice, EKEventId? eventId = null)
    {
        if (eventId == null)
            eventId = new EKEventId(0, TextSource.None);

        voice.AllowedRaces.Clear();
        string[] splitVoice = voice.voiceName.Split('_');

        foreach (var split in splitVoice)
        {
            var raceStrArr = split.Split('-');
            foreach (var raceStr in raceStrArr)
            {
                if (Enum.TryParse(typeof(NpcRaces), raceStr, true, out object? race))
                {
                    voice.AllowedRaces.Add((NpcRaces)race);
                    _log.Debug(nameof(ReSetVoiceRaces), $"Found {race} race", eventId);
                }
                else if (raceStr.Equals("Child", StringComparison.InvariantCultureIgnoreCase))
                {
                    voice.IsChildVoice = true;
                    _log.Debug(nameof(ReSetVoiceRaces), $"Found Child option", eventId);
                }
                else if (raceStr.Equals("All", StringComparison.InvariantCultureIgnoreCase))
                {
                    foreach (var raceObj in Constants.RACELIST)
                    {
                        voice.AllowedRaces.Add(raceObj);
                        _log.Debug(nameof(ReSetVoiceRaces), $"Found {raceObj} race", eventId);
                    }
                }
                else
                    _log.Debug(nameof(ReSetVoiceRaces), $"Did not Find race", eventId);
            }
        }
    }

    public void ReSetVoiceGenders(EchokrautVoice voice, EKEventId? eventId = null)
    {
        if (eventId == null)
            eventId = new EKEventId(0, TextSource.None);

        voice.AllowedGenders.Clear();
        string[] splitVoice = voice.voiceName.Split('_');

        foreach (var split in splitVoice)
        {
            if (Enum.TryParse(typeof(Genders), split, true, out object? gender))
            {
                _log.Debug(nameof(ReSetVoiceGenders), $"Found {gender} gender", eventId);
                voice.AllowedGenders.Add((Genders)gender);
            }
        }
    }

    public void MigrateOldData(EchokrautVoice? oldVoice = null, EchokrautVoice? newEkVoice = null)
    {
        if (oldVoice == null)
        {
            var oldPlayerMapData = _config.MappedPlayers.FindAll(p => p.voiceItem != null);
            var oldNpcMapData = _config.MappedNpcs.FindAll(p => p.voiceItem != null);

            if (oldPlayerMapData.Count > 0 || oldNpcMapData.Count > 0)
            {
                _log.Info(nameof(MigrateOldData), $"Migrating old npcdata", new EKEventId(0, TextSource.None));

                foreach (var player in oldPlayerMapData)
                {
                    player.Voice = _config.EchokrautVoices.Find(p => p.BackendVoice == player.voiceItem?.Voice);
                    _log.Debug(nameof(MigrateOldData), $"Migrated player {player.Name} from -> {player.voiceItem} to -> {player.Voice}", new EKEventId(0, TextSource.None));
                    if (player.Voice != null) player.voiceItem = null;
                }

                foreach (var npc in oldNpcMapData)
                {
                    npc.Voice = _config.EchokrautVoices.Find(p => p.BackendVoice == npc.voiceItem?.Voice);
                    _log.Debug(nameof(MigrateOldData), $"Migrated npc {npc.Name} from -> {npc.voiceItem} to -> {npc.Voice}", new EKEventId(0, TextSource.None));
                    if (npc.Voice != null) npc.voiceItem = null;
                }

                _config.Save();
            }
        }
        else
        {
            var oldPlayerMapData = _config.MappedPlayers.FindAll(p => p.Voice == oldVoice);
            var oldNpcMapData = _config.MappedNpcs.FindAll(p => p.Voice == oldVoice);

            if (oldPlayerMapData.Count > 0 || oldNpcMapData.Count > 0)
            {
                if (newEkVoice != null)
                {
                    _log.Info(nameof(MigrateOldData), $"Migrating old npcdata from {oldVoice} to {newEkVoice}", new EKEventId(0, TextSource.None));
                    foreach (var player in oldPlayerMapData)
                    {
                        player.Voice = newEkVoice;
                        _log.Debug(nameof(MigrateOldData), $"Migrated player {player.Name} from -> {oldVoice} to -> {newEkVoice}", new EKEventId(0, TextSource.None));
                    }
                    foreach (var npc in oldNpcMapData)
                    {
                        npc.Voice = newEkVoice;
                        _log.Debug(nameof(MigrateOldData), $"Migrated npc {npc.Name} from -> {oldVoice} to -> {newEkVoice}", new EKEventId(0, TextSource.None));
                    }
                }
                else
                {
                    _log.Info(nameof(MigrateOldData), $"Migrating old npcdata from {oldVoice} to NO VOICE", new EKEventId(0, TextSource.None));
                    foreach (var player in oldPlayerMapData)
                    {
                        player.Voice = null;
                        _log.Debug(nameof(MigrateOldData), $"Migrated player {player.Name} from -> {oldVoice} to -> NO VOICE", new EKEventId(0, TextSource.None));
                    }
                    foreach (var npc in oldNpcMapData)
                    {
                        npc.Voice = null;
                        _log.Debug(nameof(MigrateOldData), $"Migrated npc {npc.Name} from -> {oldVoice} to -> NO VOICE", new EKEventId(0, TextSource.None));
                    }
                }

                _config.Save();
            }
        }
    }

    public void RefreshSelectables(List<EchokrautVoice> voices)
    {
        try
        {
            _config.MappedNpcs.ForEach(p => { p.Voices = voices; p.RefreshSelectable(); });
            _config.MappedPlayers.ForEach(p => { p.Voices = voices; p.RefreshSelectable(); });
            _log.Debug(nameof(RefreshSelectables), $"Refreshed selectables: {_config.MappedNpcs.Count} NPCs, {_config.MappedPlayers.Count} players", new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(RefreshSelectables), $"Error Exception: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    public NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId eventId, IBackendService backend)
    {
        NpcMapData? result = null;
        var datas = GetCharacterMapDatas(eventId);

        if (data.Race == NpcRaces.Unknown)
        {
            var oldResult = datas.Find(p => p.ToString() == data.ToString());
            result = datas.Find(p => p.Name == data.Name && p.Race != NpcRaces.Unknown);

            if (result != null && oldResult != null)
                datas.Remove(oldResult);
        }
        else if (data.Race != NpcRaces.Unknown)
        {
            result = datas.Find(p => p.Name == data.Name && p.Race == NpcRaces.Unknown);

            if (result != null)
            {
                data.Voice = result.Voice;
                datas.Remove(result);
                result = null;
            }
        }

        if (result == null)
        {
            result = datas.Find(p => p.ToString() == data.ToString());

            if (result == null)
            {
                datas.Add(data);
                data.Voices = _config.EchokrautVoices;
                data.RefreshSelectable();
                backend.GetVoiceOrRandom(eventId, data);
                backend.NotifyCharacterMapped();
                var mapping = data.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ? "player" : "npc";
                _log.Debug(nameof(GetAddCharacterMapData), $"Added new {mapping} to mapping: {data.ToString()}", eventId);
                result = data;
            }
            else
                _log.Debug(nameof(GetAddCharacterMapData), $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);
        }
        else
            _log.Debug(nameof(GetAddCharacterMapData), $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);

        return result;
    }

    private List<NpcMapData> GetCharacterMapDatas(EKEventId eventId)
    {
        switch (eventId.textSource)
        {
            case TextSource.AddonTalk:
            case TextSource.AddonBattleTalk:
            case TextSource.AddonBubble:
                _log.Debug(nameof(GetCharacterMapDatas), $"Found mapping: {_config.MappedNpcs} count: {_config.MappedNpcs.Count}", eventId);
                return _config.MappedNpcs;
            case TextSource.AddonSelectString:
            case TextSource.AddonCutsceneSelectString:
            case TextSource.Chat:
                _log.Debug(nameof(GetCharacterMapDatas), $"Found mapping: {_config.MappedPlayers} count: {_config.MappedPlayers.Count}", eventId);
                return _config.MappedPlayers;
        }

        _log.Debug(nameof(GetCharacterMapDatas), $"Didn't find a mapping.", eventId);
        return new List<NpcMapData>();
    }
}
