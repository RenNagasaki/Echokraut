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
        private static Dictionary<DateTime, LogMessage> InfoLogs = new Dictionary<DateTime, LogMessage>();
        private static Dictionary<DateTime, LogMessage> DebugLogs = new Dictionary<DateTime, LogMessage>();
        private static Dictionary<DateTime, LogMessage> ErrorLogs = new Dictionary<DateTime, LogMessage>();
        public static Dictionary<DateTime, LogMessage> logList = new Dictionary<DateTime, LogMessage>();

        public static void Setup(IPluginLog log, Configuration config)
        {
            Log = log;
            Config = config;
        }

        public static void Info(string method, string text)
        {
            text = $"{method} - {text}";
            InfoLogs.Add(DateTime.Now, new LogMessage() { message = $"{text}", color = Constants.INFOLOGCOLOR });

            if (Config.ShowInfoLog)
                logList.Add(DateTime.Now, new LogMessage() { message = $"{text}", color = Constants.INFOLOGCOLOR });

            Log.Info(text);
        }

        public static void Debug(string method, string text)
        {
            text = $"{method} - {text}";
            DebugLogs.Add(DateTime.Now, new LogMessage() { message = $"{text}", color = Constants.DEBUGLOGCOLOR });

            if (Config.ShowDebugLog)
                logList.Add(DateTime.Now, new LogMessage() { message = $"{text}", color = Constants.DEBUGLOGCOLOR });

            Log.Debug(text);
        }

        public static void Error(string method, string text)
        {
            text = $"{method} - {text}";
            ErrorLogs.Add(DateTime.Now, new LogMessage() { message = $"{text}", color = Constants.ERRORLOGCOLOR });

            if (Config.ShowErrorLog)
                logList.Add(DateTime.Now, new LogMessage() { message = $"{text}", color = Constants.ERRORLOGCOLOR });

            Log.Error(text);
        }

        public static void RecreateLogList()
        {
            List<DateTime> logListKeys = new List<DateTime>();
            logList = new Dictionary<DateTime, LogMessage>();

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
                        ErrorLogs.ContainsKey(key) && Config.ShowErrorLog ? ErrorLogs[key] : new LogMessage() { message = "", color = new System.Numerics.Vector4(0, 0, 0, 0) }));
        }
    }
}
