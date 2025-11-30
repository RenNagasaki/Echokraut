using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Windows;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper.Data
{
    public static class LogHelper
    {
        public static bool Updating {get; private set;}
        private static IPluginLog Log;
        private static ConcurrentBag<LogMessage> GeneralLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> ChatLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> TalkLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> BattleTalkLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> BubbleLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> CutsceneSelectStringLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> SelectStringLogs = new ConcurrentBag<LogMessage>();
        private static ConcurrentBag<LogMessage> BackendLogs = new ConcurrentBag<LogMessage>();
        private static List<LogMessage> GeneralLogsMainThread = new List<LogMessage>();
        private static List<LogMessage> ChatLogsMainThread = new List<LogMessage>();
        private static List<LogMessage> TalkLogsMainThread = new List<LogMessage>(); 
        private static List<LogMessage> BattleTalkLogsMainThread = new List<LogMessage>();
        private static List<LogMessage> BubbleLogsMainThread = new List<LogMessage>();
        private static List<LogMessage> CutsceneSelectStringLogsMainThread = new List<LogMessage>();
        private static List<LogMessage> SelectStringLogsMainThread = new List<LogMessage>();
        private static List<LogMessage> BackendLogsMainThread = new List<LogMessage>();

        public static void Initialize(IPluginLog log)
        {
            Log = log;
        }

        public static EKEventId Start(string method, TextSource source)
        {
            var text = $"---------------------------Start----------------------------------";

            EKEventId eventId = new EKEventId(EKEventId.CurrentId++, source);
            Info(method, text, eventId);

            return eventId;
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

        public static void Error(string method, Exception e, EKEventId eventId, bool internalLog = true)
        {
            var text = $"Error: {e.Message}\r\nStacktrace: {e.StackTrace}";
            SortLogEntry(new LogMessage() { type = LogType.Error, eventId = eventId, method = method, message = $"{text}", color = Constants.ERRORLOGCOLOR, timeStamp = DateTime.Now });

            Log.Error($"{method} - {text} - ID: {eventId.Id}");
        }

        private static void SortLogEntry(LogMessage logMessage)
        {
            switch (logMessage.eventId.textSource)
            {
                case TextSource.None:
                    GeneralLogs.Add(logMessage);
                    ConfigWindow.UpdateLogGeneralFilter = true;
                    break;
                case TextSource.Chat:
                    ChatLogs.Add(logMessage);
                    ConfigWindow.UpdateLogChatFilter = true;
                    break;
                case TextSource.AddonTalk:
                    TalkLogs.Add(logMessage);
                    ConfigWindow.UpdateLogTalkFilter = true;
                    break;
                case TextSource.AddonBattleTalk:
                    BattleTalkLogs.Add(logMessage);
                    ConfigWindow.UpdateLogBattleTalkFilter = true;
                    break;
                case TextSource.AddonSelectString:
                    SelectStringLogs.Add(logMessage);
                    ConfigWindow.UpdateLogSelectStringFilter = true;
                    break;
                case TextSource.AddonCutsceneSelectString:
                    CutsceneSelectStringLogs.Add(logMessage);
                    ConfigWindow.UpdateLogCutsceneSelectStringFilter = true;
                    break;
                case TextSource.AddonBubble:
                    BubbleLogs.Add(logMessage);
                    ConfigWindow.UpdateLogBubblesFilter = true;
                    break;
                case TextSource.Backend:
                    BackendLogs.Add(logMessage);
                    ConfigWindow.UpdateLogBackendFilter = true;
                    break;
            }
        }

        public static void UpdateLogList()
        {
            MakeLogsThreadSafe(GeneralLogs, ref GeneralLogsMainThread);
            MakeLogsThreadSafe(ChatLogs, ref ChatLogsMainThread);
            MakeLogsThreadSafe(TalkLogs, ref TalkLogsMainThread);
            MakeLogsThreadSafe(BattleTalkLogs, ref BattleTalkLogsMainThread);
            MakeLogsThreadSafe(SelectStringLogs, ref SelectStringLogsMainThread);
            MakeLogsThreadSafe(CutsceneSelectStringLogs, ref CutsceneSelectStringLogsMainThread);
            MakeLogsThreadSafe(BubbleLogs, ref BubbleLogsMainThread);
            MakeLogsThreadSafe(BackendLogs, ref BackendLogsMainThread);
        }

        public static List<LogMessage> RecreateLogList(TextSource textSource)
        {
            Updating = true;
            var logListFiltered = new List<LogMessage>();
            var showDebug = false;
            var showError = false;
            var showId0 = true;
            switch (textSource)
            {
                case TextSource.None:
                    logListFiltered = new List<LogMessage>(GeneralLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowGeneralDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowGeneralErrorLog;
                    showId0 = true;
                    break;
                case TextSource.Chat:
                    logListFiltered = new List<LogMessage>(ChatLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowChatDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowChatErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowChatId0;
                    break;
                case TextSource.AddonTalk:
                    logListFiltered = new List<LogMessage>(TalkLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowTalkDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowTalkErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowTalkId0;
                    break;
                case TextSource.AddonBattleTalk:
                    logListFiltered = new List<LogMessage>(BattleTalkLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowBattleTalkDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowBattleTalkErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowBattleTalkId0;
                    break;
                case TextSource.AddonSelectString:
                    logListFiltered = new List<LogMessage>(SelectStringLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowSelectStringDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowSelectStringErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowSelectStringId0;
                    break;
                case TextSource.AddonCutsceneSelectString:
                    logListFiltered = new List<LogMessage>(CutsceneSelectStringLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowCutsceneSelectStringDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowCutsceneSelectStringErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowCutsceneSelectStringId0;
                    break;
                case TextSource.AddonBubble:
                    logListFiltered = new List<LogMessage>(BubbleLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowBubbleDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowBubbleErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowBubbleId0;
                    break;
                case TextSource.Backend:
                    logListFiltered = new List<LogMessage>(BackendLogsMainThread);
                    showDebug = Plugin.Configuration.logConfig.ShowBackendDebugLog;
                    showError = Plugin.Configuration.logConfig.ShowBackendErrorLog;
                    showId0 = Plugin.Configuration.logConfig.ShowBackendId0;
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
            Updating = false;

            return logListFiltered;
        }

        private static void MakeLogsThreadSafe(ConcurrentBag<LogMessage> logs, ref List<LogMessage> logsMainThread)
        {
            while (logs.Count > 0)
            {
                if (logs.TryTake(out LogMessage? logMessage))
                {
                    logsMainThread.Add(logMessage);
                }
            }
        }
    }
}
