using System;

namespace Echokraut.TextToTalk;

public class TextEventLogEntry
{
    public Guid Id { get; init; }

    public required DateTime Timestamp { get; init; }

    public required object Event { get; init; }
}
