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
        private static List<LogMessage> InfoLogs = new List<LogMessage>();
        private static List<LogMessage> DebugLogs = new List<LogMessage>();
        private static List<LogMessage> ErrorLogs = new List<LogMessage>();
        public static List<LogMessage> logList = new List<LogMessage>();

        public static void Setup(IPluginLog log, Configuration config)
        {
            Log = log;
            Config = config;
        }

        public static void Info(string method, string text)
        {
            text = $"{method} - {text}";
            InfoLogs.Add(new LogMessage() { message = $"{text}", color = Constants.INFOLOGCOLOR, timeStamp = DateTime.Now });

            if (Config.ShowInfoLog)
                logList.Add(new LogMessage() { message = $"{text}", color = Constants.INFOLOGCOLOR, timeStamp = DateTime.Now });

            Log.Info(text);
        }

        public static void Debug(string method, string text)
        {
            text = $"{method} - {text}";
            DebugLogs.Add(new LogMessage() { message = $"{text}", color = Constants.DEBUGLOGCOLOR, timeStamp = DateTime.Now });

            if (Config.ShowDebugLog)
                logList.Add(new LogMessage() { message = $"{text}", color = Constants.DEBUGLOGCOLOR, timeStamp = DateTime.Now });

            Log.Debug(text);
        }

        public static void Error(string method, string text)
        {
            text = $"{method} - {text}";
            ErrorLogs.Add(new LogMessage() { message = $"{text}", color = Constants.ERRORLOGCOLOR, timeStamp = DateTime.Now });

            if (Config.ShowErrorLog)
                logList.Add(new LogMessage() { message = $"{text}", color = Constants.ERRORLOGCOLOR, timeStamp = DateTime.Now });

            Log.Error(text);
        }

        public static void RecreateLogList()
        {
            logList = new List<LogMessage>();

            if (Config.ShowInfoLog)
            {
                logList.AddRange(LogHelper.InfoLogs.ToList());
            }
            if (Config.ShowDebugLog)
            {
                logList.AddRange(LogHelper.DebugLogs.ToList());
            }
            if (Config.ShowErrorLog)
            {
                logList.AddRange(LogHelper.ErrorLogs.ToList());
            }

            logList.Sort((p, q) => p.timeStamp.CompareTo(q.timeStamp));
        }
    }
}
