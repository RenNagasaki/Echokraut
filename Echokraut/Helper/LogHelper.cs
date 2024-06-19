using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public static class LogHelper
    {
        private static IPluginLog Log;
        private static Configuration Config;
        private static Dictionary<DateTime, string> infoLogs = new Dictionary<DateTime, string>();
        private static Dictionary<DateTime, string> debugLogs = new Dictionary<DateTime, string>();
        private static Dictionary<DateTime, string> errorLogs = new Dictionary<DateTime, string>();
        public static Dictionary<DateTime, string> logList = new Dictionary<DateTime, string>();

        public static void Setup(IPluginLog log, Configuration config)
        {
            Log = log;
            Config = config;
        }

        public static void Info(string text)
        {
            infoLogs.Add(DateTime.Now, "INF" + text);

            if (Config.ShowInfoLog)
                logList.Add(DateTime.Now, "INF" + text);

            Log.Info(text);
        }

        public static void Debug(string text)
        {
            debugLogs.Add(DateTime.Now, "DBG" + text);

            if (Config.ShowDebugLog)
                logList.Add(DateTime.Now, "DBG" + text);

            Log.Debug(text);
        }

        public static void Error(string text)
        {
            errorLogs.Add(DateTime.Now, "ERR" + text);

            if (Config.ShowErrorLog)
                logList.Add(DateTime.Now, "ERR" + text);

            Log.Error(text);
        }

        public static void RecreateLogList()
        {
            List<DateTime> logListKeys = new List<DateTime>();
            logList = new Dictionary<DateTime, string>();

            if (Config.ShowInfoLog)
                logListKeys.AddRange(LogHelper.infoLogs.Keys.ToList());
            if (Config.ShowDebugLog)
                logListKeys.AddRange(LogHelper.debugLogs.Keys.ToList());
            if (Config.ShowErrorLog)
                logListKeys.AddRange(LogHelper.errorLogs.Keys.ToList());

            logListKeys.Sort();
            logListKeys.ForEach(key => logList.Add(key, 
                infoLogs.ContainsKey(key) && Config.ShowInfoLog ? infoLogs[key] : 
                    debugLogs.ContainsKey(key) && Config.ShowDebugLog ? debugLogs[key] : 
                        errorLogs.ContainsKey(key) && Config.ShowErrorLog ? errorLogs[key] : ""));
        }
    }
}
