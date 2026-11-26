using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Unattended in-game E2E run (plan <c>docs/plans/ingame-e2e.md</c>). Started by a request file the
/// Mac uploads over ssh, followed through <c>EKTEST</c> log lines, survives plugin reloads through
/// <c>state.json</c>. Dev loads only: an installed plugin never acts on the files.
/// </summary>
public interface IE2eRunService : IDisposable
{
    bool IsRunning { get; }

    /// <summary>Set by backup phase A: the plugin's Dispose must not save the config before phase B.</summary>
    bool SuppressSaveOnDispose { get; }

    /// <summary>Once per plugin load, after startup: resumes an unfinished run (or closes a stale one).</summary>
    void Start();

    /// <summary>Framework tick: feeds the frame monitor, checks for a request file every <see cref="E2eRunService.PollInterval"/>.</summary>
    void Tick();
}

public sealed class E2eRunService : IE2eRunService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    /// <summary>A run whose state was last written longer ago is closed as aborted, never resumed.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    private readonly IReadOnlyList<E2eStage> _stages;
    private readonly E2eStateStore _store;
    private readonly ISelfTestRunGate _gate;
    private readonly ICommandService _commands;
    private readonly ILogService _log;
    private readonly bool _isDev;
    private readonly Func<string> _language;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<long> _tickMs;
    private readonly E2eFrameMonitor? _frames;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private bool _disposed;
    private bool _warnedNotDev;
    private long _lastPollMs = long.MinValue;
    private string? _gateOwner;

    public E2eRunService(IReadOnlyList<E2eStage> stages, E2eStateStore store, ISelfTestRunGate gate,
        ICommandService commands, ILogService log, bool isDev, Func<string> language,
        Func<DateTime>? utcNow = null, Func<long>? tickMs = null, E2eFrameMonitor? frames = null)
    {
        _frames = frames;
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _isDev = isDev;
        _language = language;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _tickMs = tickMs ?? (() => Environment.TickCount64);
    }

    public bool IsRunning => CurrentRun is { IsCompleted: false };

    private volatile bool _suppressSaveOnDispose;
    public bool SuppressSaveOnDispose => _suppressSaveOnDispose;

    /// <summary>The running (or last) run; tests await it.</summary>
    internal Task? CurrentRun { get; private set; }

    public void Start()
    {
        if (!_isDev)
        {
            WarnIfNotDev();
            return;
        }

        var state = _store.Load();
        if (state is not { IsOpen: true }) return;

        if (_utcNow() - state.UpdatedUtc > StaleAfter)
        {
            Info(E2eLogFormat.Stage(state.Id, "run", "fail",
                $"State last written {state.UpdatedUtc:u}, older than {StaleAfter.TotalHours:0} h - closed as aborted.", state.Request.RemoteUrl));
            Finish(state, aborted: true);
            return;
        }
        StartRun(state);
    }

    public void Tick()
    {
        // Every frame (telemetry, plan › B2); the request poll below only every PollInterval.
        _frames?.OnFrame();
        var now = _tickMs();
        if (_lastPollMs != long.MinValue && now - _lastPollMs < (long)PollInterval.TotalMilliseconds) return;
        _lastPollMs = now;
        PollOnce();
    }

    /// <summary>One look for <c>request.json</c> (the body of <see cref="Tick"/>).</summary>
    internal void PollOnce()
    {
        if (!_isDev)
        {
            if (_store.RequestExists) WarnIfNotDev();
            return;
        }
        if (IsRunning || !_store.RequestExists) return;

        var json = _store.TakeRequest();
        if (json == null) return;

        var (request, error) = E2eRequest.Parse(json);
        if (request == null)
        {
            Warn(E2eLogFormat.Stage(IdOf(json), "request", "fail", error ?? "invalid request", null));
            return;
        }

        var previous = _store.Load();
        var now = _utcNow();
        var state = new E2eState
        {
            Id = request.Id,
            Request = request,
            StartedUtc = now,
            UpdatedUtc = now,
            // The config the plugin runs on stays an E2E config until a restore (restore stage or
            // restore.ps1). Its backup comes along so a later run's restore stage can put it back.
            ConfigIsE2e = previous?.ConfigIsE2e == true,
            BackupDir = previous?.ConfigIsE2e == true ? previous.BackupDir : null,
        };
        if (state.ConfigIsE2e) state.Marks[Stages.PreconditionsStage.PreviousConfigIsE2eMark] = "true";
        StartRun(state);
    }

    private void StartRun(E2eState state)
    {
        var owner = $"e2e {state.Id}";
        if (!_gate.TryEnter(owner))
        {
            Warn(E2eLogFormat.Stage(state.Id, "run", "fail", $"Another test run is going ({_gate.Owner}).", state.Request.RemoteUrl));
            return;
        }
        _gateOwner = owner;
        var ct = _cts.Token;
        CurrentRun = Task.Run(() => RunAsync(state, ct), CancellationToken.None);
    }

    private async Task RunAsync(E2eState state, CancellationToken ct)
    {
        var remote = state.Request.RemoteUrl;
        string? current = null;
        try
        {
            var ctx = new E2eContext(state, () => Save(state),
                text => Info(E2eLogFormat.Stage(state.Id, current ?? "run", "progress", text, remote)),
                v => _suppressSaveOnDispose = v,
                part => ReportPart(state, part));

            var fresh = state.StageResults.Count == 0 && state.RunningStage == null;
            var planned = _stages.Where(s => s.IsIn(state.Request)).Select(s => s.Id).ToList();
            if (fresh)
            {
                Info(E2eLogFormat.Stage(state.Id, "run", "start", string.Join(",", planned), remote));
                _commands.PrintText("", $"{Loc.S("E2E run started.")} ({state.Id})");
            }
            else
            {
                // A stage that was running when the plugin unloaded is restarted, never counted.
                Info(E2eLogFormat.Stage(state.Id, "run", "resume", state.RunningStage ?? "next stage", remote));
            }
            // The phase is the reset's (requested → reset-pending → … → reset-done); the reset stage moves
            // it to Stages. A run without backup/reset stays at requested until it finishes.
            Save(state);

            var aborted = AbortingResult(state) != null;
            foreach (var stage in _stages.Where(s => s.IsIn(state.Request)))
            {
                if (aborted && !stage.RunsAfterAbort) continue;
                if (state.ResultOf(stage.Id) != null) continue;

                current = stage.Id;
                SelfTestResult result;
                long durationMs = 0;
                if (MissingNeed(state, stage) is { } need)
                {
                    result = SelfTestResult.Skip(need);
                }
                else
                {
                    state.RunningStage = stage.Id;
                    Save(state);
                    Info(E2eLogFormat.Stage(state.Id, stage.Id, "start", "", remote));
                    (result, durationMs) = await SelfTestRunner.RunWithTimeoutAsync(
                        c => stage.RunAsync(ctx, c), stage.Timeout, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return; // unloading: the next load restarts this stage
                }

                state.StageResults.Add(new E2eStageReport(stage.Id, result.Outcome, E2eLogFormat.Scrub(result.Detail, remote), durationMs));
                state.RunningStage = null;
                Save(state);
                var line = E2eLogFormat.Outcome(state.Id, stage.Id, result.Outcome, result.Detail, remote);
                if (result.Outcome == SelfTestOutcome.Fail) Warn(line); else Info(line);

                if (stage.AbortsRunUnlessPass && result.Outcome != SelfTestOutcome.Pass) aborted = true;
            }
            current = null;
            Finish(state, aborted);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Warn(E2eLogFormat.Stage(state.Id, current ?? "run", "fail", $"{ex.GetType().Name}: {ex.Message}", remote));
            try { Finish(state, aborted: true); } catch { /* the log line above is the report */ }
        }
        finally
        {
            if (_gateOwner != null) _gate.Exit(_gateOwner);
        }
    }

    /// <summary>A part of a stage (ctx.ReportPart): stored, saved and logged like a stage outcome.</summary>
    private void ReportPart(E2eState state, E2eStageReport part)
    {
        var remote = state.Request.RemoteUrl;
        state.StageResults.Add(part with { Detail = E2eLogFormat.Scrub(part.Detail, remote) });
        Save(state);
        var line = E2eLogFormat.Outcome(state.Id, part.Id, part.Outcome, part.Detail, remote);
        if (part.Outcome == SelfTestOutcome.Fail) Warn(line); else Info(line);
    }

    /// <summary>The first result of a stage that ends the run on anything but Pass, if it did not pass.</summary>
    private E2eStageReport? AbortingResult(E2eState state) =>
        state.StageResults.FirstOrDefault(r => r.Outcome != SelfTestOutcome.Pass
            && _stages.Any(s => s.Id == r.Id && s.AbortsRunUnlessPass));

    /// <summary>"needs X (outcome)" for the first dependency that is not met; null when all are.</summary>
    internal static string? MissingNeed(E2eState state, E2eStage stage) => stage.UnmetNeed(state);

    private void Finish(E2eState state, bool aborted)
    {
        state.Phase = aborted ? E2ePhase.Aborted : E2ePhase.Finished;
        state.RunningStage = null;
        var report = new E2eReport(state.Id, Plugin.PluginVersion, _language(), state.StartedUtc, _utcNow(),
            state.Phase, state.Request.ForReport(), state.StageResults, state.BackupDir, state.Telemetry);
        var path = WriteReport(report, state.Request.RemoteUrl);
        Save(state);
        // Stage 8: the config stays an E2E one until a restore (restore stage or restore.ps1).
        // Before the done line: the watcher stops reading there.
        if (state.ConfigIsE2e)
            Warn(E2eLogFormat.Stage(state.Id, "run", "warn",
                $"The plugin runs on the E2E config until restore (tools/e2e/restore.sh --live, or restore.ps1 in {state.BackupDir} with the game closed).",
                state.Request.RemoteUrl));
        Info(E2eLogFormat.Done(state.Id, report.Passed, report.Failed, report.Skipped, path ?? "(report not written)"));
        _commands.PrintText("", $"{Loc.S("E2E run finished.")} {report.Passed}/{report.Failed}/{report.Skipped} ({state.Id})");
        if (state.ConfigIsE2e)
            _commands.PrintText("", Loc.S("The plugin runs on the E2E config until it is restored (restore.sh --live or restore.ps1)."));
    }

    internal static string SerializeReport(E2eReport report, string? remoteUrl) =>
        E2eLogFormat.Scrub(JsonSerializer.Serialize(report, E2eStateStore.JsonOpts), remoteUrl);

    private string? WriteReport(E2eReport report, string? remoteUrl)
    {
        lock (_lock)
        {
            if (_disposed) return null;
            try
            {
                return _store.WriteReport(SerializeReport(report, remoteUrl), report.Id);
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(E2eRunService), $"E2E report not written: {ex.Message}", EventId);
                return null;
            }
        }
    }

    private void Save(E2eState state)
    {
        lock (_lock)
        {
            // After Dispose the next plugin instance owns state.json; a late write would overwrite it.
            if (_disposed) return;
            state.UpdatedUtc = _utcNow();
            _store.Save(state);
        }
    }

    private void WarnIfNotDev()
    {
        if (_warnedNotDev) return;
        if (!_store.RequestExists && !_store.StateExists) return;
        _warnedNotDev = true;
        _log.Warning(nameof(E2eRunService),
            $"E2E files in {_store.Directory} are ignored: E2E runs only work on a dev load of the plugin.", EventId);
    }

    /// <summary>Best effort: the id of an invalid request, for the log line the watcher waits on.</summary>
    private static string IdOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return string.Join("", p.Value.GetString()!.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(64)) is { Length: > 0 } id ? id : "-";
        }
        catch (JsonException) { /* fall through */ }
        return "-";
    }

    private static readonly EKEventId EventId = new(0, TextSource.None);

    private void Info(string line)
    {
        if (!Volatile.Read(ref _disposed)) _log.Info(nameof(E2eRunService), line, EventId);
    }

    private void Warn(string line)
    {
        if (!Volatile.Read(ref _disposed)) _log.Warning(nameof(E2eRunService), line, EventId);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _cts.Cancel();
        // Not disposed on purpose: the run task may still read the token while it unwinds.
    }
}
