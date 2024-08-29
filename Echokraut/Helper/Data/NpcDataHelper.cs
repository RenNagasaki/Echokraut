using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.API;
using Echokraut.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        public static void RefreshSelectables()
        {
            try
            {
                Configuration.MappedNpcs.ForEach(p => p.voicesSelectable = new($"##AllVoices{p.ToString()}", string.Empty, 300, BackendVoiceHelper.Voices, g => g.ToString()));
                Configuration.MappedPlayers.ForEach(p => p.voicesSelectable = new($"##AllVoices{p.ToString()}", string.Empty, 300, BackendVoiceHelper.Voices, g => g.ToString()));
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

            if (data.race == NpcRaces.Unknown)
            {
                var oldResult = datas.Find(p => p.ToString() == data.ToString());
                result = datas.Find(p => p.name == data.name && p.race != NpcRaces.Unknown);

                if (result != null)
                    datas.Remove(oldResult);
            }
            else if (data.race != NpcRaces.Unknown)
            {
                result = datas.Find(p => p.name == data.name && p.race == NpcRaces.Unknown);

                if (result != null)
                {
                    data.voiceItem = result.voiceItem;
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
                    data.voicesSelectable = new($"##AllVoices{data.ToString()}", string.Empty, 250, BackendVoiceHelper.Voices, g => g.ToString());
                    BackendHelper.GetVoiceOrRandom(eventId, data);
                    ConfigWindow.UpdateDataNpcs = true;
                    ConfigWindow.UpdateDataBubbles = true;
                    ConfigWindow.UpdateDataPlayers = true;
                    var mapping = data.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ? "player" : "npc";
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Added new {mapping} to mapping: {data.ToString()}", eventId);

                    result = data;
                }
                else
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);
            }
            else
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found existing mapping for: {data.ToString()} result: {result.ToString()}", eventId);

            return result;
        }
    }
}
