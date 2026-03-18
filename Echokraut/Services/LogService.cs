using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Echokraut.Services;

public class LogService : ILogService
{
    private readonly IPluginLog _log;
    private readonly ConcurrentBag<LogMessage> _generalLogs = new();
    private readonly ConcurrentBag<LogMessage> _chatLogs = new();
    private readonly ConcurrentBag<LogMessage> _talkLogs = new();
    private readonly ConcurrentBag<LogMessage> _battleTalkLogs = new();
    private readonly ConcurrentBag<LogMessage> _bubbleLogs = new();
    private readonly ConcurrentBag<LogMessage> _cutsceneSelectStringLogs = new();
    private readonly ConcurrentBag<LogMessage> _selectStringLogs = new();
    private readonly ConcurrentBag<LogMessage> _backendLogs = new();
    
    public bool Updating { get; private set; }

    public event Action<TextSource>? LogUpdated;
    
    public List<LogMessage> GeneralLogsMainThread { get; } = new();
    public List<LogMessage> ChatLogsMainThread { get; } = new();
    public List<LogMessage> TalkLogsMainThread { get; } = new();
    public List<LogMessage> BattleTalkLogsMainThread { get; } = new();
    public List<LogMessage> BubbleLogsMainThread { get; } = new();
    public List<LogMessage> CutsceneSelectStringLogsMainThread { get; } = new();
    public List<LogMessage> SelectStringLogsMainThread { get; } = new();
    public List<LogMessage> BackendLogsMainThread { get; } = new();

    public LogService(IPluginLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public EKEventId Start(string method, TextSource source)
    {
        var text = "---------------------------Start----------------------------------";
        var eventId = new EKEventId(EKEventId.CurrentId++, source);
        Info(method, text, eventId);
        return eventId;
    }

    public void End(string method, EKEventId eventId)
    {
        var text = "---------------------------End------------------------------------";
        Info(method, text, eventId);
    }

    public void Info(string method, string message, EKEventId eventId)
    {
        Log(LogType.Info, method, message, eventId);
    }

    public void Debug(string method, string message, EKEventId eventId)
    {
        Log(LogType.Debug, method, message, eventId);
    }

    public void Error(string method, string message, EKEventId eventId)
    {
        Log(LogType.Error, method, message, eventId);
    }

    public void Warning(string method, string message, EKEventId eventId)
    {
        Log(LogType.Warning, method, message, eventId);
    }

    private void Log(LogType logType, string method, string message, EKEventId eventId)
    {
        var logMessage = new LogMessage
        {
            type = logType,
            method = method,
            message = message,
            eventId = eventId,
            timeStamp = DateTime.Now,
            color = logType switch
            {
                LogType.Debug   => Constants.DEBUGLOGCOLOR,
                LogType.Error   => Constants.ERRORLOGCOLOR,
                LogType.Warning => Constants.ERRORLOGCOLOR,
                _               => Vector4.Zero
            }
        };

        var bag = GetBagForSource(eventId.textSource);
        bag.Add(logMessage);

        // Also log to Dalamud's logger
        var formattedMessage = $"[{eventId.Id}:{eventId.textSource}] {method}: {message}";
        switch (logType)
        {
            case LogType.Debug:
                _log.Debug(formattedMessage);
                break;
            case LogType.Info:
                _log.Info(formattedMessage);
                break;
            case LogType.Warning:
                _log.Warning(formattedMessage);
                break;
            case LogType.Error:
                _log.Error(formattedMessage);
                break;
        }

        SetUpdateFlag(eventId.textSource);
    }

    private void SetUpdateFlag(TextSource source)
    {
        LogUpdated?.Invoke(source);
    }

    private ConcurrentBag<LogMessage> GetBagForSource(TextSource source)
    {
        return source switch
        {
            TextSource.Chat => _chatLogs,
            TextSource.AddonTalk => _talkLogs,
            TextSource.AddonBattleTalk => _battleTalkLogs,
            TextSource.AddonBubble => _bubbleLogs,
            TextSource.AddonCutsceneSelectString => _cutsceneSelectStringLogs,
            TextSource.AddonSelectString => _selectStringLogs,
            TextSource.Backend => _backendLogs,
            _ => _generalLogs
        };
    }

    public void UpdateMainThreadLogs()
    {
        Updating = true;
        UpdateList(_generalLogs, GeneralLogsMainThread);
        UpdateList(_chatLogs, ChatLogsMainThread);
        UpdateList(_talkLogs, TalkLogsMainThread);
        UpdateList(_battleTalkLogs, BattleTalkLogsMainThread);
        UpdateList(_bubbleLogs, BubbleLogsMainThread);
        UpdateList(_cutsceneSelectStringLogs, CutsceneSelectStringLogsMainThread);
        UpdateList(_selectStringLogs, SelectStringLogsMainThread);
        UpdateList(_backendLogs, BackendLogsMainThread);
        Updating = false;
    }

    private void UpdateList(ConcurrentBag<LogMessage> bag, List<LogMessage> list)
    {
        while (bag.TryTake(out var message))
        {
            list.Add(message);
        }
    }

    public void ClearLogs(TextSource source)
    {
        GetLogsForSource(source).Clear();
    }

    public List<LogMessage> GetLogsForSource(TextSource source)
    {
        return source switch
        {
            TextSource.Chat => ChatLogsMainThread,
            TextSource.AddonTalk => TalkLogsMainThread,
            TextSource.AddonBattleTalk => BattleTalkLogsMainThread,
            TextSource.AddonBubble => BubbleLogsMainThread,
            TextSource.AddonCutsceneSelectString => CutsceneSelectStringLogsMainThread,
            TextSource.AddonSelectString => SelectStringLogsMainThread,
            TextSource.Backend => BackendLogsMainThread,
            _ => GeneralLogsMainThread
        };
    }
}
