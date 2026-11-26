using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Puts the real config back without a game restart (plan › Report and restore › Live restore). Opt-in:
/// only runs when the request lists it (<c>tools/e2e/restore.sh --live</c>, or <c>start.sh --restore</c> as the
/// last stage of a run). <b>Phase A</b> (<see cref="E2eSelfReload"/>): check the backup, stop the E2E engine,
/// require its port to be free, suppress the Dispose save, <c>phase = restore-pending</c>, touch the own DLL.
/// The next ctor restores (<see cref="E2eStartupReset"/>); the restarted stage reports it.
/// </summary>
public sealed class RestoreStage : E2eStage
{
    public const string StageId = "restore";

    private readonly E2eSelfReload _reload;
    private readonly string _configDirectory;

    public RestoreStage(E2eSelfReload reload, string configDirectory)
    {
        _reload = reload;
        _configDirectory = configDirectory;
    }

    public override string Id => StageId;
    public override bool ExplicitOnly => true;
    public override bool RunsAfterAbort => true;
    public override TimeSpan Timeout => _reload.StageTimeout;

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var s = ctx.State;

        // Restarted after the reload: report what the ctor did.
        if (s.Phase == E2ePhase.RestoreDone)
        {
            s.Marks.TryGetValue(E2eStartupReset.RestoreFilesMark, out var files);
            return SelfTestResult.Pass($"{files ?? "?"} files restored from {s.BackupDir}, every SHA-256 matches - the plugin runs on the real config.");
        }
        if (s.Marks.TryGetValue(E2eStartupReset.RestoreErrorMark, out var error))
            return SelfTestResult.Fail(s.Phase == E2ePhase.RestoreStarted
                ? $"Restore stopped after the wipe ({error}) - it resumes on the next plugin load; or run restore.ps1 with the game closed."
                : $"Backup check failed, E2E config untouched: {error}");
        if (s.Phase is E2ePhase.RestorePending or E2ePhase.RestoreStarted)
        {
            E2eSelfReload.Revert(ctx, E2ePhase.Stages, E2eStartupReset.RestoreRequestedMark);
            return SelfTestResult.Fail("The plugin reloaded but the startup restore did not act - E2E config untouched.");
        }

        if (!s.ConfigIsE2e) return SelfTestResult.Skip("The plugin already runs on the real config.");

        // Phase A. The backup is checked here too, so a bad one fails before the engine stops.
        try
        {
            E2eStartupReset.VerifiedBackup(s.BackupDir, _configDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return SelfTestResult.Fail($"Backup not usable, nothing changed: {ex.Message}");
        }
        if (!File.Exists(_reload.DllPath)) return SelfTestResult.Fail($"Own DLL not found at {_reload.DllPath}.");

        if (await _reload.StopEnginesAsync(ctx, ct).ConfigureAwait(false) is { } busy)
            return SelfTestResult.Fail(busy);
        if (await _reload.CloseWindowsAsync(ctx, ct).ConfigureAwait(false) is { } open)
            return SelfTestResult.Fail(open);

        var before = s.Phase;
        await _reload.RequestReloadAsync(ctx, E2ePhase.RestorePending, E2eStartupReset.RestoreRequestedMark, ct).ConfigureAwait(false);
        E2eSelfReload.Revert(ctx, before, E2eStartupReset.RestoreRequestedMark);
        return SelfTestResult.Fail(
            $"No reload within {_reload.ReloadWait.TotalSeconds:0} s after touching {Path.GetFileName(_reload.DllPath)} - E2E config untouched (the engine stays stopped).");
    }
}
