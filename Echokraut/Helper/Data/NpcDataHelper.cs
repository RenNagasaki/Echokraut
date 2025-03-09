using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.API;
using Echokraut.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Echokraut.Helper.Data
{
    public static class NpcDataHelper
    {
        static int NextEventId = 1;
        private static Configuration Configuration;

        public static void Setup(Configuration configuration)
        {
            Configuration = configuration;
        }

        public static bool IsGenderedRace(NpcRaces race)
        {
            if (((int)race > 0 && (int)race < 9) || JsonLoaderHelper.ModelGenderMap.Find(p => p.race == race) != null)
                return true;
                
            return false;
        }

        public static void ReSetVoiceRaces(EchokrautVoice voice, EKEventId? eventId = null)
        {
            if (eventId == null)
                eventId = new EKEventId(0, TextSource.None);
            
            voice.AllowedRaces.Clear();
            string[] splitVoice = voice.voiceName.Split('_');

            if (splitVoice.Length == 3)
            {
                var racesStr = splitVoice[1];
                var raceStrArr = racesStr.Split('-');
                foreach (var raceStr in raceStrArr)
                {
                    if (Enum.TryParse(typeof(NpcRaces), raceStr, true, out object? race))
                    {
                        voice.AllowedRaces.Add((NpcRaces)race);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found {race} race", eventId);
                    }
                    else if (raceStr.Equals("Child", StringComparison.InvariantCultureIgnoreCase))
                    {
                        voice.IsChildVoice = true;
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found Child option", eventId);
                    }
                    else if (raceStr.Equals("All", StringComparison.InvariantCultureIgnoreCase))
                    {
                        foreach (var raceObj in Constants.RACELIST)
                        {
                            voice.AllowedRaces.Add(raceObj);
                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found {raceObj} race", eventId);
                        }
                    }
                    else
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Did not Find race", eventId);
                }
            }
        }

        public static void ReSetVoiceGenders(EchokrautVoice voice, EKEventId? eventId = null)
        {
            if (eventId == null)
                eventId = new EKEventId(0, TextSource.None);
            
            voice.AllowedGenders.Clear();
            string[] splitVoice = voice.voiceName.Split('_');

            if (splitVoice.Length == 3)
            {
                var genderStr = splitVoice[0];
                if (Enum.TryParse(typeof(Genders), genderStr, true, out object? gender))
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found {gender} gender", eventId);
                    voice.AllowedGenders.Add((Genders)gender);
                }
            }
        }

        public static void MigrateOldData(EchokrautVoice? oldVoice = null, EchokrautVoice? newEkVoice = null)
        {
            if (oldVoice == null)
            {
                var oldPlayerMapData = Configuration.MappedPlayers.FindAll(p => p.voiceItem != null);
                var oldNpcMapData = Configuration.MappedNpcs.FindAll(p => p.voiceItem != null);

                if (oldPlayerMapData.Count > 0 || oldNpcMapData.Count > 0)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Migrating old npcdata",
                                   new EKEventId(0, TextSource.None));

                    foreach (var player in oldPlayerMapData)
                    {
                        player.Voice =
                            Configuration.EchokrautVoices.Find(p => p.BackendVoice == player.voiceItem.Voice);

                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                        $"Migrated player {player.Name} from -> {player.voiceItem} to -> {player.Voice}",
                                        new EKEventId(0, TextSource.None));

                        if (player.Voice != null)
                            player.voiceItem = null;
                    }

                    foreach (var npc in oldNpcMapData)
                    {
                        npc.Voice = Configuration.EchokrautVoices.Find(p => p.BackendVoice == npc.voiceItem.Voice);

                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                        $"Migrated npc {npc.Name} from -> {npc.voiceItem} to -> {npc.Voice}",
                                        new EKEventId(0, TextSource.None));

                        if (npc.Voice != null)
                            npc.voiceItem = null;
                    }

                    Configuration.Save();
                }
            }
            else 
            {
                var oldPlayerMapData = Configuration.MappedPlayers.FindAll(p => p.Voice == oldVoice);
                var oldNpcMapData = Configuration.MappedNpcs.FindAll(p => p.Voice == oldVoice);

                if (oldPlayerMapData.Count > 0 || oldNpcMapData.Count > 0)
                {
                    if (newEkVoice != null)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name,
                                       $"Migrating old npcdata from {oldVoice} to {newEkVoice}",
                                       new EKEventId(0, TextSource.None));

                        foreach (var player in oldPlayerMapData)
                        {
                            player.Voice = newEkVoice;

                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                            $"Migrated player {player.Name} from -> {oldVoice} to -> {newEkVoice}",
                                            new EKEventId(0, TextSource.None));
                        }

                        foreach (var npc in oldNpcMapData)
                        {
                            npc.Voice = newEkVoice;

                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                            $"Migrated npc {npc.Name} from -> {oldVoice} to -> {newEkVoice}",
                                            new EKEventId(0, TextSource.None));
                        }
                    }
                    else
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name,
                                       $"Migrating old npcdata from {oldVoice} to NO VOICE",
                                       new EKEventId(0, TextSource.None));

                        foreach (var player in oldPlayerMapData)
                        {
                            player.Voice = null;

                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                            $"Migrated player {player.Name} from -> {oldVoice} to -> NO VOICE",
                                            new EKEventId(0, TextSource.None));
                        }

                        foreach (var npc in oldNpcMapData)
                        {
                            npc.Voice = null;

                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name,
                                            $"Migrated npc {npc.Name} from -> {oldVoice} to -> NO VOICE",
                                            new EKEventId(0, TextSource.None));
                        }
                    }

                    Configuration.Save();
                }
            }
        }

        public static EKEventId EventId(string methodName, TextSource textSource)
        {
            var eventId = new EKEventId(NextEventId, textSource);
            NextEventId++;

            LogHelper.Start(methodName, eventId);
            return eventId;
        }

        private static List<NpcMapData> GetCharacterMapDatas(EKEventId eventId)
        {
            switch (eventId.textSource)
            {
                case TextSource.AddonTalk:
                case TextSource.AddonBattleTalk:
                case TextSource.AddonBubble:
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found mapping: {Configuration.MappedNpcs} count: {Configuration.MappedNpcs.Count()}", eventId);
                    return Configuration.MappedNpcs;
                case TextSource.AddonSelectString:
                case TextSource.AddonCutsceneSelectString:
                case TextSource.Chat:
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found mapping: {Configuration.MappedPlayers} count: {Configuration.MappedPlayers.Count()}", eventId);
                    return Configuration.MappedPlayers;
            }

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Didn't find a mapping.", eventId);
            return new List<NpcMapData>();
        }

        public static EchokrautVoice GetVoiceByBackendVoice(string backendVoice)
        {
            return Configuration.EchokrautVoices.Find(p => p.BackendVoice == backendVoice);
        }

        public static void RefreshSelectables()
        {
            try
            {
                Configuration.MappedNpcs.ForEach(p => p.RefreshSelectable());
                Configuration.MappedPlayers.ForEach(p => p.RefreshSelectable());
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error Exception: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        public static NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId eventId)
        {
            NpcMapData? result = null;
            var datas = GetCharacterMapDatas(eventId);

            if (data.Race == NpcRaces.Unknown)
            {
                var oldResult = datas.Find(p => p.ToString() == data.ToString());
                result = datas.Find(p => p.Name == data.Name && p.Race != NpcRaces.Unknown);

                if (result != null)
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
                    data.Voices = Configuration.EchokrautVoices;
                    data.RefreshSelectable();
                    BackendHelper.GetVoiceOrRandom(eventId, data);
                    ConfigWindow.UpdateDataNpcs = true;
                    ConfigWindow.UpdateDataBubbles = true;
                    ConfigWindow.UpdateDataPlayers = true;
                    var mapping = data.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ? "player" : "npc";
                    LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Added new {mapping} to mapping: {data.ToString()}", eventId);

                    result = data;
                }
                else
                    LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);
            }
            else
                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);

            return result;
        }
    }
}
