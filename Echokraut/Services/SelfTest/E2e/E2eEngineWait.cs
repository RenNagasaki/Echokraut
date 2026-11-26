using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The polled waits for the local EchokrauTTS engine (plan › Abort and dependency rules [H3]), shared by
/// <c>first-time</c> (install) and <c>engine</c> (switch + model download): ready = running and the engine
/// answers, failed = a <c>Failed…</c> status or an engine that went inactive. Polls on the thread pool;
/// <see cref="IBackendService.RefreshBackend"/> blocks, so never call this from the framework thread.
/// </summary>
public sealed class E2eEngineWait
{
    private readonly IEchokrauTtsInstanceService _engine;
    private readonly IBackendService _backend;
    private readonly INpcDataService _npcData;
    private readonly TimeSpan _poll;

    public E2eEngineWait(IEchokrauTtsInstanceService engine, IBackendService backend, INpcDataService npcData,
        TimeSpan? pollInterval = null)
    {
        _engine = engine;
        _backend = backend;
        _npcData = npcData;
        _poll = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    public TimeSpan PollInterval => _poll;

    /// <summary>What the poll sees; one per poll.</summary>
    internal readonly record struct InstallSnapshot(bool Installing, bool Starting, bool Running, string Status, bool Reachable);

    internal enum InstallVerdict { Waiting, Ready, Failed }

    /// <summary>
    /// The predicate [H3]. Ready = running and the engine answers. Failed = a <c>Failed…</c> status, an
    /// engine that never became active within the grace time, or an engine that went inactive on two polls
    /// in a row (one inactive poll is the gap between <c>MarkInstalled</c> and <c>InstanceRunning = true</c>).
    /// </summary>
    internal static InstallVerdict Judge(InstallSnapshot s, bool seenActive, int inactivePolls, bool graceOver)
    {
        if (s.Running && s.Reachable) return InstallVerdict.Ready;
        if (s.Status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
            || s.Status.Contains("not configured", StringComparison.OrdinalIgnoreCase))
            return InstallVerdict.Failed;
        var active = s.Installing || s.Starting || s.Running;
        if (!active && seenActive && inactivePolls >= 2) return InstallVerdict.Failed;
        if (!active && !seenActive && graceOver) return InstallVerdict.Failed;
        return InstallVerdict.Waiting;
    }

    /// <summary>
    /// Waits until the engine is ready; null on ready, else why not. <paramref name="label"/> starts every
    /// progress line (at most one per 10 s or per status change).
    /// </summary>
    public async Task<string?> WaitForReadyAsync(E2eContext ctx, string label, TimeSpan wait, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var seenActive = false;
        var inactivePolls = 0;
        var lastStatus = "";
        var lastProgress = DateTime.MinValue;
        while (true)
        {
            var running = _engine.InstanceRunning;
            var reachable = false;
            if (running)
            {
                _backend.InvalidateReachabilityCache();
                reachable = await _backend.IsBackendReachableAsync().ConfigureAwait(false);
            }
            var s = new InstallSnapshot(_engine.Installing, _engine.InstanceStarting, running, _engine.CurrentInstallStatus ?? "", reachable);
            var active = s.Installing || s.Starting || s.Running;
            seenActive |= active;
            inactivePolls = active ? 0 : inactivePolls + 1;
            var elapsed = DateTime.UtcNow - started;

            if (s.Status != lastStatus || DateTime.UtcNow - lastProgress >= TimeSpan.FromSeconds(10))
            {
                ctx.Progress($"{label} {elapsed.TotalSeconds:0} s: {(s.Status.Length > 0 ? s.Status : "-")} ({_engine.CurrentInstallProgress:P0})");
                lastStatus = s.Status;
                lastProgress = DateTime.UtcNow;
            }

            switch (Judge(s, seenActive, inactivePolls, elapsed >= TimeSpan.FromSeconds(30)))
            {
                case InstallVerdict.Ready: return null;
                case InstallVerdict.Failed:
                    return $"{label} failed after {elapsed.TotalSeconds:0} s: {(s.Status.Length > 0 ? s.Status : "engine stopped without a status")}.";
            }
            if (elapsed >= wait) return $"{label} not ready after {wait.TotalMinutes:0} min (status: {s.Status}).";
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="IBackendService.RefreshBackend"/> (again every 10 s) until at least one voice is enabled
    /// [H5]; returns the count of enabled voices (0 when the wait ran out).
    /// </summary>
    public async Task<int> WaitForVoicesAsync(TimeSpan wait, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        var lastRefresh = DateTime.MinValue;
        while (true)
        {
            if (DateTime.UtcNow - lastRefresh >= TimeSpan.FromSeconds(10))
            {
                _backend.RefreshBackend();
                lastRefresh = DateTime.UtcNow;
            }
            var enabled = _npcData.GetEchokrautVoices().Count(v => v.IsEnabled);
            if (enabled > 0 || DateTime.UtcNow >= deadline) return enabled;
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
    }
}
