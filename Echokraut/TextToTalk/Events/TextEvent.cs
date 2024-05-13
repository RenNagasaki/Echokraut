using Echokraut.TextToTalk;
using System;

namespace Echokraut.TextToTalk.Events;

public abstract class TextEvent
{
    public TextEventLogEntry ToLogEntry()
    {
        return new TextEventLogEntry
        {
            Event = this,
            Timestamp = DateTime.UtcNow,
        };
    }
}
