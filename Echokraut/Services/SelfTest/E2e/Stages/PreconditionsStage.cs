using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 0, plugin side (the ssh side is <c>tools/e2e/preconditions.ps1</c>): logged in, dev load, no
/// Talk/BattleTalk window, no cutscene or duty, a safe install root, and the current config is not
/// itself an E2E config. Every finding is reported, the run ends unless all pass.
/// </summary>
public sealed class PreconditionsStage : E2eStage
{
    public const string StageId = "preconditions";
    /// <summary>Mark set when the run was requested while the previous run left an E2E config behind.</summary>
    public const string PreviousConfigIsE2eMark = "previous-config-is-e2e";

    private readonly IE2eGameProbe _probe;
    private readonly Configuration _config;
    private readonly bool _isDev;
    private readonly IE2eRootFileSystem _fs;
    private readonly Func<string, (bool isValid, string message)>? _validateInstallPath;

    public PreconditionsStage(IE2eGameProbe probe, Configuration config, bool isDev, IE2eRootFileSystem fs,
        Func<string, (bool isValid, string message)>? validateInstallPath = null)
    {
        _validateInstallPath = validateInstallPath;
        _probe = probe;
        _config = config;
        _isDev = isDev;
        _fs = fs;
    }

    public override string Id => StageId;
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public override bool AbortsRunUnlessPass => true;

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var problems = new List<string>();
        if (!_isDev) problems.Add("not a dev load (IsDev is false)");

        var game = await _probe.SnapshotAsync().ConfigureAwait(false);
        if (!game.LoggedIn) problems.Add("not logged in");
        else if (string.IsNullOrEmpty(game.PlayerName)) problems.Add("player name unknown");
        if (game.InCutscene) problems.Add("in a cutscene");
        if (game.InDuty) problems.Add("in a duty");
        if (game.TalkOpen) problems.Add("a Talk window is open");
        if (game.BattleTalkOpen) problems.Add("a BattleTalk window is open");

        var root = ctx.Request.InstallRoot;
        if (E2eRootGuard.Check(root, [_config.TtsInstallRoot, _config.LocalSaveLocation, _config.Alltalk.LocalInstallPath], _fs, _validateInstallPath) is { } rootProblem)
            problems.Add(rootProblem);

        // An E2E config must never be backed up as if it were the real one (plan [BLK-4]). A config whose
        // TtsInstallRoot is the E2E root is caught by the root guard above (overlap).
        if (ctx.State.Marks.TryGetValue(PreviousConfigIsE2eMark, out var prev) && prev == "true")
            problems.Add("the plugin still runs on the config of an earlier E2E run - run restore.ps1 first");

        return problems.Count == 0
            ? SelfTestResult.Pass($"Logged in, dev load, no dialogue open, install root {root} is safe.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }
}
