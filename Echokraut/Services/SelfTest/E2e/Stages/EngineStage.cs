using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 5 (plan › Stages): the engine loop. For every requested sub-engine, judged at run time against
/// <see cref="IEchokrauTtsInstanceService.AvailableEngines"/> and the installed wrapper version [M10]:
/// <c>SwitchTtsBackend</c>, start if not running, wait ready (model download), voices, then the generation
/// and pipeline checks. Each engine is reported on its own as <c>engine:&lt;id&gt;</c>. At the end the last
/// engine that passed is switched back on, so <c>scenarios</c> runs on a working one [M4]; the id goes into
/// <see cref="ActiveEngineMark"/> only once it is ready. Never on the real config: it would change the
/// user's engine choice.
/// </summary>
public sealed partial class EngineStage : E2eStage
{
    public const string StageId = "engine";
    public const string PartPrefix = StageId + ":";

    /// <summary>The engine that was last seen ready (passed engine or the switch back after the loop).</summary>
    public const string ActiveEngineMark = "engine-active";

    /// <summary>The engine the stage found configured (the one <c>first-time</c> installed).</summary>
    public const string InitialEngineMark = "engine-initial";

    internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(30);

    internal static readonly TimeSpan ReadyWait = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan VoicesWait = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Below this the loop stops switching: uv keeps its package cache on the system drive
    /// (<c>%LOCALAPPDATA%\uv\cache</c>), only models go under the install root (build step 5 result).
    /// </summary>
    internal const long MinSystemDriveFreeBytes = 5L * 1024 * 1024 * 1024;

    private readonly IEchokrauTtsInstanceService _engine;
    private readonly Configuration _config;
    private readonly E2eEngineWait _wait;
    private readonly SelfTestCheck? _generation;
    private readonly SelfTestCheck? _pipeline;
    private readonly Func<long?> _systemDriveFree;
    private readonly TimeSpan _readyWait;
    private readonly TimeSpan _voicesWait;
    private readonly E2eTelemetryProbes? _probes;
    private readonly Func<E2eContext, string, CancellationToken, Task<SelfTestResult>>? _gameVoice;

    /// <param name="gameVoice">B6: the game-voice part per passed engine (<see cref="GameVoiceStage.RunOnEngineAsync"/>),
    /// run only when the stage <c>game-voice</c> belongs to the run; null = none.</param>
    public EngineStage(IEchokrauTtsInstanceService engine, Configuration config, E2eEngineWait wait,
        SelfTestCheck? generation, SelfTestCheck? pipeline, Func<long?> systemDriveFree,
        TimeSpan? readyWait = null, TimeSpan? voicesWait = null, E2eTelemetryProbes? probes = null,
        Func<E2eContext, string, CancellationToken, Task<SelfTestResult>>? gameVoice = null)
    {
        _probes = probes;
        _gameVoice = gameVoice;
        _engine = engine;
        _config = config;
        _wait = wait;
        _generation = generation;
        _pipeline = pipeline;
        _systemDriveFree = systemDriveFree;
        _readyWait = readyWait ?? ReadyWait;
        _voicesWait = voicesWait ?? VoicesWait;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [FirstTimeStage.StageId];

    /// <summary>One engine: ready wait + voices + both checks + a minute of slack.</summary>
    internal TimeSpan PerEngine => _readyWait + _voicesWait
        + (_generation?.Timeout ?? TimeSpan.Zero) + (_pipeline?.Timeout ?? TimeSpan.Zero) + TimeSpan.FromMinutes(1);

    // Every engine of decision D1, the switch back after the loop and the option restarts (B3).
    public override TimeSpan Timeout => PerEngine * (E2eRequest.AllEngines.Count + 1) + OptionsTimeout * 2
        + (_gameVoice == null ? TimeSpan.Zero : E2eGameVoiceGeneration.Timeout * E2eRequest.AllEngines.Count);

    public static string PartId(string engineId) => PartPrefix + engineId.Trim().ToLowerInvariant();

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");
        if (_generation == null || _pipeline == null)
            return SelfTestResult.Fail("The generation or pipeline check is missing from the self-test catalog.");

        ctx.State.Marks.TryAdd(InitialEngineMark, _config.EchokrauTts.TtsBackendArg);
        if (_probes != null && ctx.State.Telemetry.VramBaselineMiB == null)
            await BaselineAsync(ctx, _probes, ct).ConfigureAwait(false);

        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        RestoreInterruptedOptions(ctx);
        CheckVoicePack(ctx, outcomes);
        string? lowDisk = null;
        foreach (var requested in ctx.Request.Engines)
        {
            var id = PartId(requested);
            if (ctx.State.ResultOf(id) is { } done)
            {
                outcomes.Add((id, done.Outcome));
                continue;
            }

            SelfTestResult result;
            long ms = 0;
            if (lowDisk != null)
                result = SelfTestResult.Skip(lowDisk);
            else if (Unavailable(requested) is { } reason)
                result = SelfTestResult.Skip(reason);
            else if (LowSystemDrive() is { } low)
            {
                lowDisk = low;
                result = SelfTestResult.Skip(low);
            }
            else
            {
                (result, ms) = await SelfTestRunner.RunWithTimeoutAsync(
                    c => RunEngineAsync(ctx, requested.Trim().ToLowerInvariant(), c), PerEngine, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            ctx.ReportPart(new E2eStageReport(id, result.Outcome, result.Detail, ms));
            outcomes.Add((id, result.Outcome));
            if (result.Outcome == SelfTestOutcome.Pass
                && await RunOptionsAsync(ctx, requested.Trim().ToLowerInvariant(), ct).ConfigureAwait(false) is { } option)
                outcomes.Add((option.Id, option.Result.Outcome));
            if (result.Outcome == SelfTestOutcome.Pass
                && await RunGameVoiceAsync(ctx, requested.Trim().ToLowerInvariant(), ct).ConfigureAwait(false) is { } voice)
                outcomes.Add(voice);
        }

        var back = await SwitchBackAsync(ctx, ct).ConfigureAwait(false);
        CheckInstallerLog(ctx, outcomes);

        var (summary, failed, _) = SummarizeParts(outcomes);
        if (back != null) return SelfTestResult.Fail($"{summary}; {back}");
        if (failed.Count > 0) return SelfTestResult.Fail($"{summary}: {string.Join(", ", failed)}.");
        // The file parts alone do not make the stage pass: it is about engines.
        return outcomes.Any(o => IsPassedEngine(o.Id, o.Outcome))
            ? SelfTestResult.Pass($"{summary}; active: {ctx.State.Marks.GetValueOrDefault(ActiveEngineMark)}.")
            : SelfTestResult.Skip(summary + ".");
    }

    /// <summary>B6: <c>game-voice:engine-&lt;id&gt;</c> while this engine runs; null when the run has no game-voice stage.</summary>
    private async Task<(string Id, SelfTestOutcome Outcome)?> RunGameVoiceAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        if (_gameVoice == null || !GameVoiceStage.InRun(ctx.Request)) return null;
        var part = GameVoiceStage.EnginePartId(id);
        if (ctx.State.ResultOf(part) is { } done) return (part, done.Outcome);
        var (result, ms) = await SelfTestRunner.RunWithTimeoutAsync(c => _gameVoice(ctx, id, c), E2eGameVoiceGeneration.Timeout, ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        ctx.ReportPart(new E2eStageReport(part, result.Outcome, result.Detail, ms));
        return (part, result.Outcome);
    }

    /// <summary>Why a requested engine can not run on this install [M10]; null when it can.</summary>
    internal string? Unavailable(string requested)
    {
        var info = _engine.AvailableEngines.FirstOrDefault(e =>
            string.Equals(e.Id?.Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase));
        if (info == null)
            return $"Not in the engine catalog ({string.Join(", ", _engine.AvailableEngines.Select(e => e.Id))}).";
        var installed = _config.EchokrauTts.InstalledWrapperVersion;
        if (!WrapperUpdatePolicy.IsAtLeast(installed, info.MinVersion))
            return $"Needs wrapper {info.MinVersion}, installed {installed}.";
        return null;
    }

    private string? LowSystemDrive()
    {
        var free = _systemDriveFree();
        return free is { } f && f < MinSystemDriveFreeBytes
            ? $"System drive has only {f / (1024.0 * 1024 * 1024):0.0} GB free (uv package cache lives there), no further engine switch."
            : null;
    }

    private async Task<SelfTestResult> RunEngineAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        var free = _systemDriveFree();
        ctx.Progress($"{id}: switching from {_config.EchokrauTts.TtsBackendArg}" +
                     (free is { } f ? $", system drive {f / (1024.0 * 1024 * 1024):0.0} GB free" : ""));
        var root = ctx.Request.InstallRoot;
        var tel = new E2eEngineTelemetry { Engine = id };
        if (_probes != null) tel.F5InClassicVenvBefore = _probes.Footprint.F5InClassicVenv(root);

        if (await ActivateAsync(ctx, id, ct).ConfigureAwait(false) is { } error)
        {
            Record(ctx, tel);
            return SelfTestResult.Fail(error);
        }

        if (_probes != null) await MeasureIdleAsync(_probes, tel, ct).ConfigureAwait(false);

        var voices = await _wait.WaitForVoicesAsync(_voicesWait, ct).ConfigureAwait(false);
        if (voices == 0) return SelfTestResult.Skip($"No enabled voice within {_voicesWait.TotalSeconds:0} s after RefreshBackend.");
        ctx.Progress($"{id}: {voices} enabled voice(s)");

        var frames = _probes?.Frames.Begin();
        var gpu = _probes == null ? null : E2eGpuSampler.Start(_probes.Gpu, _probes.GpuInterval, ct);
        var gen = await SelfTestRunner.RunCheckAsync(_generation!, ct).ConfigureAwait(false);
        ctx.Progress($"{id}: generation {E2eLogFormat.Kind(gen.Outcome)} ({gen.DurationMs} ms) {gen.Detail}");
        var underrunsBefore = _probes?.Underruns();
        var pipe = await SelfTestRunner.RunCheckAsync(_pipeline!, ct).ConfigureAwait(false);
        ctx.Progress($"{id}: pipeline {E2eLogFormat.Kind(pipe.Outcome)} ({pipe.DurationMs} ms) {pipe.Detail}");

        var problems = new List<string>();
        if (_probes != null)
        {
            tel.Underruns = _probes.Underruns() - underrunsBefore;
            tel.GenerationFrames = frames!.Stop();
            tel.GenerationGpu = await gpu!.StopAsync().ConfigureAwait(false);
            if (tel.GenerationGpu != null && ctx.State.Telemetry.VramBaselineMiB is { } baseline)
                tel.VramOverBaselineMiB = tel.GenerationGpu.VramPeakMiB - baseline;
            tel.InstallRootBytes = _probes.Footprint.SizeBytes(root);
            tel.Venvs = _probes.Footprint.Venvs(root).ToList();
            Record(ctx, tel);
            ctx.Progress($"{id}: telemetry {tel.Summary()}");
            problems.AddRange(Judge(ctx.State, id, tel));
        }

        var detail = $"generation {E2eLogFormat.Kind(gen.Outcome)} {gen.DurationMs} ms: {gen.Detail} | " +
                     $"pipeline {E2eLogFormat.Kind(pipe.Outcome)}: {pipe.Detail}";
        if (problems.Count > 0) return SelfTestResult.Fail($"{string.Join("; ", problems)} | {detail}");
        if (gen.Outcome == SelfTestOutcome.Fail || pipe.Outcome == SelfTestOutcome.Fail) return SelfTestResult.Fail(detail);
        if (gen.Outcome != SelfTestOutcome.Pass || pipe.Outcome != SelfTestOutcome.Pass) return SelfTestResult.Skip(detail);
        return SelfTestResult.Pass(detail);
    }

    /// <summary>
    /// What fails an engine on top of its checks: an operation of this engine froze the game (a frame above
    /// <see cref="E2eTelemetry.FreezeLimitMs"/>), or <c>f5_tts</c> was in the classic venv before
    /// <c>f5</c> was asked for (lazy install broken, R1) while the run did not start on f5.
    /// </summary>
    internal static IEnumerable<string> Judge(E2eState state, string id, E2eEngineTelemetry tel)
    {
        foreach (var op in state.Telemetry.Freezes().Where(o => o.Op.EndsWith(":" + id, StringComparison.Ordinal)))
            yield return $"{op.Op} froze the game ({op.Frames.MaxMs:0} ms frame > {state.Telemetry.FreezeLimitMs:0} ms)";
        if (id == "f5" && tel.F5InClassicVenvBefore == true
            && state.Marks.GetValueOrDefault(InitialEngineMark) != "f5")
            yield return "f5_tts was in .venv before f5 was switched on (lazy install)";
    }

    /// <summary>Health, then a few idle seconds of frames and GPU with the engine loaded.</summary>
    private static async Task MeasureIdleAsync(E2eTelemetryProbes probes, E2eEngineTelemetry tel, CancellationToken ct)
    {
        var health = await probes.Health(ct).ConfigureAwait(false);
        tel.HealthDevice = health?.device;
        tel.HealthBackend = health?.backend;
        var w = probes.Frames.Begin();
        var gpu = E2eGpuSampler.Start(probes.Gpu, probes.GpuInterval, ct);
        try { await Task.Delay(probes.IdleWindow, ct).ConfigureAwait(false); }
        finally
        {
            tel.IdleFrames = w.Stop();
            tel.IdleGpu = await gpu.StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>One entry per engine: a rerun after a reload replaces the earlier one.</summary>
    private static void Record(E2eContext ctx, E2eEngineTelemetry tel)
    {
        ctx.State.Telemetry.Engines.RemoveAll(e => e.Engine == tel.Engine);
        ctx.State.Telemetry.Engines.Add(tel);
        ctx.Save();
    }

    /// <summary>
    /// Stops the engine <c>first-time</c> left running and reads the VRAM of the game alone. Measured as op
    /// <c>stop</c> (freeze watchdog); the first engine of the loop then goes through <c>StartInstance</c>,
    /// every later one through the restart in <c>SwitchTtsBackend</c>.
    /// </summary>
    private async Task BaselineAsync(E2eContext ctx, E2eTelemetryProbes probes, CancellationToken ct)
    {
        if (_engine.InstanceRunning || _engine.InstanceStarting)
        {
            var op = await probes.MeasureAsync("stop", () =>
            {
                _engine.StopInstance(new EKEventId(0, TextSource.None));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            ctx.State.Telemetry.Ops.Add(op);
            var deadline = DateTime.UtcNow + StopWait;
            while ((_engine.InstanceRunning || _engine.InstanceStopping) && DateTime.UtcNow < deadline)
                await Task.Delay(_wait.PollInterval, ct).ConfigureAwait(false);
            // The driver frees the memory of the ended process a moment later.
            await Task.Delay(probes.GpuInterval, ct).ConfigureAwait(false);
        }
        var samples = new List<E2eGpuSample>();
        for (var i = 0; i < 3; i++)
        {
            if (await probes.Gpu.SampleAsync(ct).ConfigureAwait(false) is { } s) samples.Add(s);
            if (i < 2) await Task.Delay(probes.GpuInterval, ct).ConfigureAwait(false);
        }
        ctx.State.Telemetry.VramBaselineMiB = samples.Count > 0 ? samples.Min(s => s.VramUsedMiB) : null;
        ctx.Save();
        ctx.Progress($"baseline without engine: {(samples.Count > 0 ? $"VRAM {samples.Min(s => s.VramUsedMiB)} MiB of {samples[0].VramTotalMiB} MiB" : "no GPU reading")}" +
                     (ctx.State.Telemetry.Ops.LastOrDefault(o => o.Op == "stop") is { } stop ? $", stop {stop.DurationMs} ms ({stop.Frames})" : ""));
    }

    /// <summary>Switch (restarts a running engine), start if nothing runs, wait ready; null on ready.</summary>
    private async Task<string?> ActivateAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        // Both block (stop + HTTP); we are on the thread pool.
        await MeasuredAsync(ctx, "switch:" + id, () => _engine.SwitchTtsBackend(id)).ConfigureAwait(false);
        if (!string.Equals(_config.EchokrauTts.TtsBackendArg, id, StringComparison.Ordinal))
            return $"Engine is {_config.EchokrauTts.TtsBackendArg} after the switch to {id}.";
        if (!_engine.InstanceRunning && !_engine.InstanceStarting && !_engine.Installing)
            await MeasuredAsync(ctx, "start:" + id, _engine.StartInstance).ConfigureAwait(false);

        // The wait covers the lazy install and the model download: the game must keep running meanwhile.
        string? error = null;
        await MeasuredAsync(ctx, "ready:" + id, async () =>
            error = await _wait.WaitForReadyAsync(ctx, id, _readyWait, ct).ConfigureAwait(false)).ConfigureAwait(false);
        if (error != null) return error;
        ctx.State.Marks[ActiveEngineMark] = id;
        ctx.Save();
        return null;
    }

    /// <summary>Runs a blocking engine operation; with telemetry its frames land in <see cref="E2eTelemetry.Ops"/>.</summary>
    private async Task MeasuredAsync(E2eContext ctx, string name, Action op) =>
        await MeasuredAsync(ctx, name, () => { op(); return Task.CompletedTask; }).ConfigureAwait(false);

    private async Task MeasuredAsync(E2eContext ctx, string name, Func<Task> op)
    {
        if (_probes == null)
        {
            await op().ConfigureAwait(false);
            return;
        }
        ctx.State.Telemetry.Ops.Add(await _probes.MeasureAsync(name, op).ConfigureAwait(false));
    }

    /// <summary>
    /// The last passed engine back on when the loop ended on another one (a failed or skipped engine);
    /// null when nothing had to be done or it worked.
    /// </summary>
    private async Task<string?> SwitchBackAsync(E2eContext ctx, CancellationToken ct)
    {
        var last = LastPassed(ctx.State);
        if (last == null) return null;
        var id = last.Substring(PartPrefix.Length);
        if (ctx.State.Marks.GetValueOrDefault(ActiveEngineMark) == id
            && string.Equals(_config.EchokrauTts.TtsBackendArg, id, StringComparison.Ordinal)
            && _engine.InstanceRunning)
            return null;

        ctx.Progress($"switching back to {id} (last passed engine)");
        ctx.State.Marks.Remove(ActiveEngineMark);
        ctx.Save();
        var (result, _) = await SelfTestRunner.RunWithTimeoutAsync(async c =>
        {
            var error = await ActivateAsync(ctx, id, c).ConfigureAwait(false);
            return error == null ? SelfTestResult.Pass("") : SelfTestResult.Fail(error);
        }, PerEngine, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return result.Outcome == SelfTestOutcome.Pass ? null : $"switch back to {id} failed: {result.Detail}";
    }

    /// <summary>
    /// For a stage that runs on the engine this stage left on (scenarios, wrapper-http) [M4]: with <c>engine</c> in
    /// the run, "needs …" unless the last passed <c>engine:*</c> part is active again; null when it is, or when the
    /// run has no engine stage (then the engine <c>first-time</c> left running counts).
    /// </summary>
    public static string? ActiveEngineUnmet(E2eState state)
    {
        if (!state.Request.Includes(StageId)) return null;
        if (LastPassed(state) is not { } last) return $"needs a passed {PartPrefix}* (none passed)";
        var id = last.Substring(PartPrefix.Length);
        return state.Marks.GetValueOrDefault(ActiveEngineMark) == id
            ? null
            : $"needs {last} active again (switch back failed)";
    }

    /// <summary>The id of the last <c>engine:*</c> part that passed, in report order; null when none did.</summary>
    public static string? LastPassed(E2eState state) =>
        state.StageResults.LastOrDefault(r => IsPassedEngine(r.Id, r.Outcome))?.Id;

    private static bool IsPassedEngine(string partId, SelfTestOutcome outcome) =>
        outcome == SelfTestOutcome.Pass && partId.StartsWith(PartPrefix, StringComparison.Ordinal);
}
