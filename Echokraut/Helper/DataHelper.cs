using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
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
        static public Dictionary<string, string> npcRacesMap = new Dictionary<string, string>()
        {
            { "Hyuran", "Hyur" }

        };

        static public string getRaceEng(string nationalRace, IPluginLog Log)
        {
            string engRace = nationalRace.Replace("'", "");

            if (npcRacesMap.ContainsKey(engRace))
                engRace = npcRacesMap[engRace];

            return engRace;
        }

        static public NpcMapData getNpcMapData(List<NpcMapData> datas, NpcMapData data)
        {
            NpcMapData result = null;

            foreach (var item in datas)
            {
                if (item.ToString() == data.ToString())
                {
                    result = item;
                    break;
                }
            }

            return result;
        }

        static public string analyzeAndImproveText(string text)
        {
            var resultText = text;

            resultText = Regex.Replace(resultText, "(?<=^|[^/.\\w])[a-zA-ZäöüÄÖÜ]+[\\.\\,\\!\\?](?=[a-zA-ZäöüÄÖÜ])", "$& ");

            return resultText;
        }

        static public string cleanUpName(string name)
        {
            name = name.Replace("[a]", "");
            name = Regex.Replace(name, "[^a-zA-Z0-9-äöüÄÖÜ' ]+", "");

            return name;
        }

        static public string unCleanUpName(string name)
        {
            name = name.Replace("+", " ").Replace("=", "'");

            return name;
        }

        static public string VoiceMessageToFileName(string voiceMessage)
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
