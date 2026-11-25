using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;

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
public sealed class EngineStage : E2eStage
{
    public const string StageId = "engine";
    public const string PartPrefix = StageId + ":";

    /// <summary>The engine that was last seen ready (passed engine or the switch back after the loop).</summary>
    public const string ActiveEngineMark = "engine-active";

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

    public EngineStage(IEchokrauTtsInstanceService engine, Configuration config, E2eEngineWait wait,
        SelfTestCheck? generation, SelfTestCheck? pipeline, Func<long?> systemDriveFree,
        TimeSpan? readyWait = null, TimeSpan? voicesWait = null)
    {
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

    // Every engine of decision D1 plus the switch back after the loop.
    public override TimeSpan Timeout => PerEngine * (E2eRequest.AllEngines.Count + 1);

    public static string PartId(string engineId) => PartPrefix + engineId.Trim().ToLowerInvariant();

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");
        if (_generation == null || _pipeline == null)
            return SelfTestResult.Fail("The generation or pipeline check is missing from the self-test catalog.");

        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
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
        }

        var back = await SwitchBackAsync(ctx, ct).ConfigureAwait(false);

        var (summary, failed, passed) = SummarizeParts(outcomes);
        if (back != null) return SelfTestResult.Fail($"{summary}; {back}");
        if (failed.Count > 0) return SelfTestResult.Fail($"{summary}: {string.Join(", ", failed)}.");
        return passed > 0
            ? SelfTestResult.Pass($"{summary}; active: {ctx.State.Marks.GetValueOrDefault(ActiveEngineMark)}.")
            : SelfTestResult.Skip(summary + ".");
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
        if (await ActivateAsync(ctx, id, ct).ConfigureAwait(false) is { } error) return SelfTestResult.Fail(error);

        var voices = await _wait.WaitForVoicesAsync(_voicesWait, ct).ConfigureAwait(false);
        if (voices == 0) return SelfTestResult.Skip($"No enabled voice within {_voicesWait.TotalSeconds:0} s after RefreshBackend.");
        ctx.Progress($"{id}: {voices} enabled voice(s)");

        var gen = await SelfTestRunner.RunCheckAsync(_generation!, ct).ConfigureAwait(false);
        ctx.Progress($"{id}: generation {E2eLogFormat.Kind(gen.Outcome)} ({gen.DurationMs} ms) {gen.Detail}");
        var pipe = await SelfTestRunner.RunCheckAsync(_pipeline!, ct).ConfigureAwait(false);
        ctx.Progress($"{id}: pipeline {E2eLogFormat.Kind(pipe.Outcome)} ({pipe.DurationMs} ms) {pipe.Detail}");

        var detail = $"generation {E2eLogFormat.Kind(gen.Outcome)} {gen.DurationMs} ms: {gen.Detail} | " +
                     $"pipeline {E2eLogFormat.Kind(pipe.Outcome)}: {pipe.Detail}";
        if (gen.Outcome == SelfTestOutcome.Fail || pipe.Outcome == SelfTestOutcome.Fail) return SelfTestResult.Fail(detail);
        if (gen.Outcome != SelfTestOutcome.Pass || pipe.Outcome != SelfTestOutcome.Pass) return SelfTestResult.Skip(detail);
        return SelfTestResult.Pass(detail);
    }

    /// <summary>Switch (restarts a running engine), start if nothing runs, wait ready; null on ready.</summary>
    private async Task<string?> ActivateAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        // Both block (stop + HTTP); we are on the thread pool.
        _engine.SwitchTtsBackend(id);
        if (!string.Equals(_config.EchokrauTts.TtsBackendArg, id, StringComparison.Ordinal))
            return $"Engine is {_config.EchokrauTts.TtsBackendArg} after the switch to {id}.";
        if (!_engine.InstanceRunning && !_engine.InstanceStarting && !_engine.Installing)
            _engine.StartInstance();

        var error = await _wait.WaitForReadyAsync(ctx, id, _readyWait, ct).ConfigureAwait(false);
        if (error != null) return error;
        ctx.State.Marks[ActiveEngineMark] = id;
        ctx.Save();
        return null;
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

    /// <summary>The id of the last <c>engine:*</c> part that passed, in report order; null when none did.</summary>
    public static string? LastPassed(E2eState state) =>
        state.StageResults.LastOrDefault(r => r.Outcome == SelfTestOutcome.Pass
            && r.Id.StartsWith(PartPrefix, StringComparison.Ordinal))?.Id;
}
