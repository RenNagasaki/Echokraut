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

    public VoiceMessageEntry(VoiceMessage message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        State = VoiceMessageState.PendingGeneration;
        QueuedAt = DateTime.UtcNow;
        Id = Guid.NewGuid();
    }

    public void TransitionTo(VoiceMessageState newState)
    {
        State = newState;
        
        switch (newState)
        {
            case VoiceMessageState.Generating:
                StartedGeneratingAt = DateTime.UtcNow;
                break;
            case VoiceMessageState.Playing:
                StartedPlayingAt = DateTime.UtcNow;
                break;
            case VoiceMessageState.Completed:
            case VoiceMessageState.Cancelled:
            case VoiceMessageState.Failed:
                CompletedAt = DateTime.UtcNow;
                break;
        }
    }

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
