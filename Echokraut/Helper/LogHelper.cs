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
        private static Dictionary<DateTime, string> InfoLogs = new Dictionary<DateTime, string>();
        private static Dictionary<DateTime, string> DebugLogs = new Dictionary<DateTime, string>();
        private static Dictionary<DateTime, string> ErrorLogs = new Dictionary<DateTime, string>();
        public static Dictionary<DateTime, string> logList = new Dictionary<DateTime, string>();

        public static void Setup(IPluginLog log, Configuration config)
        {
            Log = log;
            Config = config;
        }

        public static void Info(string method, string text)
        {
            text = $"{method} - {text}";
            InfoLogs.Add(DateTime.Now, $"INF{text}");

            if (Config.ShowInfoLog)
                logList.Add(DateTime.Now, $"INF{text}");

            Log.Info(text);
        }

        public static void Debug(string method, string text)
        {
            text = $"{method} - {text}";
            DebugLogs.Add(DateTime.Now, $"DBG{text}");

            if (Config.ShowDebugLog)
                logList.Add(DateTime.Now, $"DBG{text}");

            Log.Debug(text);
        }

        public static void Error(string method, string text)
        {
            text = $"{method} - {text}";
            ErrorLogs.Add(DateTime.Now, $"ERR{text}");

            if (Config.ShowErrorLog)
                logList.Add(DateTime.Now, $"ERR{text}");

            Log.Error(text);
        }

        public static void RecreateLogList()
        {
            List<DateTime> logListKeys = new List<DateTime>();
            logList = new Dictionary<DateTime, string>();

            if (Config.ShowInfoLog)
                logListKeys.AddRange(LogHelper.InfoLogs.Keys.ToList());
            if (Config.ShowDebugLog)
                logListKeys.AddRange(LogHelper.DebugLogs.Keys.ToList());
            if (Config.ShowErrorLog)
                logListKeys.AddRange(LogHelper.ErrorLogs.Keys.ToList());

            logListKeys.Sort();
            logListKeys.ForEach(key => logList.Add(key, 
                InfoLogs.ContainsKey(key) && Config.ShowInfoLog ? InfoLogs[key] : 
                    DebugLogs.ContainsKey(key) && Config.ShowDebugLog ? DebugLogs[key] : 
                        ErrorLogs.ContainsKey(key) && Config.ShowErrorLog ? ErrorLogs[key] : ""));
        }
    }
}
