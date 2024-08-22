using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper.API
{
    public static class JsonLoaderHelper
    {
        private static string RacePath = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcRaces.json";
        private static string GendersPath = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcGenders.json";
        private static string EmoticonPath = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/Emoticons.json";
        private static string VoiceNamesDE = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesDE.json";
        private static string VoiceNamesEN = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesEN.json";
        private static string VoiceNamesFR = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesFR.json";
        private static string VoiceNamesJA = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesJA.json";

        public static Dictionary<int, NpcRaces> ModelsToRaceMap;
        public static List<NpcGenderRaceMap> ModelGenderMap;
        public static List<string> Emoticons;
        public static List<VoiceMap> VoiceMaps;
        public static void Setup(ClientLanguage language)
        {
            LoadModelsToRaceMap();
            LoadModelsToGenderMap();
            LoadEmoticons();
            LoadVoiceNames(language);
        }

        private static void LoadModelsToRaceMap()
        {
            try
            {
                var request = WebRequest.Create(RacePath);
                WebResponse reply;
                reply = request.GetResponse();
                var returninfo = new StreamReader(reply.GetResponseStream());
                var json = returninfo.ReadToEnd();
                if (json == null)
                {
                    ModelsToRaceMap = new Dictionary<int, NpcRaces>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load npc race maps.", new EKEventId(0, TextSource.None));
                    return;
                }
                ModelsToRaceMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, NpcRaces>>(json);
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Loaded npc race maps for {ModelsToRaceMap.Keys.Count} races", new EKEventId(0, TextSource.None));
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading npc race maps: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        private static void LoadModelsToGenderMap()
        {
            try
            {
                var request = WebRequest.Create(GendersPath);
                WebResponse reply;
                reply = request.GetResponse();
                var returninfo = new StreamReader(reply.GetResponseStream());
                var json = returninfo.ReadToEnd();
                if (json == null)
                {
                    ModelGenderMap = new List<NpcGenderRaceMap>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load npc gender maps.", new EKEventId(0, TextSource.None));
                    return;
                }
                ModelGenderMap = System.Text.Json.JsonSerializer.Deserialize<List<NpcGenderRaceMap>>(json);
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Loaded npc gender maps for {ModelGenderMap.Count} races", new EKEventId(0, TextSource.None));
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading npc gender maps: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        private static void LoadEmoticons()
        {
            try
            {
                var request = WebRequest.Create(EmoticonPath);
                WebResponse reply;
                reply = request.GetResponse();
                var returninfo = new StreamReader(reply.GetResponseStream());
                var json = returninfo.ReadToEnd();
                if (json == null)
                {
                    Emoticons = new List<string>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load emoticons.", new EKEventId(0, TextSource.None));
                    return;
                }
                Emoticons = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Loaded emoticons for {Emoticons.Count} emoticons", new EKEventId(0, TextSource.None));
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading emoticons: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        public static void LoadVoiceNames(ClientLanguage language)
        {
            try
            {
                var url = "";

                switch (language)
                {
                    case ClientLanguage.German:
                        url = VoiceNamesDE;
                        break;
                    case ClientLanguage.English:
                        url = VoiceNamesEN;
                        break;
                    case ClientLanguage.Japanese:
                        url = VoiceNamesJA;
                        break;
                    case ClientLanguage.French:
                        url = VoiceNamesFR;
                        break;
                }
                var request = WebRequest.Create(url);
                WebResponse reply;
                reply = request.GetResponse();
                var returninfo = new StreamReader(reply.GetResponseStream());
                var json = returninfo.ReadToEnd();
                if (json == null)
                {
                    VoiceMaps = new List<VoiceMap>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load voiceNames.", new EKEventId(0, TextSource.None));
                    return;
                }
                VoiceMaps = System.Text.Json.JsonSerializer.Deserialize<List<VoiceMap>>(json);
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Loaded voice name maps for {VoiceMaps?.Count} npcs", new EKEventId(0, TextSource.None));
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading voices map: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        public static string GetNpcName(string npcName)
        {
            var voiceMap = VoiceMaps.Find(p => p.speakers.Contains(npcName, StringComparer.OrdinalIgnoreCase));
            if (voiceMap != null)
                npcName = voiceMap.voiceName;

            return npcName;
        }
    }
}
