using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class VoiceMapHelper
    {
        private static string VoiceNamesDE = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesDE.json";
        private static string VoiceNamesEN = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesEN.json";
        public static List<VoiceMap> VoiceMaps;

        public static void Setup(ClientLanguage clientLanguage)
        {
            try
            {
                string url = "";

                switch (clientLanguage)
                {
                    case ClientLanguage.German:
                        url = VoiceNamesDE;
                        break;
                    case ClientLanguage.English:
                    case ClientLanguage.Japanese:
                    case ClientLanguage.French:
                        url = VoiceNamesEN;
                        break;
                }
                WebRequest request = WebRequest.Create(url);
                WebResponse reply;
                reply = request.GetResponse();
                StreamReader returninfo = new StreamReader(reply.GetResponseStream());
                string json = returninfo.ReadToEnd();
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
            var voiceMap = VoiceMaps.Find(p => p.speakers.Contains(npcName));
            if (voiceMap != null)
                npcName = voiceMap.voiceName;

            return npcName;
        }
    }
}
