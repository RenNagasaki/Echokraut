using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 1 (plan › Stages). <b>Phase A</b> on the running plugin with the real config
/// (<see cref="E2eSelfReload"/>): stop the real local engine, require its port to be free, suppress the
/// config save on Dispose, <c>phase = reset-pending</c>, touch the own DLL so Dalamud reloads the plugin.
/// <b>Phase B</b> runs in the next ctor (<see cref="E2eStartupReset"/>); the restarted stage then only
/// reports what it did. No reload within the reload wait → fail, phase back to requested, real config untouched.
/// </summary>
public sealed class BackupStage : E2eStage
{
    public const string StageId = "backup";
    public const int DefaultEnginePort = E2eSelfReload.DefaultEnginePort;

    private readonly E2eSelfReload _reload;

    public BackupStage(IEchokrauTtsInstanceService echokrauTts, IAlltalkInstanceService alltalk, Configuration config,
        string dllPath, Func<int, CancellationToken, Task<bool>>? portOpen = null, Func<DateTime>? utcNow = null,
        Action<string, DateTime>? touch = null, TimeSpan? reloadWait = null, TimeSpan? portWait = null,
        TimeSpan? pollInterval = null)
    {
        _reload = new E2eSelfReload(echokrauTts, alltalk, config, dllPath, portOpen, utcNow, touch,
            reloadWait, portWait, pollInterval);
    }

    /// <summary>Shares the catalog's <see cref="E2eSelfReload"/> (with the window service) with <c>restore</c>.</summary>
    public BackupStage(E2eSelfReload reload) => _reload = reload;

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [PreconditionsStage.StageId];
    public override TimeSpan Timeout => _reload.StageTimeout;
    public override bool AbortsRunUnlessPass => true;

    internal IReadOnlyList<int> Ports() => _reload.Ports();

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var s = ctx.State;

        // Restarted after the reload: report phase B.
        if (s.Marks.TryGetValue(E2eStartupReset.BackupErrorMark, out var error))
            return SelfTestResult.Fail($"Backup failed, real config untouched: {error}");
        if (s.Phase >= E2ePhase.BackupDone && s.BackupDir != null)
        {
            s.Marks.TryGetValue(E2eStartupReset.BackupFilesMark, out var files);
            return SelfTestResult.Pass($"{files ?? "?"} files backed up to {s.BackupDir}, every SHA-256 matches, restore.ps1 written.");
        }
        if (s.Phase == E2ePhase.ResetPending)
        {
            Revert(ctx);
            return SelfTestResult.Fail("The plugin reloaded but the startup reset did not act (phase still reset-pending) - real config untouched.");
        }

        // Phase A.
        if (!File.Exists(_reload.DllPath)) return SelfTestResult.Fail($"Own DLL not found at {_reload.DllPath}.");

        if (await _reload.StopEnginesAsync(ctx, ct).ConfigureAwait(false) is { } busy)
            return SelfTestResult.Fail(busy);
        if (await _reload.CloseWindowsAsync(ctx, ct).ConfigureAwait(false) is { } open)
            return SelfTestResult.Fail(open);

        await _reload.RequestReloadAsync(ctx, E2ePhase.ResetPending, E2eStartupReset.ResetRequestedMark, ct).ConfigureAwait(false);
        Revert(ctx);
        return SelfTestResult.Fail(
            $"No reload within {_reload.ReloadWait.TotalSeconds:0} s after touching {Path.GetFileName(_reload.DllPath)} - real config untouched (the engine stays stopped).");
    }

    private static void Revert(E2eContext ctx) =>
        E2eSelfReload.Revert(ctx, E2ePhase.Requested, E2eStartupReset.ResetRequestedMark);
}
