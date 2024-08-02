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
    public static class NpcGenderRacesHelper
    {
        private static string RacePath = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcRaces.json";
        private static string GendersPath = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcGenders.json";

        public static Dictionary<int, NpcRaces> ModelsToRaceMap;
        public static List<NpcGenderRaceMap> ModelsToGenderMap;
        public static void Setup()
        {
            LoadModelsToRaceMap();
            LoadModelsToGenderMap();
        }

        private static void LoadModelsToRaceMap()
        {
            try
            {
                WebRequest request = WebRequest.Create(RacePath);
                WebResponse reply;
                reply = request.GetResponse();
                StreamReader returninfo = new StreamReader(reply.GetResponseStream());
                string json = returninfo.ReadToEnd();
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
                WebRequest request = WebRequest.Create(GendersPath);
                WebResponse reply;
                reply = request.GetResponse();
                StreamReader returninfo = new StreamReader(reply.GetResponseStream());
                string json = returninfo.ReadToEnd();
                if (json == null)
                {
                    ModelsToGenderMap = new List<NpcGenderRaceMap>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load npc gender maps.", new EKEventId(0, TextSource.None));
                    return;
                }
                ModelsToGenderMap = System.Text.Json.JsonSerializer.Deserialize<List<NpcGenderRaceMap>>(json);
                LogHelper.Important(MethodBase.GetCurrentMethod().Name, $"Loaded npc gender maps for {ModelsToGenderMap.Count} races", new EKEventId(0, TextSource.None));
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading npc gender maps: {ex}", new EKEventId(0, TextSource.None));
            }
        }
    }
}
