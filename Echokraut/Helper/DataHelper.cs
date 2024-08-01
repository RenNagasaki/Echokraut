using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Windows;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.GeneratedSheets;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
        }

        public static EKEventId EventId(string methodName, TextSource textSource)
        {
            var eventId = new EKEventId(NextEventId, textSource);
            NextEventId++;

            LogHelper.Start(methodName, eventId);
            return eventId;
        }

        public static Dictionary<string, string> NpcRacesMap = new Dictionary<string, string>()
        {
            { "Hyuran", "Hyur" }

        };

        public static string GetRaceEng(string nationalRace)
        {
            string engRace = nationalRace.Replace("'", "");

            if (NpcRacesMap.ContainsKey(engRace))
                engRace = NpcRacesMap[engRace];

            return engRace;
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

        private static List<NpcMapData> GetCharacterMapDatas(TextSource textSource) {
            List<NpcMapData> datas = new List<NpcMapData>();

            switch (textSource)
            {
                case TextSource.AddonTalk:
                case TextSource.AddonBattleTalk:
                case TextSource.AddonBubble:
                    datas = Configuration.MappedNpcs;
                    break;
                case TextSource.AddonSelectString:
                case TextSource.AddonCutSceneSelectString:
                case TextSource.Chat:
                    datas = Configuration.MappedPlayers;
                    break;
            }

            return datas;
        }

        public static NpcMapData GetAddCharacterMapData(NpcMapData data, EKEventId  eventId)
        {
            NpcMapData? result = null;
            var datas = GetCharacterMapDatas(eventId.textSource);

            result = datas.Find(p => p.ToString() == data.ToString());

            if (result == null)
            {
                datas.Add(data);
                ConfigWindow.UpdateNpcData = true;
                ConfigWindow.UpdateBubbleData = true;
                ConfigWindow.UpdatePlayerData = true;
                var mapping = data.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player ? "player" : "npc";
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Added new {mapping} to mapping: {data.ToString()}", eventId);

                result = data;
            }
            else
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found existing mapping for: {data.ToString()} result.", eventId);

            return result;
        }

        public static string AnalyzeAndImproveText(string text)
        {
            var resultText = text;

            resultText = Regex.Replace(resultText, "(?<=^|[^/.\\w])[a-zA-ZäöüÄÖÜ]+[\\.\\,\\!\\?](?=[a-zA-ZäöüÄÖÜ])", "$& ");

            return resultText;
        }

        public static string CleanUpName(string name)
        {
            name = name.Replace("[a]", "");
            name = Regex.Replace(name, "[^a-zA-Z0-9-äöüÄÖÜ' ]+", "");

            return name;
        }

        public static string VoiceMessageToFileName(string voiceMessage)
        {
            string fileName = voiceMessage;
            string[] temp = fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
            fileName = String.Join("", temp).ToLower().Replace(" ", "").Replace(".", "").Replace("!", "").Replace(",", "").Replace("-", "").Replace("_", "");
            if (fileName.Length > 120)
                fileName = fileName.Substring(0, 120);

            return fileName;
        }
    }
}
