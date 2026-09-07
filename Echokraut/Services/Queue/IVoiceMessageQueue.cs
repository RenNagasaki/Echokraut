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
    /// Raised whenever messages of a source were cancelled (advance, dialog close, /ek cancel).
    /// Lets the backend abort the generation request that is actually in flight — cancelling the
    /// queue entry alone still leaves the engine busy producing audio nobody will hear.
    /// </summary>
    event Action<TextSource>? SourceCancelled;

    /// <summary>
    /// Current cancellation epoch for a source. Every cancellation bumps it. A message stamped
    /// with an older epoch belongs to a line the player has already moved past.
    /// </summary>
    long GetEpoch(TextSource source);

    /// <summary>
    /// True if this message was created before the last cancellation of its source, i.e. it is a
    /// leftover from a line the player skipped. Closes the race where the producer is still
    /// running (ProcessSpeechAsync is async) while the cancellation happens, so the message is
    /// enqueued *after* the flush and would otherwise survive it.
    /// </summary>
    bool IsObsolete(VoiceMessage message);


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
