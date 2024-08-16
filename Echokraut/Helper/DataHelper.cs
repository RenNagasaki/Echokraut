using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Utils;
using Echokraut.Windows;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Echokraut.Helper
{
    public static class DataHelper
    {
        static int NextEventId = 1;
        private static Configuration Configuration;
        private static IClientState ClientState;
        private static IDataManager DataManager;
        private static ushort TerritoryRow;
        private static TerritoryType? Territory;

        public static void Setup(Configuration configuration, IClientState clientState, IDataManager dataManager)
        {
            Configuration = configuration;
            ClientState = clientState;
            DataManager = dataManager;

            //Detector = new LanguageDetector();

            //Detector.AddLanguages(new string[]{ "en", "de", "fr", "ja" });
        }

        public static EKEventId EventId(string methodName, TextSource textSource)
        {
            var eventId = new EKEventId(NextEventId, textSource);
            NextEventId++;

            LogHelper.Start(methodName, eventId);
            return eventId; 
        }

        public static TerritoryType? GetTerritory()
        {
            var territoryRow = ClientState.TerritoryType;
            if (territoryRow != TerritoryRow)
            {
                TerritoryRow = territoryRow;
                Territory = DataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
            }

            return Territory;
        }

        private static List<NpcMapData> GetCharacterMapDatas(EKEventId eventId) {
            switch (eventId.textSource)
            {
                case TextSource.AddonTalk:
                case TextSource.AddonBattleTalk:
                case TextSource.AddonBubble:
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found mapping: {Configuration.MappedNpcs} count: {Configuration.MappedNpcs.Count()}", eventId);
                    return Configuration.MappedNpcs;
                case TextSource.AddonSelectString:
                case TextSource.AddonCutSceneSelectString:
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
                Configuration.MappedNpcs.ForEach(p => p.voicesSelectable = new($"##AllVoices{p.ToString()}", string.Empty, 250, BackendVoiceHelper.Voices, g => g.ToString()));
                Configuration.MappedPlayers.ForEach(p => p.voicesSelectable = new($"##AllVoices{p.ToString()}", string.Empty, 250, BackendVoiceHelper.Voices, g => g.ToString()));
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error Exception: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        public static NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId  eventId)
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

        public static string AnalyzeAndImproveText(string text)
        {
            var resultText = text;

            resultText = Regex.Replace(resultText, @"(?<=^|[^/.\w])[a-zA-ZäöüÄÖÜ]+[\.\,\!\?](?=[a-zA-ZäöüÄÖÜ])", "$& ");

            return resultText;
        }

        public static string CleanUpName(string name)
        {
            name = name.Replace("[a]", "");
            name = Regex.Replace(name, "[^a-zA-Z0-9-äöüÄÖÜ' ]+", "");

            return name;
        }

        internal unsafe static void PrintTargetInfo(IChatGui chatGui, IClientState clientState, IDataManager dataManager)
        {
            var localPlayer = clientState.LocalPlayer;

            if (localPlayer != null) {
                var target = localPlayer.TargetObject;
                if (target != null)
                {
                        var race = CharacterGenderRaceUtils.GetSpeakerRace(dataManager, new EKEventId(0, TextSource.None), target, out var raceStr, out var modelId);
                        var gender = CharacterGenderRaceUtils.GetCharacterGender(dataManager, new EKEventId(0, TextSource.None), target, race, out var modelBody);
                        var bodyType = dataManager.GetExcelSheet<ENpcBase>()!.GetRow(target.DataId)?.BodyType;
                        chatGui.Print(new Dalamud.Game.Text.XivChatEntry() { Name = target.Name, Message = $"Echokraut Target -> Name: {target.Name}, Race: {race}, Gender: {gender}, ModelID: {modelId}, ModelBody: {modelBody}, BodyType: {bodyType}", Timestamp = 22 * 60 + 12, Type = Dalamud.Game.Text.XivChatType.Echo });
                }
            }
        }
    }
}
