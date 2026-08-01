using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Services.Queue;

/// <summary>
/// Thread-safe implementation of voice message queue using concurrent collections
/// </summary>
public class VoiceMessageQueue : IVoiceMessageQueue
{
    // Priority queues - dialogue gets priority over bubbles
    private readonly ConcurrentQueue<VoiceMessageEntry> _priorityPendingQueue = new();
    private readonly ConcurrentQueue<VoiceMessageEntry> _normalPendingQueue = new();
    private readonly ConcurrentQueue<VoiceMessageEntry> _readyToPlayQueue = new();
    
    // State tracking
    private readonly ConcurrentDictionary<Guid, VoiceMessageEntry> _allEntries = new();
    private readonly ConcurrentDictionary<Guid, VoiceMessageEntry> _generatingEntries = new();
    private VoiceMessageEntry? _currentlyPlaying;
    private readonly object _playingLock = new();
    
    // Cancellation epoch per source — bumped on every CancelBySource/CancelAll. See IsObsolete.
    private readonly ConcurrentDictionary<TextSource, long> _epochs = new();

    /// <summary>
    /// How long a finished entry stays in <c>_allEntries</c> before it is pruned. It has to
    /// outlive the callers that still look the entry up right after it went terminal
    /// (<c>GetEntry</c>, <c>GetEntriesByState</c>, the UI's statistics poll), but nothing reads an
    /// entry minutes later. Settable for tests only.
    /// </summary>
    internal TimeSpan TerminalRetention { get; set; } = TimeSpan.FromSeconds(30);

    public event Action<TextSource>? SourceCancelled;

    // Statistics
    private int _totalCompleted;
    private int _totalCancelled;
    private int _totalFailed;

    public void Enqueue(VoiceMessage message, bool isPriority = false)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        
        var entry = new VoiceMessageEntry(message);
        _allEntries[entry.Id] = entry;

        if (isPriority)
            _priorityPendingQueue.Enqueue(entry);
        else
            _normalPendingQueue.Enqueue(entry);

        // Amortised cleanup — no timer needed, the queue only grows when something is enqueued.
        PruneTerminal();
    }

    /// <summary>
    /// Drops finished entries older than <see cref="TerminalRetention"/> from
    /// <c>_allEntries</c>. Without this the dictionary grows for the whole play session: every
    /// line ever spoken stays resident, and <c>CancelAll</c>/<c>CancelBySource</c>/
    /// <c>GetStatistics</c> walk the full history on every dialogue advance.
    /// </summary>
    private void PruneTerminal()
    {
        var cutoff = DateTime.UtcNow - TerminalRetention;
        foreach (var entry in _allEntries.Values)
        {
            if (entry.IsTerminal && entry.CompletedAt.HasValue && entry.CompletedAt.Value <= cutoff)
                _allEntries.TryRemove(entry.Id, out _);
        }
    }

    /// <summary>
    /// Runs once per entry, right after it reached a terminal state. Releases the references the
    /// entry holds on the game world — an entry lingers for <see cref="TerminalRetention"/>
    /// afterwards and must not keep an <c>IGameObject</c> alive for that long.
    /// </summary>
    private static void ReleaseGameReferences(VoiceMessageEntry entry)
    {
        // Only the game-object handles: Speaker (NpcMapData) and Stream stay, the playback path
        // still reads them while it finishes up (OnSourceEnded saves the audio off the stream).
        entry.Message.SpeakerObj = null;
        entry.Message.SpeakerFollowObj = null;
    }

    public bool TryDequeuePendingGeneration(out VoiceMessageEntry? entry)
    {
        // Priority queue first
        if (_priorityPendingQueue.TryDequeue(out entry))
            return true;
        
        // Then normal queue
        if (_normalPendingQueue.TryDequeue(out entry))
            return true;
        
        entry = null;
        return false;
    }

    public bool TryDequeueReadyToPlay(out VoiceMessageEntry? entry)
    {
        if (_readyToPlayQueue.TryDequeue(out entry))
            return true;
        
        entry = null;
        return false;
    }

    public void MarkAsGenerating(Guid entryId)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            // A cancelled entry must not be pulled back into the pipeline — TransitionTo
            // refuses terminal→non-terminal moves and we honour that here.
            if (!entry.TransitionTo(VoiceMessageState.Generating)) return;
            _generatingEntries[entryId] = entry;
        }
    }

    public void MarkAsReadyToPlay(Guid entryId)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            // Cancelled while generating (dialog closed, queue flushed): drop it instead of
            // enqueuing for playback, otherwise the finished clip plays after the fact.
            if (!entry.TransitionTo(VoiceMessageState.ReadyToPlay))
            {
                _generatingEntries.TryRemove(entryId, out _);
                return;
            }
            _generatingEntries.TryRemove(entryId, out _);
            _readyToPlayQueue.Enqueue(entry);
        }
    }

    public void MarkAsPlaying(Guid entryId)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            if (!entry.TransitionTo(VoiceMessageState.Playing)) return;
            lock (_playingLock)
            {
                _currentlyPlaying = entry;
            }
        }
    }

    public void MarkAsPaused(Guid entryId)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            entry.TransitionTo(VoiceMessageState.Paused);
        }
    }

    public void MarkAsCompleted(Guid entryId)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            if (!entry.TransitionTo(VoiceMessageState.Completed)) return;
            ReleaseGameReferences(entry);
            System.Threading.Interlocked.Increment(ref _totalCompleted);
            lock (_playingLock)
            {
                if (_currentlyPlaying?.Id == entryId)
                    _currentlyPlaying = null;
            }
            _generatingEntries.TryRemove(entryId, out _);
        }
    }

    public void MarkAsCancelled(Guid entryId)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            if (!entry.TransitionTo(VoiceMessageState.Cancelled)) return;
            ReleaseGameReferences(entry);
            System.Threading.Interlocked.Increment(ref _totalCancelled);
            lock (_playingLock)
            {
                if (_currentlyPlaying?.Id == entryId)
                    _currentlyPlaying = null;
            }
            _generatingEntries.TryRemove(entryId, out _);
        }
    }

    public void MarkAsFailed(Guid entryId, Exception error)
    {
        if (_allEntries.TryGetValue(entryId, out var entry))
        {
            entry.Error = error;
            if (!entry.TransitionTo(VoiceMessageState.Failed)) return;
            ReleaseGameReferences(entry);
            System.Threading.Interlocked.Increment(ref _totalFailed);
            lock (_playingLock)
            {
                if (_currentlyPlaying?.Id == entryId)
                    _currentlyPlaying = null;
            }
            _generatingEntries.TryRemove(entryId, out _);
        }
    }

    public long GetEpoch(TextSource source)
        => _epochs.TryGetValue(source, out var epoch) ? epoch : 0;

    public bool IsObsolete(VoiceMessage message)
        => message != null && message.CancelEpoch < GetEpoch(message.Source);

    private void BumpEpoch(TextSource source)
        => _epochs.AddOrUpdate(source, 1, (_, current) => current + 1);

    public void CancelBySource(TextSource source)
    {
        // Bump FIRST: a producer that is still building a message for the line being skipped
        // stamped the old epoch, so it is recognised as obsolete even though it enqueues after
        // this flush. Without that ordering the fast-skip race slips through.
        BumpEpoch(source);

        foreach (var entry in _allEntries.Values.Where(e => e.Message.Source == source))
        {
            if (!entry.IsTerminal)
                MarkAsCancelled(entry.Id);
        }

        SourceCancelled?.Invoke(source);
    }

    public void CancelAll()
    {
        // Invalidate every source we've ever seen (plus the ones already carrying an epoch), so
        // in-flight producers of any source are recognised as obsolete — same ordering rationale
        // as CancelBySource.
        var sources = _allEntries.Values.Select(e => e.Message.Source)
                                        .Concat(_epochs.Keys)
                                        .Distinct()
                                        .ToList();
        foreach (var source in sources)
            BumpEpoch(source);

        // Drain the queues so nothing stale is handed out afterwards…
        while (_priorityPendingQueue.TryDequeue(out _)) { }
        while (_normalPendingQueue.TryDequeue(out _)) { }
        while (_readyToPlayQueue.TryDequeue(out _)) { }

        // …then cancel every entry that hasn't finished yet. Walking _allEntries (rather than
        // only the queues + _generatingEntries) is what catches an entry in flight between
        // TryDequeuePendingGeneration and MarkAsGenerating: it sits in no collection at all for
        // that window, so the old code left it alive and it played after the dialog had closed.
        foreach (var entry in _allEntries.Values)
        {
            if (!entry.IsTerminal)
                MarkAsCancelled(entry.Id);
        }

        foreach (var source in sources)
            SourceCancelled?.Invoke(source);
    }

    public VoiceMessageEntry? GetEntry(Guid entryId)
    {
        _allEntries.TryGetValue(entryId, out var entry);
        return entry;
    }

    public VoiceMessageEntry? GetCurrentlyPlaying()
    {
        lock (_playingLock)
        {
            return _currentlyPlaying;
        }
    }

    public IReadOnlyList<VoiceMessageEntry> GetEntriesByState(VoiceMessageState state)
    {
        return _allEntries.Values
            .Where(e => e.State == state)
            .ToList();
    }

    public QueueStatistics GetStatistics()
    {
        var states = _allEntries.Values
            .GroupBy(e => e.State)
            .ToDictionary(g => g.Key, g => g.Count());
        
        return new QueueStatistics
        {
            PendingGeneration = states.GetValueOrDefault(VoiceMessageState.PendingGeneration, 0),
            Generating = states.GetValueOrDefault(VoiceMessageState.Generating, 0),
            ReadyToPlay = states.GetValueOrDefault(VoiceMessageState.ReadyToPlay, 0),
            Playing = states.GetValueOrDefault(VoiceMessageState.Playing, 0),
            Paused = states.GetValueOrDefault(VoiceMessageState.Paused, 0),
            TotalCompleted = _totalCompleted,
            TotalCancelled = _totalCancelled,
            TotalFailed = _totalFailed
        };
    }

    public void Dispose()
    {
        CancelAll();
        _allEntries.Clear();
        _generatingEntries.Clear();
    }
}
