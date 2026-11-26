using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>One line spoken under a voiced NPC's name: what the backend used and how long until the audio started.</summary>
/// <param name="Source">Reference the generation used; null when the line did not play.</param>
/// <param name="FirstAudioMs">Queued → playing, null when it did not play.</param>
/// <param name="Error">Why it did not play.</param>
public sealed record E2eGameVoiceLine(ReferenceSource? Source, int ClipId, long? FirstAudioMs, string? Error)
{
    public bool Played => Error == null;
}

/// <summary>
/// The game-voice lines of the E2E run (plan › Stages row 7 [H7], B6): one line for a voiced NPC goes through the
/// real pipeline. Everything game-audio related stays inside <see cref="IGameVoiceReferenceService"/> (root
/// CLAUDE.md › hard constraint): it picks the NPC from the text index, ranks that NPC's clips (decoded in memory,
/// only ids stored) and hands the reference to the backend. The line is self-test speech (never saved, never on
/// Drive); its clip row and, when the run created it, the character are deleted by id right after. Also owns the
/// opt-in switch (decision D2) for every stage that speaks with a game voice.
/// </summary>
public sealed class E2eGameVoiceGeneration
{
    /// <summary>The options a game-voice part turns on (D2), switched off again at the end of the stage.</summary>
    public static readonly string[] Options =
    [
        nameof(Configuration.GameVoiceReferenceEnabled),
        nameof(Configuration.GameVoiceConsentVersion),
        nameof(Configuration.GameVoiceConsentUtc),
        nameof(Configuration.GameVoiceRankOnConversationStart),
        nameof(Configuration.GameVoiceNpcOverrides),
    ];

    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly IDatabaseService _db;
    private readonly INpcDataService _npcData;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public E2eGameVoiceGeneration(IGameVoiceReferenceService gameVoice, IDatabaseService db, INpcDataService npcData,
        IVoiceMessageQueue queue, IAudioPlaybackService playback, E2eSelfTestSpeaker speaker)
    {
        _gameVoice = gameVoice;
        _db = db;
        _npcData = npcData;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    /// <summary>Ranking with Whisper on the remote plus one generation.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromMinutes(10);

    public Dalamud.Game.ClientLanguage Language => _speaker.Language;

    /// <summary>D2: the run is the opt-in. Old values go into touchedOptions first.</summary>
    public static void EnableOptIn(E2eContext ctx, Configuration config, bool rankOnFirstLine = false)
    {
        E2eOptions.Set(ctx.State, config, nameof(Configuration.GameVoiceConsentVersion), GameVoiceConsent.CurrentVersion);
        E2eOptions.Set<DateTime?>(ctx.State, config, nameof(Configuration.GameVoiceConsentUtc), DateTime.UtcNow);
        E2eOptions.Set(ctx.State, config, nameof(Configuration.GameVoiceReferenceEnabled), true);
        if (rankOnFirstLine) E2eOptions.Set(ctx.State, config, nameof(Configuration.GameVoiceRankOnConversationStart), true);
        config.Save();
        ctx.Save();
    }

    /// <summary>Puts every game-voice option back; drops the decoded PCM when the switch ends up off.</summary>
    public static void RestoreOptIn(E2eContext ctx, Configuration config, IGameVoiceReferenceService gameVoice)
    {
        if (E2eOptions.Restore(ctx.State, config, Options).Count == 0) return;
        config.Save();
        if (!config.GameVoiceReferenceEnabled) gameVoice.ClearMemory();
        ctx.Save();
    }

    /// <summary>The remote stage's part: sample an NPC, rank it, one line with its game voice.</summary>
    public async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var language = _speaker.Language;
        var npc = (await _gameVoice.SampleVoicedNpcsAsync(language, 1, ct: ct).ConfigureAwait(false)).FirstOrDefault();
        if (npc == null) return SelfTestResult.Fail($"No voiced NPC with its own token in the text index ({language}).");
        var name = npc.Name;
        ctx.Progress($"game voice: NPC {name}, ranking its clips");

        var stored = await RankAsync(name, ct).ConfigureAwait(false);
        if (stored == 0) return SelfTestResult.Fail($"Ranking {name} stored no clip (see log).");
        ctx.Progress($"game voice: {stored} clip(s) ranked for {name}");

        var line = await SpeakAsync(ctx, name, ct).ConfigureAwait(false);
        if (!line.Played) return SelfTestResult.Fail(line.Error!);
        return line.Source == ReferenceSource.GameVoice
            ? SelfTestResult.Pass($"{name}: {stored} clip(s) ranked, line spoken with the game voice (clip {line.ClipId}).")
            : SelfTestResult.Fail($"{name}: {stored} clip(s) ranked, but the line was spoken with {line.Source} (server refused the reference? see log).");
    }

    /// <summary>Ranks every token of <paramref name="name"/> (quick stage + Whisper); returns the stored clips.</summary>
    public Task<int> RankAsync(string name, CancellationToken ct) =>
        _gameVoice.RankAsync(new GameVoiceSpeaker(0, name, 0, _speaker.Language), new EKEventId(0, TextSource.VoiceTest), ct);

    /// <summary>
    /// Speaks the test sentence under <paramref name="name"/> and waits until it played. The clip row and a character
    /// the line created are removed again (traces in <c>createdIds</c> first, so a reload mid-line leaves nothing).
    /// </summary>
    public async Task<E2eGameVoiceLine> SpeakAsync(E2eContext ctx, string name, CancellationToken ct)
    {
        var newCharacter = !_npcData.MappedNpcs.Any(n => n.Name == name);
        SelfTestSpeech? speech = null;
        try
        {
            speech = await _speaker.SendAsync(TextSource.VoiceTest, _speaker.TestText, name).ConfigureAwait(false);
            if (newCharacter) ctx.State.CreatedIds.Add($"character:{name}");
            if (speech.Queued is not { } message)
                return new E2eGameVoiceLine(null, 0, null, $"The line for {name} was not queued: {speech.WhyNotQueued()}.");
            if (message.VoiceClipId > 0) ctx.State.CreatedIds.Add($"clip:{message.VoiceClipId}");
            ctx.Save();

            var entry = await PlaybackWatch.WaitForEntryAsync(_queue, e => ReferenceEquals(e.Message, message), ct).ConfigureAwait(false);
            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "game-voice line", ct).ConfigureAwait(false) is { } notPlayed)
                return new E2eGameVoiceLine(null, message.VoiceClipId, null, notPlayed);
            var firstAudio = entry.StartedPlayingAt is { } started ? (long)(started - entry.QueuedAt).TotalMilliseconds : (long?)null;
            return new E2eGameVoiceLine(message.ReferenceSource, message.VoiceClipId, firstAudio, null);
        }
        finally
        {
            if (speech?.Queued is { } queued && ct.IsCancellationRequested) _playback.StopPlaying(queued);
            RemoveTraces(ctx, speech, newCharacter, name);
            ctx.Save();
        }
    }

    private void RemoveTraces(E2eContext ctx, SelfTestSpeech? speech, bool newCharacter, string name)
    {
        if (speech?.Queued is { VoiceClipId: > 0 } message && ctx.State.CreatedIds.Remove($"clip:{message.VoiceClipId}"))
            _db.DeleteVoiceClip(message.VoiceClipId);
        if (newCharacter && ctx.State.CreatedIds.Remove($"character:{name}"))
            SelfTestTraces.RemoveCharacter(_db, _npcData, speech?.Speaker, name);
    }

    /// <summary>"GameVoice, first audio 812 ms" for a detail line.</summary>
    public static string Describe(E2eGameVoiceLine line) =>
        line.Played ? $"{line.Source}, first audio {line.FirstAudioMs?.ToString() ?? "?"} ms" : $"not played: {line.Error}";

    /// <summary>A part that expects <paramref name="expected"/>; null when the line matched.</summary>
    public static string? Mismatch(E2eGameVoiceLine line, ReferenceSource expected, string what) =>
        !line.Played ? $"{what}: {line.Error}"
        : line.Source != expected ? $"{what}: spoken with {line.Source}, expected {expected}"
        : null;
}

/// <summary>Collects <see cref="IGameVoiceReferenceService.PairRanked"/> while a part runs (rank timings for the report).</summary>
public sealed class E2eRankRecorder : IDisposable
{
    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly List<GameVoiceRankTiming> _timings = new();

    public E2eRankRecorder(IGameVoiceReferenceService gameVoice)
    {
        _gameVoice = gameVoice;
        _gameVoice.PairRanked += OnRanked;
    }

    public IReadOnlyList<GameVoiceRankTiming> Timings
    {
        get { lock (_timings) return _timings.ToList(); }
    }

    public IReadOnlyList<GameVoiceRankTiming> For(string token) =>
        Timings.Where(t => string.Equals(t.Token, token, StringComparison.Ordinal)).ToList();

    private void OnRanked(GameVoiceRankTiming timing)
    {
        lock (_timings) _timings.Add(timing);
    }

    public void Dispose() => _gameVoice.PairRanked -= OnRanked;
}
