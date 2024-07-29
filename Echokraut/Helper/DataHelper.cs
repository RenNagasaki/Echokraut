using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Windows;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
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

        public static string GetRaceEng(string nationalRace, IPluginLog Log)
        {
            string engRace = nationalRace.Replace("'", "");

            if (NpcRacesMap.ContainsKey(engRace))
                engRace = NpcRacesMap[engRace];

            return engRace;
        }

        public static NpcMapData GetCharacterMapData(List<NpcMapData> npcDatas, List<NpcMapData> playerDatas, NpcMapData data, EKEventId  eventId)
        {
            NpcMapData? result = null;
            var datas = data.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player || eventId.textSource == TextSource.Chat ? playerDatas : npcDatas;
            result = datas.Find(p => p.ToString() == data.ToString());

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Tried finding existing mapping for: {data.ToString()} result: {result?.ToString()}", eventId);

            return result;
        }

        public static void AddCharacterMapData(List<NpcMapData> npcDatas, List<NpcMapData> playerDatas, NpcMapData data, EKEventId eventId)
        {
            if (data.objectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player || eventId.textSource == TextSource.Chat)
            {
                playerDatas.Add(data);
                ConfigWindow.UpdatePlayerData = true;
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Added new player to mapping: {data.ToString()}", eventId);
            }
            else
            {
                npcDatas.Add(data);
                ConfigWindow.UpdateNpcData = true;
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Added new npc to mapping: {data.ToString()}", eventId);
            }
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
