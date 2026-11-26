using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Build step B6, opt-in part (plan › Coverage, R2 phase 6; user decision 2026-11-25: runs only when listed in
/// <c>--stages</c>): the game-voice batch index through <see cref="IGameVoiceReferenceService.BuildIndexAsync"/>, the
/// same call as the Backend tab's button. Same hard constraint as <see cref="GameVoiceStage"/> (opt-in for the stage,
/// every part inside an <see cref="E2eAudioFileWatch"/>), the frame watchdog over every batch (op <c>game-voice-index</c>).
/// <list type="bullet">
/// <item><c>:abort-resume</c> — fast mode, cancelled after <see cref="AbortAfter"/> pairs, started again and cancelled
/// again: the second run counts the first one's pairs as current and ranks others.</item>
/// <item><c>:whisper</c> — with Whisper, cancelled after <see cref="WhisperPairs"/> pairs: every ranking used it.</item>
/// <item><c>:full</c> — the whole batch (fast, resumes) with voice-change detection; the matching report
/// <c>game_voice_unmatched_&lt;lang&gt;.json</c> exists and is complete (15-30 min).</item>
/// <item><c>:splits</c> — the detection's suggestions hold the measured German boundaries (Y'shtola 06100, Hydaelyn
/// 06000) and none for Alphinaud/Thancred; only with <c>/health speaker_embedding</c> (XTTS).</item>
/// <item><c>:unseen-npc</c> — after the batch a voiced NPC never met speaks at once with its game voice, no ranking.</item>
/// </list>
/// </summary>
public sealed class GameVoiceIndexStage : E2eStage
{
    public const string StageId = "game-voice-index";
    public const string PartPrefix = StageId + ":";
    public const string AbortResumePart = PartPrefix + "abort-resume";
    public const string WhisperPart = PartPrefix + "whisper";
    public const string FullPart = PartPrefix + "full";
    public const string SplitsPart = PartPrefix + "splits";
    public const string UnseenNpcPart = PartPrefix + "unseen-npc";

    internal const int AbortAfter = 5;
    internal const int WhisperPairs = 3;

    /// <summary>How often a running batch writes a progress line (done/total, ETA).</summary>
    internal static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan FullBatchTimeout = TimeSpan.FromMinutes(120);

    private static readonly EKEventId Event = new(0, TextSource.None);

    private readonly Configuration _config;
    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly INpcDataService _npcData;
    private readonly E2eGameVoiceGeneration _lines;
    private readonly Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> _health;
    private readonly string? _pluginConfigDir;
    private readonly Func<E2eRequest, IEnumerable<(string Label, string? Path)>> _watchRoots;
    private readonly E2eFrameMonitor? _frames;

    public GameVoiceIndexStage(Configuration config, IGameVoiceReferenceService gameVoice, INpcDataService npcData,
        E2eGameVoiceGeneration lines, Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> health,
        string? pluginConfigDir, E2eFrameMonitor? frames = null,
        Func<E2eRequest, IEnumerable<(string Label, string? Path)>>? watchRoots = null)
    {
        _config = config;
        _gameVoice = gameVoice;
        _npcData = npcData;
        _lines = lines;
        _health = health;
        _pluginConfigDir = pluginConfigDir;
        _watchRoots = watchRoots ?? (r => E2eAudioFileWatch.RunRoots(r.InstallRoot, _pluginConfigDir));
        _frames = frames;
    }

    public override string Id => StageId;
    public override bool ExplicitOnly => true;
    public override IReadOnlyList<string> Needs => [FirstTimeStage.StageId];
    public override string? UnmetNeed(E2eState state) => FirstUnmet(state, Needs) ?? EngineStage.ActiveEngineUnmet(state);
    public override TimeSpan Timeout => FullBatchTimeout + TimeSpan.FromMinutes(30);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");

        E2eGameVoiceGeneration.RestoreOptIn(ctx, _config, _gameVoice);
        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        try
        {
            E2eGameVoiceGeneration.EnableOptIn(ctx, _config);
            var availability = await _gameVoice.RefreshAvailabilityAsync(Event, ct).ConfigureAwait(false);
            var health = await _health(ct).ConfigureAwait(false);
            var blocker = availability == GameVoiceAvailability.Available
                ? null
                : $"game voice is {availability} on {_config.EchokrauTts.TtsBackendArg} (needs an engine with inline_reference)";

            await RunPartAsync(ctx, outcomes, AbortResumePart, blocker, c => Watched(ctx, AbortResumeAsync, c), ct).ConfigureAwait(false);
            await RunPartAsync(ctx, outcomes, WhisperPart, blocker ?? (health?.asr_check == true ? null : "the engine has no /asr-check (Whisper)"),
                c => Watched(ctx, WhisperAsync, c), ct).ConfigureAwait(false);

            GameVoiceBatchResult? full = null;
            var fullOutcome = await RunPartAsync(ctx, outcomes, FullPart, blocker, c => Watched(ctx, async (x, cc) =>
            {
                var (result, batch) = await FullAsync(x, health, cc).ConfigureAwait(false);
                full = batch;
                return result;
            }, c), ct).ConfigureAwait(false);

            var splitsBlocker = health?.speaker_embedding == true ? null : $"no /health speaker_embedding on {_config.EchokrauTts.TtsBackendArg} (run with --engines xtts)";
            await RunPartAsync(ctx, outcomes, SplitsPart,
                blocker ?? splitsBlocker ?? (full != null ? null : $"needs {FullPart} in this load ({E2eLogFormat.Kind(fullOutcome)})"),
                _ => Task.FromResult(JudgeSplits(full!)), ct).ConfigureAwait(false);
            await RunPartAsync(ctx, outcomes, UnseenNpcPart,
                blocker ?? (fullOutcome == SelfTestOutcome.Pass ? null : $"needs {FullPart} ({E2eLogFormat.Kind(fullOutcome)})"),
                c => Watched(ctx, UnseenNpcAsync, c), ct).ConfigureAwait(false);
        }
        finally
        {
            if (!ct.IsCancellationRequested) E2eGameVoiceGeneration.RestoreOptIn(ctx, _config, _gameVoice);
        }
        return FromParts(outcomes);
    }

    // ── parts ──────────────────────────────────────────────────────────────

    private async Task<SelfTestResult> AbortResumeAsync(E2eContext ctx, CancellationToken ct)
    {
        var outdatedBefore = await _gameVoice.CountOutdatedTokensAsync(_lines.Language, ct).ConfigureAwait(false);
        var first = await BatchAsync(ctx, "abort 1", new GameVoiceBatchOptions(WithAsr: false), AbortAfter, ct).ConfigureAwait(false);
        var second = await BatchAsync(ctx, "abort 2", new GameVoiceBatchOptions(WithAsr: false), AbortAfter, ct).ConfigureAwait(false);
        var problems = new List<string>();
        problems.AddRange(Freezes(first.Op));
        problems.AddRange(Freezes(second.Op));
        problems.AddRange(JudgeAbortResume(first.Result, second.Result, AbortAfter));
        var detail = $"outdated tokens before {outdatedBefore}; run 1: {Describe(first.Result)}; run 2: {Describe(second.Result)}";
        return FromProblems(problems, detail);
    }

    /// <summary>
    /// Both runs cancelled after <paramref name="after"/> pairs, and the second one saw the first one's pairs as current
    /// (resume): its up-to-date count grew by at least what the first ranked.
    /// </summary>
    internal static IEnumerable<string> JudgeAbortResume(GameVoiceBatchResult first, GameVoiceBatchResult second, int after)
    {
        foreach (var (name, r) in new[] { ("run 1", first), ("run 2", second) })
        {
            if (r.NotRunReason != null) yield return $"{name} did not run: {r.NotRunReason}";
            else if (!r.Cancelled) yield return $"{name} not cancelled ({r.Ranked} ranked)";
            else if (r.Ranked != after) yield return $"{name} ranked {r.Ranked} pair(s) before the cancel took, expected {after}";
        }
        if (first.NotRunReason == null && second.NotRunReason == null && second.UpToDate < first.UpToDate + first.Ranked)
            yield return $"resume: run 2 counted {second.UpToDate} pair(s) current, expected >= {first.UpToDate + first.Ranked}";
    }

    private async Task<SelfTestResult> WhisperAsync(E2eContext ctx, CancellationToken ct)
    {
        using var ranks = new E2eRankRecorder(_gameVoice);
        var run = await BatchAsync(ctx, "whisper", new GameVoiceBatchOptions(WithAsr: true), WhisperPairs, ct).ConfigureAwait(false);
        var problems = Freezes(run.Op).ToList();
        if (run.Result.NotRunReason != null) problems.Add($"did not run: {run.Result.NotRunReason}");
        var timings = ranks.Timings;
        if (timings.Count == 0) problems.Add("no pair ranked");
        if (timings.Where(t => !t.WithAsr).Select(t => t.Token).ToList() is { Count: > 0 } without)
            problems.Add($"ranked without Whisper: {string.Join(", ", without)}");
        var detail = $"{Describe(run.Result)}; " + string.Join(", ", timings.Select(t => $"{t.Token} {t.TotalMs} ms"));
        return FromProblems(problems, detail);
    }

    private async Task<(SelfTestResult, GameVoiceBatchResult?)> FullAsync(E2eContext ctx, EchokrauTtsHealthResponse? health, CancellationToken ct)
    {
        var options = new GameVoiceBatchOptions(WithAsr: false, DetectVoiceChanges: health?.speaker_embedding == true);
        var (result, op) = await BatchAsync(ctx, "full", options, null, ct).ConfigureAwait(false);
        var problems = Freezes(op).ToList();
        if (result.NotRunReason != null) problems.Add($"did not run: {result.NotRunReason}");
        else if (result.Cancelled) problems.Add("cancelled");
        var outdated = await _gameVoice.CountOutdatedTokensAsync(_lines.Language, ct).ConfigureAwait(false);
        var langCode = GameVoiceReferenceService.LanguageCode(_lines.Language);
        var (reportProblems, reportDetail) = E2eGameVoiceChecks.JudgeUnmatchedReport(Read(result.ReportPath), langCode);
        problems.AddRange(reportProblems);
        var detail = $"{Describe(result)}, {op.DurationMs / 1000} s, frames {op.Frames}; outdated after {outdated}; {reportDetail}";
        var verdict = FromProblems(problems, detail);
        return (verdict, result.NotRunReason == null && !result.Cancelled ? result : null);
    }

    private SelfTestResult JudgeSplits(GameVoiceBatchResult full)
    {
        var (problems, detail) = E2eGameVoiceChecks.JudgeSplitSuggestions(Read(full.SuggestionsPath), GameVoiceReferenceService.LanguageCode(_lines.Language));
        return FromProblems(problems, detail);
    }

    private async Task<SelfTestResult> UnseenNpcAsync(E2eContext ctx, CancellationToken ct)
    {
        // Met = a character in the DB, or spoken by an earlier game-voice part (those characters are deleted again).
        var known = new HashSet<string>(_npcData.MappedNpcs.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        known.UnionWith(GameVoiceStage.NpcsSpokenBy(ctx.State));
        var candidates = await _gameVoice.SampleVoicedNpcsAsync(_lines.Language, 20, ct: ct).ConfigureAwait(false);
        var npc = candidates.FirstOrDefault(n => !known.Contains(n.Name));
        if (npc == null) return SelfTestResult.Fail($"No voiced NPC left that the run has not met ({candidates.Count} sampled).");
        using var ranks = new E2eRankRecorder(_gameVoice);
        var line = await _lines.SpeakAsync(ctx, npc.Name, ct).ConfigureAwait(false);
        var problems = new List<string>();
        if (E2eGameVoiceGeneration.Mismatch(line, ReferenceSource.GameVoice, npc.Name) is { } wrong) problems.Add(wrong);
        if (ranks.Timings.Count > 0) problems.Add($"ranked live although the batch indexed it: {string.Join(", ", ranks.Timings.Select(t => t.Token))}");
        var detail = $"{npc.Name} ({npc.Token}, never met): {E2eGameVoiceGeneration.Describe(line)}";
        return FromProblems(problems, detail);
    }

    // ── batch plumbing ─────────────────────────────────────────────────────

    /// <summary>One batch, cancelled after <paramref name="cancelAfter"/> pairs (null = to the end), under the frame watchdog.</summary>
    private async Task<(GameVoiceBatchResult Result, E2eOpTelemetry Op)> BatchAsync(E2eContext ctx, string label,
        GameVoiceBatchOptions options, int? cancelAfter, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var lastLine = DateTime.MinValue;
        // The batch reports two phases (ranking, then voice-change detection) with one running clock.
        var phaseStart = TimeSpan.Zero;
        var lastDone = 0;
        var lastElapsed = TimeSpan.Zero;
        var progress = new SyncProgress(p =>
        {
            if (p.Done < lastDone) phaseStart = lastElapsed; // the new phase began when the last one ended
            lastDone = p.Done;
            lastElapsed = p.Elapsed;
            if (cancelAfter is { } n && p.Done >= n) cts.Cancel();
            if (DateTime.UtcNow - lastLine < ProgressEvery && p.Done != p.Total) return;
            lastLine = DateTime.UtcNow;
            ctx.Progress($"index {label}: {p.Done}/{p.Total} pair(s), {p.Token}, {p.Elapsed:hh\\:mm\\:ss}{Eta(p, phaseStart)}");
        });
        GameVoiceBatchResult result = null!;
        var op = await Measure("game-voice-index:" + label, async () =>
            result = await _gameVoice.BuildIndexAsync(_lines.Language, options, progress, cts.Token).ConfigureAwait(false)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        ctx.State.Telemetry.Ops.Add(op);
        ctx.Save();
        return (result, op);
    }

    private async Task<E2eOpTelemetry> Measure(string name, Func<Task> op)
    {
        if (_frames != null) return await _frames.MeasureAsync(name, op).ConfigureAwait(false);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await op().ConfigureAwait(false);
        return new E2eOpTelemetry(name, watch.ElapsedMilliseconds, E2eFrameMonitor.Summarize([]));
    }

    /// <summary>Remaining time from the mean pair time since <paramref name="phaseStart"/> (the phase's own clock).</summary>
    internal static string Eta(GameVoiceBatchProgress p, TimeSpan phaseStart)
    {
        if (p.Done <= 0 || p.Done >= p.Total) return "";
        var spent = p.Elapsed - phaseStart;
        if (spent < TimeSpan.Zero) spent = TimeSpan.Zero;
        var left = TimeSpan.FromTicks(spent.Ticks / p.Done * (p.Total - p.Done));
        return $", ETA {left:hh\\:mm\\:ss}";
    }

    private static IEnumerable<string> Freezes(E2eOpTelemetry op) =>
        op.Frames.MaxMs > E2eFrameMonitor.FreezeLimitMs
            ? [$"{op.Op} froze the game ({op.Frames.MaxMs:0} ms frame > {E2eFrameMonitor.FreezeLimitMs:0} ms)"]
            : [];

    private static string Describe(GameVoiceBatchResult r) =>
        r.NotRunReason != null
            ? $"not run ({r.NotRunReason})"
            : $"{r.Ranked} ranked ({r.WithReference} with reference), {r.UpToDate} current, {r.Tokens} token(s), " +
              $"{r.Unmatched} unmatched, {r.Suggestions} suggestion(s){(r.Cancelled ? ", cancelled" : "")}";

    private static string? Read(string? path)
    {
        try { return path != null && File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (IOException) { return null; }
    }

    private Task<SelfTestResult> Watched(E2eContext ctx, Func<E2eContext, CancellationToken, Task<SelfTestResult>> part,
        CancellationToken ct) =>
        E2eAudioFileWatch.RunAsync(_watchRoots(ctx.Request), () => part(ctx, ct));

    /// <summary>Reports on the calling thread (the batch's), so the cancel after N pairs lands before the next pair.</summary>
    private sealed class SyncProgress(Action<GameVoiceBatchProgress> onReport) : IProgress<GameVoiceBatchProgress>
    {
        public void Report(GameVoiceBatchProgress value) => onReport(value);
    }
}
