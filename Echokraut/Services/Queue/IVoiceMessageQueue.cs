using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.Queue;

/// <summary>
/// Thread-safe queue for managing voice message lifecycle
/// </summary>
public interface IVoiceMessageQueue : IDisposable
{
    /// <summary>
    /// Enqueue a new voice message for processing
    /// </summary>
    void Enqueue(VoiceMessage message, bool isPriority = false);
    
    /// <summary>
    /// Try to dequeue the next message pending generation
    /// </summary>
    bool TryDequeuePendingGeneration(out VoiceMessageEntry? entry);
    
    /// <summary>
    /// Try to dequeue the next message ready to play
    /// </summary>
    bool TryDequeueReadyToPlay(out VoiceMessageEntry? entry);
    
    /// <summary>
    /// Mark a message as generating
    /// </summary>
    void MarkAsGenerating(Guid entryId);
    
    /// <summary>
    /// Mark a message as ready to play
    /// </summary>
    void MarkAsReadyToPlay(Guid entryId);
    
    /// <summary>
    /// Mark a message as playing
    /// </summary>
    void MarkAsPlaying(Guid entryId);
    
    /// <summary>
    /// Mark a message as paused
    /// </summary>
    void MarkAsPaused(Guid entryId);
    
    /// <summary>
    /// Mark a message as completed
    /// </summary>
    void MarkAsCompleted(Guid entryId);
    
    /// <summary>
    /// Mark a message as cancelled
    /// </summary>
    void MarkAsCancelled(Guid entryId);
    
    /// <summary>
    /// Mark a message as failed
    /// </summary>
    void MarkAsFailed(Guid entryId, Exception error);
    
    /// <summary>
    /// Cancel all messages from a specific source
    /// </summary>
    void CancelBySource(TextSource source);
    
    /// <summary>
    /// Cancel all messages
    /// </summary>
    void CancelAll();
    
    /// <summary>
    /// Get an entry by its ID
    /// </summary>
    VoiceMessageEntry? GetEntry(Guid entryId);
    
    /// <summary>
    /// Get currently playing message
    /// </summary>
    VoiceMessageEntry? GetCurrentlyPlaying();
    
    /// <summary>
    /// Get all entries in a specific state
    /// </summary>
    IReadOnlyList<VoiceMessageEntry> GetEntriesByState(VoiceMessageState state);
    
    /// <summary>
    /// Get queue statistics
    /// </summary>
    QueueStatistics GetStatistics();
}

/// <summary>
/// Queue statistics for monitoring
/// </summary>
public class QueueStatistics
{
    public int PendingGeneration { get; set; }
    public int Generating { get; set; }
    public int ReadyToPlay { get; set; }
    public int Playing { get; set; }
    public int Paused { get; set; }
    public int TotalCompleted { get; set; }
    public int TotalCancelled { get; set; }
    public int TotalFailed { get; set; }
}
