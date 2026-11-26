using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echokraut.Services.Queue;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.Checks;

/// <summary>Waits until a queue entry matching a predicate plays and (optionally) finishes.</summary>
internal static class PlaybackWatch
{
    /// <summary>Poll interval; a spoken line lasts far longer than this.</summary>
    internal static TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>The first entry seen playing that matches <paramref name="match"/>.</summary>
    public static async Task<VoiceMessageEntry> WaitForPlayingAsync(IVoiceMessageQueue queue,
        Func<VoiceMessageEntry, bool> match, CancellationToken ct)
    {
        while (true)
        {
            if (queue.GetCurrentlyPlaying() is { } entry && match(entry)) return entry;
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The first entry in any state (also finished ones still retained) that matches <paramref name="match"/>.</summary>
    public static VoiceMessageEntry? FindEntry(IVoiceMessageQueue queue, Func<VoiceMessageEntry, bool> match)
    {
        foreach (var state in Enum.GetValues<VoiceMessageState>())
            foreach (var entry in queue.GetEntriesByState(state))
                if (match(entry)) return entry;
        return null;
    }

    /// <summary>Polls <see cref="FindEntry"/> until an entry matches (the caller's timeout ends it).</summary>
    public static async Task<VoiceMessageEntry> WaitForEntryAsync(IVoiceMessageQueue queue,
        Func<VoiceMessageEntry, bool> match, CancellationToken ct)
    {
        while (true)
        {
            if (FindEntry(queue, match) is { } entry) return entry;
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Null once <paramref name="entry"/> played and finished; else why it did not play.</summary>
    public static async Task<string?> WaitPlayedAsync(IVoiceMessageQueue queue, VoiceMessageEntry entry, string what,
        CancellationToken ct)
    {
        while (true)
        {
            if (queue.GetCurrentlyPlaying() is { } current && current.Id == entry.Id)
            {
                await WaitUntilFinishedAsync(queue, current, ct).ConfigureAwait(false);
                return null;
            }
            if (entry.IsTerminal)
                return entry.StartedPlayingAt != null
                    ? null
                    : $"The {what} ended {entry.State} without playing{(entry.Error != null ? $": {entry.Error.Message}" : "")}.";
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Until <paramref name="entry"/> is no longer the one playing.</summary>
    public static async Task WaitUntilFinishedAsync(IVoiceMessageQueue queue, VoiceMessageEntry entry, CancellationToken ct)
    {
        while (queue.GetCurrentlyPlaying() is { } current && current.Id == entry.Id)
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// End to end through the real pipeline: text cleaning, character creation, voice assignment, DB
/// log, cache lookup, backend, queue and BASS playback — with a sentence of our own instead of a
/// game line, so no quest progress is needed. Everything it creates (character row, voice clip
/// row) is removed afterwards; the audio is never saved (<see cref="VoiceMessage.IsSelfTest"/>).
/// The pipeline is started on the framework thread like the addon helpers do: it reads the object
/// table (<c>LocalPlayer</c>), which Dalamud only allows there (first in-game run, 2026-11-21:
/// "Not on main thread!" from the thread pool).
/// </summary>
public sealed class PipelineCheck : SelfTestCheck
{
    /// <summary>Name of the throwaway speaker. Not a game NPC, so no alias, voice-name or game-voice match.</summary>
    internal const string SpeakerName = "Echokraut Selftest";

    private readonly Configuration _config;
    private readonly IVoiceMessageProcessor _processor;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly IDatabaseService _db;
    private readonly INpcDataService _npcData;
    private readonly IClientState _clientState;
    private readonly IFramework _framework;
    private readonly ILogService _log;

    public PipelineCheck(Configuration config, IVoiceMessageProcessor processor, IVoiceMessageQueue queue,
        IAudioPlaybackService playback, IDatabaseService db, INpcDataService npcData, IClientState clientState,
        IFramework framework, ILogService log)
    {
        _config = config;
        _processor = processor;
        _queue = queue;
        _playback = playback;
        _db = db;
        _npcData = npcData;
        _clientState = clientState;
        _framework = framework;
        _log = log;
    }

    public override string Id => "pipeline";
    public override string Name => Loc.S("Dialogue pipeline and playback");
    public override TimeSpan Timeout => TimeSpan.FromSeconds(180);

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        if (!_config.Enabled) return SelfTestResult.Skip("Echokraut is switched off (/ekt).");

        var eventId = LiveGeneration.NewEventId(_log, nameof(PipelineCheck));
        SelfTestSpeech? speech = null;
        try
        {
            var text = Constants.TestMessage(_clientState.ClientLanguage);
            speech = await _framework.RunOnFrameworkThread(
                () => _processor.ProcessSelfTestSpeechAsync(eventId, SpeakerName, text)).ConfigureAwait(false);
            if (speech.Queued is not { } message)
                return SelfTestResult.Fail(speech.Error != null
                    ? $"The pipeline threw {speech.Error}"
                    : speech.Speaker == null
                    ? "The pipeline stopped before resolving a speaker (backend unavailable or text not speakable, see log)."
                    : $"The pipeline stopped before the backend (voice \"{speech.Speaker.voice}\", see log: muted, no voice or volume 0).");

            var entry = await PlaybackWatch.WaitForPlayingAsync(_queue, e => ReferenceEquals(e.Message, message), ct)
                .ConfigureAwait(false);
            var waited = entry.StartedPlayingAt is { } p ? (p - entry.QueuedAt).TotalSeconds : 0;
            await PlaybackWatch.WaitUntilFinishedAsync(_queue, entry, ct).ConfigureAwait(false);

            if (message.VoiceClipId <= 0)
                return SelfTestResult.Fail("Played, but the line was not logged to the database (VoiceClipId 0).");
            return SelfTestResult.Pass(
                $"Voice {message.Speaker.voice}, played after {waited:0.0} s, logged as clip {message.VoiceClipId} ({message.ReferenceSource}).");
        }
        finally
        {
            if (speech?.Queued is { } queued && ct.IsCancellationRequested)
                _playback.StopPlaying(queued);
            SelfTestTraces.RemoveCharacter(_db, _npcData, speech?.Speaker, SpeakerName);
            _log.End(nameof(PipelineCheck), eventId);
        }
    }
}

/// <summary>
/// The player triggers a real game line (talk to an NPC, a speech bubble, a /say line); passes as
/// soon as a line of that source plays. Covers the addon hooks the automatic run cannot reach.
/// </summary>
public sealed class InteractivePlaybackCheck : SelfTestCheck
{
    private readonly IVoiceMessageQueue _queue;
    private readonly TextSource _source;
    private readonly Func<string?> _skipReason;
    private readonly string _nameKey;
    private readonly string _instructionKey;
    private readonly TimeProvider _time;

    /// <param name="skipReason">Null when the source is voiced in the current configuration, else why not.</param>
    /// <param name="time">Clock that decides which lines count as "after the start"; the queue's clock.</param>
    public InteractivePlaybackCheck(IVoiceMessageQueue queue, string id, TextSource source, string nameKey,
        string instructionKey, Func<string?> skipReason, TimeProvider? time = null)
    {
        _queue = queue;
        _time = time ?? TimeProvider.System;
        Id = id;
        _source = source;
        _nameKey = nameKey;
        _instructionKey = instructionKey;
        _skipReason = skipReason;
    }

    public override string Id { get; }
    public override string Name => Loc.S(_nameKey);
    public override bool Interactive => true;
    public override string? Instruction => Loc.S(_instructionKey);
    public override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        if (_skipReason() is { } reason) return SelfTestResult.Skip(reason);
        var since = _time.GetUtcNow().UtcDateTime;
        var entry = await PlaybackWatch.WaitForPlayingAsync(_queue,
            e => e.Message.Source == _source && e.QueuedAt >= since, ct).ConfigureAwait(false);
        var text = entry.Message.Text ?? "";
        return SelfTestResult.Pass($"{entry.Message.Speaker?.Name}: \"{(text.Length > 60 ? text[..60] + "…" : text)}\"");
    }
}
