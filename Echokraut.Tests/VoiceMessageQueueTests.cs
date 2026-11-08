using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Services.Queue;
using Xunit;

namespace Echokraut.Tests;

public class VoiceMessageQueueTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static VoiceMessage MakeMessage(TextSource source = TextSource.AddonTalk, string text = "Hello.")
    {
        return new VoiceMessage
        {
            Text = text,
            OriginalText = text,
            Speaker = new NpcMapData(ObjectKind.EventNpc) { Name = "TestNpc" },
            Source = source,
            EventId = new EKEventId(1, source),
        };
    }

    // ── Enqueue / Dequeue ─────────────────────────────────────────────────────

    [Fact]
    public void Enqueue_ThenTryDequeue_ReturnsEntry()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());

        var found = queue.TryDequeuePendingGeneration(out var entry);

        Assert.True(found);
        Assert.NotNull(entry);
    }

    [Fact]
    public void Enqueue_NullMessage_Throws()
    {
        var queue = new VoiceMessageQueue();

        Assert.Throws<ArgumentNullException>(() => queue.Enqueue(null!));
    }

    [Fact]
    public void TryDequeue_EmptyQueue_ReturnsFalse()
    {
        var queue = new VoiceMessageQueue();

        var found = queue.TryDequeuePendingGeneration(out var entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void PriorityMessage_DequeuedBeforeNormal()
    {
        var queue = new VoiceMessageQueue();
        var normal  = MakeMessage(text: "Normal");
        var priority = MakeMessage(text: "Priority");

        queue.Enqueue(normal, isPriority: false);
        queue.Enqueue(priority, isPriority: true);

        queue.TryDequeuePendingGeneration(out var first);

        Assert.Equal("Priority", first!.Message.Text);
    }

    // ── State transitions ─────────────────────────────────────────────────────

    [Fact]
    public void MarkAsGenerating_SetsState()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);

        queue.MarkAsGenerating(entry!.Id);

        Assert.Equal(VoiceMessageState.Generating, queue.GetEntry(entry.Id)!.State);
    }

    [Fact]
    public void MarkAsReadyToPlay_EntryAppearsInReadyQueue()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);

        queue.MarkAsReadyToPlay(entry.Id);

        var found = queue.TryDequeueReadyToPlay(out var ready);
        Assert.True(found);
        Assert.Equal(entry.Id, ready!.Id);
    }

    [Fact]
    public void MarkAsPlaying_SetsCurrentlyPlaying()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);
        queue.MarkAsReadyToPlay(entry.Id);
        queue.TryDequeueReadyToPlay(out _);

        queue.MarkAsPlaying(entry.Id);

        Assert.Equal(entry.Id, queue.GetCurrentlyPlaying()!.Id);
    }

    [Fact]
    public void MarkAsCompleted_ClearsCurrentlyPlaying()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);
        queue.MarkAsReadyToPlay(entry.Id);
        queue.TryDequeueReadyToPlay(out _);
        queue.MarkAsPlaying(entry.Id);

        queue.MarkAsCompleted(entry.Id);

        Assert.Null(queue.GetCurrentlyPlaying());
        Assert.Equal(VoiceMessageState.Completed, queue.GetEntry(entry.Id)!.State);
    }

    [Fact]
    public void MarkAsFailed_SetsErrorAndState()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);

        var error = new Exception("backend down");
        queue.MarkAsFailed(entry!.Id, error);

        var result = queue.GetEntry(entry.Id)!;
        Assert.Equal(VoiceMessageState.Failed, result.State);
        Assert.Same(error, result.Error);
    }

    // ── CancelAll ─────────────────────────────────────────────────────────────

    [Fact]
    public void CancelAll_CancelsEverything()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage(text: "A"));
        queue.Enqueue(MakeMessage(text: "B"), isPriority: true);
        queue.TryDequeuePendingGeneration(out var generating);
        queue.MarkAsGenerating(generating!.Id);

        queue.CancelAll();

        var stats = queue.GetStatistics();
        Assert.Equal(0, stats.PendingGeneration);
        Assert.Equal(0, stats.Generating);
        Assert.True(stats.TotalCancelled >= 2);
    }

    [Fact]
    public void CancelAll_ClearsCurrentlyPlaying()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);
        queue.MarkAsReadyToPlay(entry.Id);
        queue.TryDequeueReadyToPlay(out _);
        queue.MarkAsPlaying(entry.Id);

        queue.CancelAll();

        Assert.Null(queue.GetCurrentlyPlaying());
    }

    // ── CancelBySource ────────────────────────────────────────────────────────

    [Fact]
    public void CancelBySource_OnlyCancelsMatchingSource()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage(TextSource.AddonTalk));
        queue.Enqueue(MakeMessage(TextSource.AddonBubble));

        queue.CancelBySource(TextSource.AddonTalk);

        var talkEntries = queue.GetEntriesByState(VoiceMessageState.Cancelled)
            .Where(e => e.Message.Source == TextSource.AddonTalk).ToList();
        var bubbleEntries = queue.GetEntriesByState(VoiceMessageState.Cancelled)
            .Where(e => e.Message.Source == TextSource.AddonBubble).ToList();

        Assert.Single(talkEntries);
        Assert.Empty(bubbleEntries);
    }

    [Fact]
    public void CancelBySource_AlreadyCompletedNotAffected()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage(TextSource.AddonTalk));
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);
        queue.MarkAsReadyToPlay(entry.Id);
        queue.TryDequeueReadyToPlay(out _);
        queue.MarkAsPlaying(entry.Id);
        queue.MarkAsCompleted(entry.Id);

        queue.CancelBySource(TextSource.AddonTalk);

        Assert.Equal(VoiceMessageState.Completed, queue.GetEntry(entry.Id)!.State);
    }

    // ── Statistics ────────────────────────────────────────────────────────────

    [Fact]
    public void GetStatistics_ReflectsCurrentState()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage(text: "A")); // pending
        queue.Enqueue(MakeMessage(text: "B")); // will become generating

        queue.TryDequeuePendingGeneration(out var b);
        queue.MarkAsGenerating(b!.Id);

        var stats = queue.GetStatistics();

        Assert.Equal(1, stats.PendingGeneration);
        Assert.Equal(1, stats.Generating);
    }

    [Fact]
    public void GetStatistics_TotalCompletedCounts()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);
        queue.MarkAsReadyToPlay(entry.Id);
        queue.TryDequeueReadyToPlay(out _);
        queue.MarkAsPlaying(entry.Id);
        queue.MarkAsCompleted(entry.Id);

        Assert.Equal(1, queue.GetStatistics().TotalCompleted);
    }

    // ── GetEntriesByState ─────────────────────────────────────────────────────

    [Fact]
    public void GetEntriesByState_ReturnsOnlyMatchingState()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage(text: "A"));
        queue.Enqueue(MakeMessage(text: "B"));
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);

        var pending    = queue.GetEntriesByState(VoiceMessageState.PendingGeneration);
        var generating = queue.GetEntriesByState(VoiceMessageState.Generating);

        Assert.Single(pending);
        Assert.Single(generating);
    }

    // ── VoiceMessageEntry timestamps ──────────────────────────────────────────

    [Fact]
    public void TransitionToGenerating_SetsStartedGeneratingAt()
    {
        var entry = new VoiceMessageEntry(MakeMessage());
        entry.TransitionTo(VoiceMessageState.Generating);

        Assert.NotNull(entry.StartedGeneratingAt);
    }

    [Fact]
    public void TransitionToCompleted_SetsCompletedAt()
    {
        var entry = new VoiceMessageEntry(MakeMessage());
        entry.TransitionTo(VoiceMessageState.Completed);

        Assert.NotNull(entry.CompletedAt);
        Assert.NotNull(entry.GetTotalProcessingTime());
    }

    // ── Terminal states are final ─────────────────────────────────────────────
    // A dialogue line whose generation is already running keeps running when the dialog
    // closes. Without these guards the finished clip flipped its cancelled entry back to
    // ReadyToPlay and played after the dialog box was long gone.

    [Fact]
    public void TransitionFromCancelled_IsRejected()
    {
        var entry = new VoiceMessageEntry(MakeMessage());
        entry.TransitionTo(VoiceMessageState.Cancelled);

        var moved = entry.TransitionTo(VoiceMessageState.ReadyToPlay);

        Assert.False(moved);
        Assert.True(entry.IsTerminal);
        Assert.Equal(VoiceMessageState.Cancelled, entry.State);
    }

    [Fact]
    public void MarkAsReadyToPlay_AfterCancel_DoesNotEnqueueForPlayback()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);

        // Dialog closes mid-generation…
        queue.CancelBySource(TextSource.AddonTalk);
        // …and the backend delivers the finished audio afterwards.
        queue.MarkAsReadyToPlay(entry.Id);

        Assert.False(queue.TryDequeueReadyToPlay(out var ready));
        Assert.Null(ready);
        Assert.Equal(VoiceMessageState.Cancelled, entry.State);
    }

    [Fact]
    public void MarkAsGenerating_AfterCancel_KeepsEntryCancelled()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.CancelAll();

        queue.MarkAsGenerating(entry!.Id);

        Assert.Equal(VoiceMessageState.Cancelled, entry.State);
    }

    [Fact]
    public void MarkAsPlaying_AfterCancel_DoesNotBecomeCurrentlyPlaying()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsCancelled(entry!.Id);

        queue.MarkAsPlaying(entry.Id);

        Assert.Null(queue.GetCurrentlyPlaying());
        Assert.Equal(VoiceMessageState.Cancelled, entry.State);
    }

    // ── Cancellation epochs (fast-skip race) ──────────────────────────────────
    // Producing a message is async: when the player skips a line, the message for that line can
    // still be under construction and reaches the queue AFTER the flush. The epoch stamp is what
    // identifies it as belonging to a line already left behind.

    private static VoiceMessage MakeMessage(VoiceMessageQueue queue, TextSource source = TextSource.AddonTalk,
                                            string text = "Hello.")
    {
        var message = MakeMessage(source, text);
        message.CancelEpoch = queue.GetEpoch(source);
        return message;
    }

    [Fact]
    public void FreshMessage_IsNotObsolete()
    {
        var queue = new VoiceMessageQueue();

        Assert.False(queue.IsObsolete(MakeMessage(queue)));
    }

    [Fact]
    public void MessageStampedBeforeCancel_IsObsolete_EvenIfEnqueuedAfterwards()
    {
        var queue = new VoiceMessageQueue();
        // Line 2 starts being built…
        var line2 = MakeMessage(queue, text: "Line 2");

        // …the player skips to line 3, which flushes the source…
        queue.CancelBySource(TextSource.AddonTalk);

        // …and only now does line 2 finish and reach the queue.
        queue.Enqueue(line2, isPriority: true);

        Assert.True(queue.IsObsolete(line2));
    }

    [Fact]
    public void CancelDuringStreamStartup_LeavesEntryCancelledAndMessageObsolete()
    {
        // The stream startup blocks (WAV sniff + prebuffer cushion): the entry is already marked
        // Playing when the player skips. AudioPlaybackService re-checks both signals after the
        // startup returns and discards the line — this is what those two signals must report.
        var queue = new VoiceMessageQueue();
        var line = MakeMessage(queue);
        queue.Enqueue(line, isPriority: true);
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsGenerating(entry!.Id);
        queue.MarkAsReadyToPlay(entry.Id);
        queue.TryDequeueReadyToPlay(out _);
        queue.MarkAsPlaying(entry.Id);

        queue.CancelBySource(TextSource.AddonTalk);

        Assert.Equal(VoiceMessageState.Cancelled, entry.State);
        Assert.True(queue.IsObsolete(line));
    }

    [Fact]
    public void MessageStampedAfterCancel_IsNotObsolete()
    {
        var queue = new VoiceMessageQueue();
        queue.CancelBySource(TextSource.AddonTalk);

        var line3 = MakeMessage(queue, text: "Line 3");

        Assert.False(queue.IsObsolete(line3));
    }

    [Fact]
    public void CancelBySource_DoesNotInvalidateOtherSources()
    {
        var queue = new VoiceMessageQueue();
        var bubble = MakeMessage(queue, TextSource.AddonBubble, "Bubble");

        queue.CancelBySource(TextSource.AddonTalk);

        Assert.False(queue.IsObsolete(bubble));
    }

    [Fact]
    public void CancelAll_InvalidatesEverySourceInFlight()
    {
        var queue = new VoiceMessageQueue();
        var talk = MakeMessage(queue, TextSource.AddonTalk, "Talk");
        var bubble = MakeMessage(queue, TextSource.AddonBubble, "Bubble");
        queue.Enqueue(talk, isPriority: true);
        queue.Enqueue(bubble);

        queue.CancelAll();

        Assert.True(queue.IsObsolete(talk));
        Assert.True(queue.IsObsolete(bubble));
    }

    [Fact]
    public void CancelBySource_RaisesSourceCancelled()
    {
        var queue = new VoiceMessageQueue();
        var seen = new List<TextSource>();
        queue.SourceCancelled += seen.Add;

        queue.CancelBySource(TextSource.AddonTalk);

        Assert.Equal(new[] { TextSource.AddonTalk }, seen);
    }

    [Fact]
    public void SourceCancelled_FiresAfterTheEpochMoved()
    {
        // The backend aborts the in-flight request in this handler; by then the epoch must
        // already identify that request's message as obsolete, or the abort would race the
        // "discard the result" check.
        var queue = new VoiceMessageQueue();
        var inFlight = MakeMessage(queue, text: "In flight");
        bool obsoleteWhenNotified = false;
        queue.SourceCancelled += _ => obsoleteWhenNotified = queue.IsObsolete(inFlight);

        queue.CancelBySource(TextSource.AddonTalk);

        Assert.True(obsoleteWhenNotified);
    }

    [Fact]
    public void MarkAsCompleted_AfterCancel_DoesNotDoubleCount()
    {
        var queue = new VoiceMessageQueue();
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsCancelled(entry!.Id);

        queue.MarkAsCompleted(entry.Id);

        var stats = queue.GetStatistics();
        Assert.Equal(1, stats.TotalCancelled);
        Assert.Equal(0, stats.TotalCompleted);
    }

    // ── Retention of finished entries ─────────────────────────────────────────

    [Fact]
    public void FinishedEntries_ArePrunedOnceTheyOutliveTheRetention()
    {
        // Without pruning, _allEntries kept every line ever spoken for the whole session and
        // CancelAll/GetStatistics walked that history on every dialogue advance.
        var queue = new VoiceMessageQueue { TerminalRetention = TimeSpan.Zero };

        var stale = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            queue.Enqueue(MakeMessage(text: $"Line {i}"));
            queue.TryDequeuePendingGeneration(out var e);
            queue.MarkAsCompleted(e!.Id);
            stale.Add(e.Id);
        }

        // The next enqueue is what triggers the amortised sweep.
        queue.Enqueue(MakeMessage(text: "Fresh"));

        Assert.All(stale, id => Assert.Null(queue.GetEntry(id)));
    }

    [Fact]
    public void FinishedEntry_StaysReachableInsideTheRetention()
    {
        // The playback loop and the UI still look an entry up right after it went terminal.
        var queue = new VoiceMessageQueue(); // default retention (30s)
        queue.Enqueue(MakeMessage());
        queue.TryDequeuePendingGeneration(out var entry);
        queue.MarkAsCompleted(entry!.Id);

        queue.Enqueue(MakeMessage(text: "Next"));

        Assert.NotNull(queue.GetEntry(entry.Id));
    }

    [Fact]
    public void TerminalEntry_ReleasesItsGameObjectReferences()
    {
        // An entry lingers for the retention window — it must not pin an IGameObject for that
        // long. Speaker/Stream stay: OnSourceEnded still saves the audio off the message.
        var queue = new VoiceMessageQueue();
        var message = MakeMessage();
        queue.Enqueue(message);
        queue.TryDequeuePendingGeneration(out var entry);

        queue.MarkAsCancelled(entry!.Id);

        Assert.Null(message.SpeakerObj);
        Assert.Null(message.SpeakerFollowObj);
        Assert.NotNull(message.Speaker);
    }
}
