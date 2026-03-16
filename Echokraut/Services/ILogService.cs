using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Collections.Generic;

namespace Echokraut.Services;

public interface ILogService
{
    bool Updating { get; }

    /// <summary>Fired (on any thread) whenever a new log entry is added for a given TextSource.</summary>
    event Action<TextSource>? LogUpdated;

    EKEventId Start(string method, TextSource source);
    void End(string method, EKEventId eventId);
    void Info(string method, string message, EKEventId eventId);
    void Debug(string method, string message, EKEventId eventId);
    void Error(string method, string message, EKEventId eventId);
    void Warning(string method, string message, EKEventId eventId);

    void UpdateMainThreadLogs();
    void ClearLogs(TextSource source);
    List<LogMessage> GetLogsForSource(TextSource source);
}
