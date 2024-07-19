using Echokraut.DataClasses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class VoiceHelper
    {
        public static List<VoiceMap> voiceMaps;

        public static void Setup()
        {
            try
            {
                string resourceName = "Echokraut.Resources.VoiceNames.json";
                string json = ResourcesHelper.ReadResourceEmbedded(resourceName);
                if (json == null)
                {
                    voiceMaps = new List<VoiceMap>();
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to load voiceNames.json from embedded resources.", 0);
                    return;
                }
                voiceMaps = System.Text.Json.JsonSerializer.Deserialize<List<VoiceMap>>(json);
            }
            catch (Exception ex) 
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while loading voices map: {ex}", 0);
            }
        }
    }
}
