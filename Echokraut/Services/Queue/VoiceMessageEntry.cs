using Echokraut.DataClasses;
using System;

namespace Echokraut.Services.Queue;

/// <summary>
/// Wraps a VoiceMessage with state tracking and metadata
/// </summary>
public class VoiceMessageEntry
{
    public VoiceMessage Message { get; }
    public VoiceMessageState State { get; set; }
    public DateTime QueuedAt { get; }
    public DateTime? StartedGeneratingAt { get; set; }
    public DateTime? StartedPlayingAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Exception? Error { get; set; }
    public Guid Id { get; }

    private readonly TimeProvider _time;

    /// <param name="time">Clock for the timestamps; the system clock unless a test steers it.</param>
    public VoiceMessageEntry(VoiceMessage message, TimeProvider? time = null)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        _time = time ?? TimeProvider.System;
        State = VoiceMessageState.PendingGeneration;
        QueuedAt = UtcNow;
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// True once the entry reached a terminal state. Terminal entries never transition again —
    /// a cancelled message must stay cancelled even if its (already running) generation finishes
    /// afterwards, otherwise it gets re-queued and plays long after the dialog was closed.
    /// </summary>
    public bool IsTerminal => State is VoiceMessageState.Completed
                                    or VoiceMessageState.Cancelled
                                    or VoiceMessageState.Failed;

    /// <summary>
    /// Moves the entry to <paramref name="newState"/>. Returns false (and changes nothing) when
    /// the entry is already terminal — see <see cref="IsTerminal"/>.
    /// </summary>
    public bool TransitionTo(VoiceMessageState newState)
    {
        if (IsTerminal) return false;

        State = newState;

        switch (newState)
        {
            case VoiceMessageState.Generating:
                StartedGeneratingAt = UtcNow;
                break;
            case VoiceMessageState.Playing:
                StartedPlayingAt = UtcNow;
                break;
            case VoiceMessageState.Completed:
            case VoiceMessageState.Cancelled:
            case VoiceMessageState.Failed:
                CompletedAt = UtcNow;
                break;
        }

        return true;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public TimeSpan? GetTotalProcessingTime()
    {
        if (CompletedAt.HasValue)
            return CompletedAt.Value - QueuedAt;
        return null;
    }

    public override string ToString()
    {
        return $"[{Id:N}] {Message.Speaker.Name}: {Message.Text.Substring(0, Math.Min(30, Message.Text.Length))}... ({State})";
    }
}
