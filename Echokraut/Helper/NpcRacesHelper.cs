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
    public static class NpcRacesHelper
    {
        private static string RemotePath = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcRaces.json";

        public static Dictionary<int, NpcRaces> ModelsToRaceMap;
        public static void Setup()
        {
            try
            {
                WebRequest request = WebRequest.Create(RemotePath);
                WebResponse reply;
                reply = request.GetResponse();
                StreamReader returninfo = new StreamReader(reply.GetResponseStream());
                string json = returninfo.ReadToEnd();
                if (json == null)
                {
                    ModelsToRaceMap = new Dictionary<int, NpcRaces>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load voiceNames.", new EKEventId(0, TextSource.None));
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
    }
}
