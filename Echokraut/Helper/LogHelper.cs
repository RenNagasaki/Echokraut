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
        private static SortedDictionary<DateTime, LogMessage> InfoLogs = new SortedDictionary<DateTime, LogMessage>();
        private static SortedDictionary<DateTime, LogMessage> DebugLogs = new SortedDictionary<DateTime, LogMessage>();
        private static SortedDictionary<DateTime, LogMessage> ErrorLogs = new SortedDictionary<DateTime, LogMessage>();
        public static SortedDictionary<DateTime, LogMessage> logList = new SortedDictionary<DateTime, LogMessage>();

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
            logList = new SortedDictionary<DateTime, LogMessage>();

            if (Config.ShowInfoLog)
            {
                var logListKeys = new List<DateTime>(); 
                logListKeys.AddRange(LogHelper.InfoLogs.Keys.ToList());
                logListKeys.ForEach(key => logList.Add(key, InfoLogs[key]));
            }
            if (Config.ShowDebugLog)
            {
                var logListKeys = new List<DateTime>();
                logListKeys.AddRange(LogHelper.DebugLogs.Keys.ToList());
                logListKeys.ForEach(key => logList.Add(key, DebugLogs[key]));
            }
            if (Config.ShowErrorLog)
            {
                var logListKeys = new List<DateTime>();
                logListKeys.AddRange(LogHelper.ErrorLogs.Keys.ToList());
                logListKeys.ForEach(key => logList.Add(key, ErrorLogs[key])); 
            }
        }
    }
}
