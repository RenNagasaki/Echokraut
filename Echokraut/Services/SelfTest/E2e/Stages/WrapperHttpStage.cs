using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>Waits of the stage <c>wrapper-http</c>; tests shorten them.</summary>
/// <param name="RunToEndWait">How long the client that stayed may take to get its whole clip (cancel part, pair one).</param>
public sealed record WrapperHttpTimings(TimeSpan LogSettle, TimeSpan Poll, TimeSpan CancelWait, TimeSpan GpuWindow, TimeSpan FirstBytesWait,
    TimeSpan RunToEndWait)
{
    public static WrapperHttpTimings Default { get; } = new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(3));
}

/// <summary>
/// Stage <c>wrapper-http</c> (plan › B4): the wrapper's response cache and cancel refcount against the engine the
/// <c>engine</c> stage left on, plus the two plugin paths that depend on them. Parts, each reported as
/// <c>wrapper-http:&lt;part&gt;</c>:
/// <list type="bullet">
/// <item><c>cache</c> — direct <c>POST /tts</c>: miss → hit (same bytes) → <c>no_cache</c> bypass → hit, then two
/// at once → miss + shared (same bytes); exactly one <c>tts request start</c> per generation, <c>/health</c>
/// counters move by exactly that, the built-in page sends <c>no_cache</c>.</item>
/// <item><c>cancel</c> — two clients on one generation: cancelling one leaves it running, cancelling both cancels
/// it (job state, no <c>tts request done</c>, GPU load drops on cuda), the same text afterwards is a miss.</item>
/// <item><c>vcm-regenerate</c> — <see cref="IVoiceClipManagerService.GenerateForVoiceClip"/> twice on a throwaway
/// clip: two generations, both bypass, no hit.</item>
/// <item><c>repeat-hit</c> — the plugin's normal path (<see cref="IBackendService.GenerateVoice"/>, no
/// <c>NoCache</c>) twice: one generation, the second answer is a hit with first bytes at once.</item>
/// </list>
/// Never on the real config; texts carry the run id so a cache entry of an earlier run never answers.
/// </summary>
public sealed partial class WrapperHttpStage : E2eStage
{
    public const string StageId = "wrapper-http";
    public const string PartPrefix = StageId + ":";

    /// <summary>The second, cached answer must start within this (plan: "≈ 0").</summary>
    internal const long RepeatHitMaxFirstByteMs = 300;

    /// <summary>Below this mean GPU load while generating, a drop after the cancel is not judged (too little signal).</summary>
    internal const double GpuJudgeMinPercent = 20;

    private readonly Configuration _config;
    private readonly IE2eWrapperHttp _http;
    private readonly Func<string, IE2eWrapperLog> _logFor;
    private IE2eWrapperLog _log = null!;
    private readonly INpcDataService _npcData;
    private readonly IBackendService _backend;
    private readonly IVoiceClipManagerService _vcm;
    private readonly IDatabaseService _db;
    private readonly ILogService _logService;
    private readonly Func<ClientLanguage> _language;
    private readonly IE2eGpuProbe? _gpu;
    private readonly TimeSpan _gpuInterval;
    private readonly WrapperHttpTimings _t;

    public WrapperHttpStage(Configuration config, IE2eWrapperHttp http, Func<string, IE2eWrapperLog> wrapperLog, INpcDataService npcData,
        IBackendService backend, IVoiceClipManagerService vcm, IDatabaseService db, ILogService logService,
        Func<ClientLanguage> language, IE2eGpuProbe? gpu = null, TimeSpan? gpuInterval = null, WrapperHttpTimings? timings = null)
    {
        _config = config;
        _http = http;
        _logFor = wrapperLog;
        _npcData = npcData;
        _backend = backend;
        _vcm = vcm;
        _db = db;
        _logService = logService;
        _language = language;
        _gpu = gpu;
        _gpuInterval = gpuInterval ?? TimeSpan.FromSeconds(1);
        _t = timings ?? WrapperHttpTimings.Default;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [FirstTimeStage.StageId];
    public override string? UnmetNeed(E2eState state) => FirstUnmet(state, Needs) ?? EngineStage.ActiveEngineUnmet(state);
    public override TimeSpan Timeout => PartTimeout * 3 + CancelTimeout + TimeSpan.FromSeconds(30);

    internal static TimeSpan PartTimeout => TimeSpan.FromMinutes(3);

    /// <summary>The cancel part waits for one clip to run to its end (up to <see cref="WrapperHttpTimings.RunToEndWait"/>).</summary>
    internal TimeSpan CancelTimeout => PartTimeout + _t.RunToEndWait;

    private delegate Task<SelfTestResult> Part(E2eContext ctx, EchokrautVoice voice, CancellationToken ct);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");
        if (LiveGeneration.SkipIfNone(_config) is { } none) return none;
        if (_config.BackendSelection != TTSBackends.EchokrauTTS)
            return SelfTestResult.Skip($"Active engine is {_config.BackendSelection}, not EchokrauTTS.");
        _log = _logFor(ctx.Request.InstallRoot);
        var health = await _http.HealthAsync(ct).ConfigureAwait(false);
        if (health == null) return SelfTestResult.Fail("The wrapper's /health did not answer.");
        if (health.Cache == null) return SelfTestResult.Skip("Wrapper without response cache in /health (before 0.0.2.0).");
        if (!health.Cache.Enabled) return SelfTestResult.Skip("Response cache is switched off on this wrapper.");
        if (LiveGeneration.PickTestVoice(_npcData) is not { } voice) return SelfTestResult.Fail("No enabled voice to generate with.");

        var parts = new (string Id, Part Run, TimeSpan Timeout)[]
        {
            ("cache", CacheAsync, PartTimeout),
            ("cancel", (c, v, t) => CancelAsync(c, v, health.Device, t), CancelTimeout),
            ("vcm-regenerate", VcmRegenerateAsync, PartTimeout),
            ("repeat-hit", RepeatHitAsync, PartTimeout),
        };
        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        foreach (var (name, run, timeout) in parts)
        {
            var id = PartPrefix + name;
            if (ctx.State.ResultOf(id) is { } done)
            {
                outcomes.Add((id, done.Outcome));
                continue;
            }
            var (result, ms) = await SelfTestRunner.RunWithTimeoutAsync(c => run(ctx, voice, c), timeout, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            ctx.ReportPart(new E2eStageReport(id, result.Outcome, result.Detail, ms));
            outcomes.Add((id, result.Outcome));
        }

        var (summary, failed, passed) = SummarizeParts(outcomes);
        var on = $"{_config.EchokrauTts.TtsBackendArg} on {health.Device ?? "?"}";
        if (failed.Count > 0) return SelfTestResult.Fail($"{summary} ({on}): {string.Join(", ", failed)}.");
        return passed > 0 ? SelfTestResult.Pass($"{summary} ({on}).") : SelfTestResult.Skip($"{summary} ({on}).");
    }

    /// <summary>A text no earlier run cached: the test sentence plus the run id and a tag (sentences are not played).</summary>
    internal string Text(E2eContext ctx, string tag, int sentences = 1)
    {
        var baseText = Constants.TestMessage(_language());
        return string.Join(" ", Enumerable.Range(1, sentences).Select(i => $"{baseText} {ctx.Request.Id} {tag} {i}."));
    }

    // ------------------------------------------------------------------ cache

    private async Task<SelfTestResult> CacheAsync(E2eContext ctx, EchokrautVoice voice, CancellationToken ct)
    {
        var sample = voice.BackendVoice;
        var before = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache;
        var mark = _log.Mark();
        var problems = new List<string>();
        var steps = new List<string>();

        var t1 = Text(ctx, "cache");
        var (miss, missHash) = await CallAsync(sample, t1, false, ct).ConfigureAwait(false);
        await Expect("first", miss, "miss", 1);
        var (hit, hitHash) = await CallAsync(sample, t1, false, ct).ConfigureAwait(false);
        await Expect("repeat", hit, "hit", 1);
        if (missHash != null && hitHash != null && missHash != hitHash) problems.Add("the hit answered other bytes than the miss");
        var (bypass, bypassHash) = await CallAsync(sample, t1, true, ct).ConfigureAwait(false);
        await Expect("no_cache", bypass, "bypass", 2);
        var (hit2, hit2Hash) = await CallAsync(sample, t1, false, ct).ConfigureAwait(false);
        await Expect("after bypass", hit2, "hit", 2);
        if (bypassHash != null && hit2Hash != null && bypassHash != hit2Hash)
            problems.Add("the hit after the bypass did not answer the bypass run's bytes (entry not replaced)");

        // Two at once: the second joins the first while it still generates (three sentences keep it running).
        var t2 = Text(ctx, "shared", 3);
        using (var first = await _http.StartTtsAsync(sample, t2, false, ct).ConfigureAwait(false))
        using (var second = await _http.StartTtsAsync(sample, t2, false, ct).ConfigureAwait(false))
        {
            if (first == null || second == null) problems.Add("the shared pair was refused");
            else
            {
                var hashes = await Task.WhenAll(first.ReadToEndAsync(ct), second.ReadToEndAsync(ct)).ConfigureAwait(false);
                await Expect("pair 1", first, "miss", 3);
                await Expect("pair 2", second, "shared", 3);
                if (hashes[0] != hashes[1]) problems.Add("the shared client got other bytes than the first");
            }
        }

        var after = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache;
        if (before != null && after != null && JudgeCacheDelta(after.Minus(before), hits: 2, shared: 1, misses: 2, bypassed: 1) is { } delta)
            problems.Add(delta);
        if (after == null) problems.Add("/health response_cache missing after the calls");
        var page = await _http.PageAsync(ct).ConfigureAwait(false);
        var pageNote = page == null ? "test page not served" : page.Contains("no_cache", StringComparison.Ordinal) ? "test page sends no_cache" : null;
        if (pageNote == null) problems.Add("the built-in test page does not send no_cache");

        var detail = $"{string.Join(", ", steps)}; /health {Delta(before, after)}; {pageNote ?? "page without no_cache"}";
        return Verdict(problems, detail);

        // X-Cache of one call and the generations started so far (cumulative since the mark).
        async Task Expect(string step, IE2eTtsCall? call, string cache, int startsSoFar)
        {
            if (call == null)
            {
                problems.Add($"{step}: /tts refused");
                return;
            }
            var starts = await StartsAsync(mark, startsSoFar, ct).ConfigureAwait(false);
            steps.Add($"{step} {call.Cache} ({call.BytesRead} B, starts {starts})");
            if (call.Cache != cache) problems.Add($"{step}: X-Cache {Show(call.Cache)}, expected {cache}");
            if (starts != startsSoFar) problems.Add($"{step}: {starts} tts request start line(s) so far, expected {startsSoFar}");
            if (call.BytesRead == 0) problems.Add($"{step}: empty answer");
        }
    }

    /// <summary>One whole call; the call object stays readable for its header values.</summary>
    private async Task<(IE2eTtsCall? Call, string? Hash)> CallAsync(string sample, string text, bool noCache, CancellationToken ct)
    {
        var call = await _http.StartTtsAsync(sample, text, noCache, ct).ConfigureAwait(false);
        if (call == null) return (null, null);
        try { return (call, await call.ReadToEndAsync(ct).ConfigureAwait(false)); }
        finally { call.Dispose(); }
    }

    /// <summary>
    /// <c>tts request start</c> lines since <paramref name="mark"/>: polls until <paramref name="expected"/> are there
    /// or <see cref="WrapperHttpTimings.LogSettle"/> passed, then gives the wrapper one more poll to log a surplus one.
    /// </summary>
    private async Task<int> StartsAsync(long mark, int expected, CancellationToken ct)
    {
        var until = DateTime.UtcNow + _t.LogSettle;
        int n;
        while ((n = E2eInstallerLog.TtsStarts(_log.Since(mark))) < expected && DateTime.UtcNow < until)
            await Task.Delay(_t.Poll, ct).ConfigureAwait(false);
        await Task.Delay(_t.Poll, ct).ConfigureAwait(false);
        return E2eInstallerLog.TtsStarts(_log.Since(mark));
    }

    /// <summary>"…" naming every counter that moved other than expected; null when all moved exactly so.</summary>
    internal static string? JudgeCacheDelta(E2eCacheStats delta, int hits, int shared, int misses, int bypassed)
    {
        var wrong = new List<string>();
        if (delta.Hits != hits) wrong.Add($"hits +{delta.Hits} (expected +{hits})");
        if (delta.Shared != shared) wrong.Add($"shared +{delta.Shared} (expected +{shared})");
        if (delta.Misses != misses) wrong.Add($"misses +{delta.Misses} (expected +{misses})");
        if (delta.Bypassed != bypassed) wrong.Add($"bypassed +{delta.Bypassed} (expected +{bypassed})");
        return wrong.Count == 0 ? null : "/health response_cache: " + string.Join(", ", wrong);
    }

    private static string Delta(E2eCacheStats? before, E2eCacheStats? after) =>
        before != null && after != null ? after.Minus(before).ToString() : "?";

    /// <summary>Fail with every problem in front of the detail, else pass with the detail.</summary>
    private static SelfTestResult Verdict(IReadOnlyCollection<string> problems, string detail) =>
        problems.Count > 0 ? SelfTestResult.Fail($"{string.Join("; ", problems)} | {detail}") : SelfTestResult.Pass(detail);

    private static string Show(string header) => header.Length == 0 ? "(missing)" : header;

    // ------------------------------------------------------------ repeat-hit

    private async Task<SelfTestResult> RepeatHitAsync(E2eContext ctx, EchokrautVoice voice, CancellationToken ct)
    {
        var text = Text(ctx, "repeat");
        var before = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache;
        var mark = _log.Mark();
        var first = await PluginLineAsync(voice, text, ct).ConfigureAwait(false);
        var second = await PluginLineAsync(voice, text, ct).ConfigureAwait(false);
        var starts = await StartsAsync(mark, 1, ct).ConfigureAwait(false);
        var after = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache;

        if (first == null || second == null) return SelfTestResult.Fail("The engine returned no audio for the plugin line (see log).");
        var detail = $"first bytes {first.Value.FirstByteMs} ms then {second.Value.FirstByteMs} ms ({first.Value.Bytes}/{second.Value.Bytes} B), " +
                     $"starts {starts}, /health {Delta(before, after)}";
        var problems = JudgeRepeat(first.Value.FirstByteMs, second.Value.FirstByteMs, starts).ToList();
        if (before != null && after != null && JudgeCacheDelta(after.Minus(before), hits: 1, shared: 0, misses: 1, bypassed: 0) is { } delta)
            problems.Add(delta);
        return Verdict(problems, detail);
    }

    /// <summary>Test plan R2: the repeated line is one generation, its first bytes come at once and before the first line's.</summary>
    internal static IEnumerable<string> JudgeRepeat(long firstMs, long secondMs, int starts)
    {
        if (starts != 1) yield return $"{starts} tts request start line(s) for one text twice, expected 1";
        if (secondMs > RepeatHitMaxFirstByteMs) yield return $"repeated line's first bytes after {secondMs} ms (> {RepeatHitMaxFirstByteMs} ms)";
        else if (secondMs >= firstMs && firstMs > RepeatHitMaxFirstByteMs) yield return $"repeated line not faster ({secondMs} ms vs {firstMs} ms)";
    }

    /// <summary>
    /// A line through the plugin's normal generation path (like a dialogue line: no <c>NoCache</c>), read to the end so
    /// the wrapper can store the finished clip; first-byte time from the call on.
    /// </summary>
    private async Task<(long FirstByteMs, long Bytes)?> PluginLineAsync(EchokrautVoice voice, string text, CancellationToken ct)
    {
        var eventId = LiveGeneration.NewEventId(_logService, nameof(WrapperHttpStage));
        var message = new VoiceMessage
        {
            Source = Echotools.Logging.Enums.TextSource.VoiceTest,
            Speaker = new NpcMapData(ObjectKind.None) { Name = voice.VoiceName, Voices = _npcData.GetEchokrautVoices(), Voice = voice },
            Text = text,
            OriginalText = text,
            Language = _language(),
            EventId = eventId,
        };
        var started = Environment.TickCount64;
        try
        {
            if (!await _backend.GenerateVoice(message).ConfigureAwait(false) || message.Stream == null) return null;
            var buffer = new byte[16 * 1024];
            long total = 0, firstMs = -1;
            int n;
            while ((n = await message.Stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (firstMs < 0) firstMs = Environment.TickCount64 - started;
                total += n;
            }
            return total == 0 ? null : (firstMs, total);
        }
        finally
        {
            message.Stream?.Dispose();
            _logService.End(nameof(WrapperHttpStage), eventId);
        }
    }
}
