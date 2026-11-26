using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Three self-test lines play in send order [M2]. Matched by their own message references, so a real
/// line playing in between does not count.
/// </summary>
public sealed class QueueOrderScenario : E2eScenario
{
    internal const int Lines = 3;

    private readonly Configuration _config;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public QueueOrderScenario(Configuration config, IVoiceMessageQueue queue, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "queue-order";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(4);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        var speeches = new List<SelfTestSpeech>();
        try
        {
            for (var i = 0; i < Lines; i++)
            {
                var speech = await _speaker.SendAsync(TextSource.VoiceTest, _speaker.TestText).ConfigureAwait(false);
                speeches.Add(speech);
                if (speech.Queued == null)
                    return SelfTestResult.Fail($"Line {i + 1} was not queued ({speech.WhyNotQueued()}).");
            }
            var messages = speeches.Select(s => s.Queued!).ToList();
            var order = await WatchOrderAsync(messages, ct).ConfigureAwait(false);
            if (order.Error != null) return SelfTestResult.Fail(order.Error);
            var played = string.Join(",", order.Played.Select(i => i + 1));
            return order.Played.SequenceEqual(Enumerable.Range(0, Lines))
                ? SelfTestResult.Pass($"{Lines} lines played in send order ({played}).")
                : SelfTestResult.Fail($"Lines played in the order {played}.");
        }
        finally
        {
            if (ct.IsCancellationRequested) _playback.ClearQueue(TextSource.VoiceTest);
            foreach (var s in speeches) _speaker.RemoveTraces(s);
        }
    }

    /// <summary>
    /// Indexes of <paramref name="messages"/> in the order they started playing, once all ended. Read
    /// from each entry's own start time (the entries are held, so pruning does not matter) instead of
    /// polling what plays now — a short line could start and end between two polls.
    /// </summary>
    private async Task<(List<int> Played, string? Error)> WatchOrderAsync(IReadOnlyList<VoiceMessage> messages, CancellationToken ct)
    {
        var entries = new VoiceMessageEntry?[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            entries[i] = PlaybackWatch.FindEntry(_queue, e => ReferenceEquals(e.Message, message));
            if (entries[i] == null) return ([], $"Line {i + 1} is not tracked by the queue.");
        }
        while (true)
        {
            if (EndedUnplayed(entries) is { } error) return ([], error);
            if (entries.All(e => e!.IsTerminal)) break;
            await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
        }
        var played = Enumerable.Range(0, entries.Length).OrderBy(i => entries[i]!.StartedPlayingAt).ToList();
        return (played, null);
    }

    private static string? EndedUnplayed(VoiceMessageEntry?[] entries)
    {
        for (var i = 0; i < entries.Length; i++)
            if (entries[i] is { IsTerminal: true, StartedPlayingAt: null } e)
                return $"Line {i + 1} ended {e.State} without playing{(e.Error != null ? $": {e.Error.Message}" : "")}.";
        return null;
    }
}

/// <summary>
/// Cancel = per-source epoch: a self-test line is cancelled through the playback service (the same call
/// a dialogue advance makes) while it is still queued → it is obsolete and never plays.
/// </summary>
public sealed class EpochCancelScenario : E2eScenario
{
    /// <summary>How long the line must stay silent after the cancel (the generation takes seconds).</summary>
    internal static TimeSpan SilenceWatch { get; set; } = TimeSpan.FromSeconds(15);

    private readonly Configuration _config;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public EpochCancelScenario(Configuration config, IVoiceMessageQueue queue, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "epoch-cancel";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(1);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        SelfTestSpeech? speech = null;
        try
        {
            var epochBefore = _queue.GetEpoch(TextSource.VoiceTest);
            speech = await _speaker.SendAsync(TextSource.VoiceTest, _speaker.TestText).ConfigureAwait(false);
            if (speech.Queued is not { } message)
                return SelfTestResult.Fail($"The line was not queued ({speech.WhyNotQueued()}).");

            var stateAtCancel = PlaybackWatch.FindEntry(_queue, e => ReferenceEquals(e.Message, message))?.State;
            if (stateAtCancel is VoiceMessageState.Playing)
                return SelfTestResult.Skip("The line was already playing before the cancel (nothing queued to drop).");
            _playback.ClearQueue(TextSource.VoiceTest);
            var epochAfter = _queue.GetEpoch(TextSource.VoiceTest);

            if (!_queue.IsObsolete(message))
                return SelfTestResult.Fail($"Not obsolete after the cancel (epoch {epochBefore} -> {epochAfter}).");

            var until = DateTime.UtcNow + SilenceWatch;
            while (DateTime.UtcNow < until)
            {
                if (_queue.GetCurrentlyPlaying() is { } playing && ReferenceEquals(playing.Message, message))
                {
                    _playback.StopPlaying(message);
                    return SelfTestResult.Fail("The cancelled line played anyway.");
                }
                await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
            }
            var end = PlaybackWatch.FindEntry(_queue, e => ReferenceEquals(e.Message, message))?.State;
            return SelfTestResult.Pass(
                $"Cancelled while {stateAtCancel?.ToString() ?? "untracked"}, epoch {epochBefore} -> {epochAfter}, " +
                $"silent for {SilenceWatch.TotalSeconds:0} s, ended {end?.ToString() ?? "pruned"}.");
        }
        finally
        {
            _speaker.RemoveTraces(speech);
        }
    }
}
