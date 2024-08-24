using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper.Data
{
    public static class LogHelper
    {
        private static IPluginLog Log;
        private static Configuration Config;
        private static List<LogMessage> GeneralLogs = new List<LogMessage>();
        public static List<LogMessage> GeneralLogsFiltered = new List<LogMessage>();
        private static List<LogMessage> ChatLogs = new List<LogMessage>();
        public static List<LogMessage> ChatLogsFiltered = new List<LogMessage>();
        private static List<LogMessage> TalkLogs = new List<LogMessage>();
        public static List<LogMessage> TalkLogsFiltered = new List<LogMessage>();
        private static List<LogMessage> BattleTalkLogs = new List<LogMessage>();
        public static List<LogMessage> BattleTalkLogsFiltered = new List<LogMessage>();
        private static List<LogMessage> BubbleLogs = new List<LogMessage>();
        public static List<LogMessage> BubbleLogsFiltered = new List<LogMessage>();
        private static List<LogMessage> CutsceneSelectStringLogs = new List<LogMessage>();
        public static List<LogMessage> CutsceneSelectStringLogsFiltered = new List<LogMessage>();
        private static List<LogMessage> SelectStringLogs = new List<LogMessage>();
        public static List<LogMessage> SelectStringLogsFiltered = new List<LogMessage>();

        public static void Setup(IPluginLog log, Configuration config)
        {
            Log = log;
            Config = config;
        }

        public static void Start(string method, EKEventId eventId)
        {
            var text = $"---------------------------Start----------------------------------";

            Important(method, text, eventId);
        }

        public static void End(string method, EKEventId eventId)
        {
            var text = $"----------------------------End-----------------------------------";

            Important(method, text, eventId);
        }

        public static void Info(string method, string text, EKEventId eventId)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Info, eventId = eventId, method = method, message = $"{text}", color = Constants.INFOLOGCOLOR, timeStamp = DateTime.Now });

            Log.Info($"{method} - {text} - ID: {eventId.Id}");
        }

        public static void Important(string method, string text, EKEventId eventId)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Important, eventId = eventId, method = method, message = $"{text}", color = Constants.IMPORTANTLOGCOLOR, timeStamp = DateTime.Now });

            Log.Info($"{method} - {text} - ID: {eventId.Id}");
        }

        public static void Debug(string method, string text, EKEventId eventId)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Debug, eventId = eventId, method = method, message = $"{text}", color = Constants.DEBUGLOGCOLOR, timeStamp = DateTime.Now });

            Log.Debug($"{method} - {text} - ID: {eventId.Id}");
        }

        public static void Error(string method, string text, EKEventId eventId, bool internalLog = true)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Error, eventId = eventId, method = method, message = $"{text}", color = Constants.ERRORLOGCOLOR, timeStamp = DateTime.Now });

            Log.Error($"{method} - {text} - ID: {eventId.Id}");
        }

        private static void SortLogEntry(LogMessage logMessage)
        {
            switch (logMessage.eventId.textSource)
            {
                case TextSource.None:
                    GeneralLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowGeneralInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowGeneralDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowGeneralErrorLog
                        || logMessage.type == LogType.Important)
                        GeneralLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogGeneralFilter = true;
                    break;
                case TextSource.Chat:
                    ChatLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowChatInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowChatDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowChatErrorLog
                        || logMessage.type == LogType.Important)
                        ChatLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogChatFilter = true;
                    break;
                case TextSource.AddonTalk:
                    TalkLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowTalkInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowTalkDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowTalkErrorLog
                        || logMessage.type == LogType.Important)
                        TalkLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogTalkFilter = true;
                    break;
                case TextSource.AddonBattleTalk:
                    BattleTalkLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowBattleTalkInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowBattleTalkDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowBattleTalkErrorLog
                        || logMessage.type == LogType.Important)
                        BattleTalkLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogBattleTalkFilter = true;
                    break;
                case TextSource.AddonSelectString:
                    SelectStringLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowSelectStringInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowSelectStringDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowSelectStringErrorLog
                        || logMessage.type == LogType.Important)
                        SelectStringLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogSelectStringFilter = true;
                    break;
                case TextSource.AddonCutsceneSelectString:
                    CutsceneSelectStringLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowCutsceneSelectStringInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowCutsceneSelectStringDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowCutsceneSelectStringErrorLog
                        || logMessage.type == LogType.Important)
                        CutsceneSelectStringLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogCutsceneSelectStringFilter = true;
                    break;
                case TextSource.AddonBubble:
                    BubbleLogs.Add(logMessage);
                    if (logMessage.type == LogType.Info && Config.logConfig.ShowBubbleInfoLog
                        || logMessage.type == LogType.Debug && Config.logConfig.ShowBubbleDebugLog
                        || logMessage.type == LogType.Error && Config.logConfig.ShowBubbleErrorLog
                        || logMessage.type == LogType.Important)
                        BubbleLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogBubblesFilter = true;
                    break;
            }
        }

        public static List<LogMessage> RecreateLogList(TextSource textSource)
        {
            var logListFiltered = new List<LogMessage>();
            var showInfo = false;
            var showDebug = false;
            var showError = false;
            var showId0 = false;
            switch (textSource)
            {
                case TextSource.None:
                    GeneralLogsFiltered = new List<LogMessage>(GeneralLogs);
                    logListFiltered = GeneralLogsFiltered;
                    showInfo = Config.logConfig.ShowGeneralInfoLog;
                    showDebug = Config.logConfig.ShowGeneralDebugLog;
                    showError = Config.logConfig.ShowGeneralErrorLog;
                    showId0 = true;
                    break;
                case TextSource.Chat:
                    ChatLogsFiltered = new List<LogMessage>(ChatLogs);
                    logListFiltered = ChatLogsFiltered;
                    showInfo = Config.logConfig.ShowChatInfoLog;
                    showDebug = Config.logConfig.ShowChatDebugLog;
                    showError = Config.logConfig.ShowChatErrorLog;
                    showId0 = Config.logConfig.ShowChatId0;
                    break;
                case TextSource.AddonTalk:
                    TalkLogsFiltered = new List<LogMessage>(TalkLogs);
                    logListFiltered = TalkLogsFiltered;
                    showInfo = Config.logConfig.ShowTalkInfoLog;
                    showDebug = Config.logConfig.ShowTalkDebugLog;
                    showError = Config.logConfig.ShowTalkErrorLog;
                    showId0 = Config.logConfig.ShowTalkId0;
                    break;
                case TextSource.AddonBattleTalk:
                    BattleTalkLogsFiltered = new List<LogMessage>(BattleTalkLogs);
                    logListFiltered = BattleTalkLogsFiltered;
                    showInfo = Config.logConfig.ShowBattleTalkInfoLog;
                    showDebug = Config.logConfig.ShowBattleTalkDebugLog;
                    showError = Config.logConfig.ShowBattleTalkErrorLog;
                    showId0 = Config.logConfig.ShowBattleTalkId0;
                    break;
                case TextSource.AddonSelectString:
                    SelectStringLogsFiltered = new List<LogMessage>(SelectStringLogs);
                    logListFiltered = SelectStringLogsFiltered;
                    showInfo = Config.logConfig.ShowSelectStringInfoLog;
                    showDebug = Config.logConfig.ShowSelectStringDebugLog;
                    showError = Config.logConfig.ShowSelectStringErrorLog;
                    showId0 = Config.logConfig.ShowSelectStringId0;
                    break;
                case TextSource.AddonCutsceneSelectString:
                    CutsceneSelectStringLogsFiltered = new List<LogMessage>(CutsceneSelectStringLogs);
                    logListFiltered = CutsceneSelectStringLogsFiltered;
                    showInfo = Config.logConfig.ShowCutsceneSelectStringInfoLog;
                    showDebug = Config.logConfig.ShowCutsceneSelectStringDebugLog;
                    showError = Config.logConfig.ShowCutsceneSelectStringErrorLog;
                    showId0 = Config.logConfig.ShowCutSceneSelectStringId0;
                    break;
                case TextSource.AddonBubble:
                    BubbleLogsFiltered = new List<LogMessage>(BubbleLogs);
                    logListFiltered = BubbleLogsFiltered;
                    showInfo = Config.logConfig.ShowBubbleInfoLog;
                    showDebug = Config.logConfig.ShowBubbleDebugLog;
                    showError = Config.logConfig.ShowBubbleErrorLog;
                    showId0 = Config.logConfig.ShowBubbleId0;
                    break;
            }

            if (!showInfo)
            {
                logListFiltered.RemoveAll(p => p.type == LogType.Info);
            }
            if (!showDebug)
            {
                logListFiltered.RemoveAll(p => p.type == LogType.Debug);
            }
            if (!showError)
            {
                logListFiltered.RemoveAll(p => p.type == LogType.Error);
            }
            if (!showId0)
            {
                logListFiltered.RemoveAll(p => p.eventId.Id == 0);
            }

            logListFiltered.Sort((p, q) => p.timeStamp.CompareTo(q.timeStamp));

            return new List<LogMessage>(logListFiltered);
        }
    }
}
