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

            Info(method, text, eventId);
        }

        public static void End(string method, EKEventId eventId)
        {
            var text = $"----------------------------End-----------------------------------";

            Info(method, text, eventId);
        }

        public static void Info(string method, string text, EKEventId eventId)
        {
            text = $"{text}";
            SortLogEntry(new LogMessage() { type = LogType.Info, eventId = eventId, method = method, message = $"{text}", color = Constants.INFOLOGCOLOR, timeStamp = DateTime.Now });

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
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowGeneralDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowGeneralErrorLog))
                        GeneralLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogGeneralFilter = true;
                    break;
                case TextSource.Chat:
                    ChatLogs.Add(logMessage);
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowChatDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowChatErrorLog))
                        ChatLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogChatFilter = true;
                    break;
                case TextSource.AddonTalk:
                    TalkLogs.Add(logMessage);
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowTalkDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowTalkErrorLog))
                        TalkLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogTalkFilter = true;
                    break;
                case TextSource.AddonBattleTalk:
                    BattleTalkLogs.Add(logMessage);
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowBattleTalkDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowBattleTalkErrorLog))
                        BattleTalkLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogBattleTalkFilter = true;
                    break;
                case TextSource.AddonSelectString:
                    SelectStringLogs.Add(logMessage);
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowSelectStringDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowSelectStringErrorLog))
                        SelectStringLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogSelectStringFilter = true;
                    break;
                case TextSource.AddonCutsceneSelectString:
                    CutsceneSelectStringLogs.Add(logMessage);
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowCutsceneSelectStringDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowCutsceneSelectStringErrorLog))
                        CutsceneSelectStringLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogCutsceneSelectStringFilter = true;
                    break;
                case TextSource.AddonBubble:
                    BubbleLogs.Add(logMessage);
                    if ((logMessage.type == LogType.Info)
                        || (logMessage.type == LogType.Debug && Config.logConfig.ShowBubbleDebugLog)
                        || (logMessage.type == LogType.Error && Config.logConfig.ShowBubbleErrorLog))
                        BubbleLogsFiltered.Add(logMessage);
                    ConfigWindow.UpdateLogBubblesFilter = true;
                    break;
            }
        }

        public static List<LogMessage> RecreateLogList(TextSource textSource)
        {
            var logListFiltered = new List<LogMessage>();
            var showDebug = false;
            var showError = false;
            var showId0 = false;
            switch (textSource)
            {
                case TextSource.None:
                    GeneralLogsFiltered = new List<LogMessage>(GeneralLogs);
                    logListFiltered = GeneralLogsFiltered;
                    showDebug = Config.logConfig.ShowGeneralDebugLog;
                    showError = Config.logConfig.ShowGeneralErrorLog;
                    showId0 = true;
                    break;
                case TextSource.Chat:
                    ChatLogsFiltered = new List<LogMessage>(ChatLogs);
                    logListFiltered = ChatLogsFiltered;
                    showDebug = Config.logConfig.ShowChatDebugLog;
                    showError = Config.logConfig.ShowChatErrorLog;
                    showId0 = Config.logConfig.ShowChatId0;
                    break;
                case TextSource.AddonTalk:
                    TalkLogsFiltered = new List<LogMessage>(TalkLogs);
                    logListFiltered = TalkLogsFiltered;
                    showDebug = Config.logConfig.ShowTalkDebugLog;
                    showError = Config.logConfig.ShowTalkErrorLog;
                    showId0 = Config.logConfig.ShowTalkId0;
                    break;
                case TextSource.AddonBattleTalk:
                    BattleTalkLogsFiltered = new List<LogMessage>(BattleTalkLogs);
                    logListFiltered = BattleTalkLogsFiltered;
                    showDebug = Config.logConfig.ShowBattleTalkDebugLog;
                    showError = Config.logConfig.ShowBattleTalkErrorLog;
                    showId0 = Config.logConfig.ShowBattleTalkId0;
                    break;
                case TextSource.AddonSelectString:
                    SelectStringLogsFiltered = new List<LogMessage>(SelectStringLogs);
                    logListFiltered = SelectStringLogsFiltered;
                    showDebug = Config.logConfig.ShowSelectStringDebugLog;
                    showError = Config.logConfig.ShowSelectStringErrorLog;
                    showId0 = Config.logConfig.ShowSelectStringId0;
                    break;
                case TextSource.AddonCutsceneSelectString:
                    CutsceneSelectStringLogsFiltered = new List<LogMessage>(CutsceneSelectStringLogs);
                    logListFiltered = CutsceneSelectStringLogsFiltered;
                    showDebug = Config.logConfig.ShowCutsceneSelectStringDebugLog;
                    showError = Config.logConfig.ShowCutsceneSelectStringErrorLog;
                    showId0 = Config.logConfig.ShowCutSceneSelectStringId0;
                    break;
                case TextSource.AddonBubble:
                    BubbleLogsFiltered = new List<LogMessage>(BubbleLogs);
                    logListFiltered = BubbleLogsFiltered;
                    showDebug = Config.logConfig.ShowBubbleDebugLog;
                    showError = Config.logConfig.ShowBubbleErrorLog;
                    showId0 = Config.logConfig.ShowBubbleId0;
                    break;
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
