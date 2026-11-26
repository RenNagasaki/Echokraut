using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 3 (plan › Stages): walks the first-time wizard like a newcomer, through the same
/// <see cref="IFirstTimeWizardController"/> actions the window's buttons call: EchokrauTTS → Local →
/// install root = request root (emptied, marker first) → <c>LocalSaveLocation</c> under it (E2E-only,
/// the wizard has no such field) → community-DB tick as in the backed-up config [M3] → Install → wait
/// with a polled predicate [H3] → the listener on the engine port lives under the root [BLK-2] →
/// voices mapped [H5] → Next → Finish. Then checks the result [H2].
/// </summary>
public sealed class FirstTimeStage : E2eStage
{
    public const string StageId = "first-time";
    /// <summary>Part: the wizard opened by itself after the reset reload.</summary>
    public const string AutoOpenPart = StageId + ":auto-open";

    internal static readonly TimeSpan InstallWait = TimeSpan.FromMinutes(60);
    internal static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan VoicesWait = TimeSpan.FromMinutes(2);

    private readonly IFirstTimeWizardController _wizard;
    private readonly Configuration _config;
    private readonly IEchokrauTtsInstanceService _engine;
    private readonly ICommandService _commands;
    private readonly SelfTestCheck? _backendCheck;
    private readonly IE2eInstallHost _host;
    private readonly Func<Action, Task> _onFramework;
    private readonly string _pluginVersion;
    private readonly TimeSpan _poll;
    private readonly E2eEngineWait _wait;
    private readonly TimeSpan _stepPause;
    private readonly TimeSpan _installWait;
    private readonly TimeSpan _windowWait;
    private readonly TimeSpan _voicesWait;
    private readonly E2eFrameMonitor? _frames;

    public FirstTimeStage(IFirstTimeWizardController wizard, Configuration config, IEchokrauTtsInstanceService engine,
        IBackendService backend, INpcDataService npcData, ICommandService commands, SelfTestCheck? backendCheck,
        IE2eInstallHost host, Func<Action, Task> onFramework, string pluginVersion,
        TimeSpan? pollInterval = null, TimeSpan? stepPause = null, TimeSpan? installWait = null,
        TimeSpan? windowWait = null, TimeSpan? voicesWait = null, E2eFrameMonitor? frames = null)
    {
        _frames = frames;
        _wizard = wizard;
        _config = config;
        _engine = engine;
        _commands = commands;
        _backendCheck = backendCheck;
        _host = host;
        _onFramework = onFramework;
        _pluginVersion = pluginVersion;
        _poll = pollInterval ?? TimeSpan.FromSeconds(2);
        _wait = new E2eEngineWait(engine, backend, npcData, _poll);
        _stepPause = stepPause ?? TimeSpan.FromSeconds(1);
        _installWait = installWait ?? InstallWait;
        _windowWait = windowWait ?? WindowWait;
        _voicesWait = voicesWait ?? VoicesWait;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [ResetStage.StageId];
    public override TimeSpan Timeout => _installWait + _windowWait + _voicesWait + TimeSpan.FromMinutes(3);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var root = ctx.Request.InstallRoot;

        // A requested test wrapper that is not there fails before anything is touched: the release
        // install would pass and the run would report a wrapper it never tested.
        if (ctx.Request.TestWrapper && !_engine.TestInstallAvailable)
            return SelfTestResult.Fail($"testWrapper requested, but the test zip is missing ({_engine.TestWrapperZipPath ?? "no dev load"}; tools/e2e/push-test-wrapper.sh).");
        // Same for an older release (plan › B8): resolved before anything is touched, so the install below fetches it.
        if (ctx.Request.WrapperVersion is { } tag)
        {
            if (await _engine.SelectWrapperReleaseAsync(tag).ConfigureAwait(false) is { } releaseError)
                return SelfTestResult.Fail($"wrapperVersion {tag}: {releaseError}.");
            ctx.Progress($"wrapper release {tag} selected for the install");
        }

        // 1. The wizard window. HandleStartup opens it on a fresh config; if it missed the login, toggle it [L6].
        // Whether it opened by itself is a result of its own (test plan 1: fresh config, logged in → wizard opens);
        // the run goes on either way.
        var autoOpened = await WaitForAsync(() => _wizard.IsWindowOpen, _windowWait / 3, ct).ConfigureAwait(false);
        if (ctx.State.ResultOf(AutoOpenPart) == null)
            ctx.ReportPart(new E2eStageReport(AutoOpenPart, autoOpened ? SelfTestOutcome.Pass : SelfTestOutcome.Fail,
                autoOpened
                    ? "The wizard opened by itself on the fresh config (HandleStartup, logged in)."
                    : $"The wizard did not open by itself within {(_windowWait / 3).TotalSeconds:0} s on a fresh config - the stage had to open it.",
                0));
        if (!autoOpened)
        {
            ctx.Progress("wizard not open, opening it");
            await _onFramework(() => { if (!_wizard.IsWindowOpen) _commands.ToggleFirstTimeUi(); }).ConfigureAwait(false);
            if (!await WaitForAsync(() => _wizard.IsWindowOpen, _windowWait, ct).ConfigureAwait(false))
                return SelfTestResult.Fail($"The first-time wizard did not open within {_windowWait.TotalSeconds:0} s.");
        }

        // 2. Step 0: engine + mode.
        await StepAsync(() => _wizard.ChooseEngine(TTSBackends.EchokrauTTS), ct).ConfigureAwait(false);
        await StepAsync(() => _wizard.ChooseMode(AlltalkInstanceType.Local), ct).ConfigureAwait(false);
        ctx.Progress("step 0: EchokrauTTS, Local");

        // 3. Step 1: install root (fresh, marker before the install), save location, community tick.
        if (_host.PrepareRoot(root) is { } prepareError) return SelfTestResult.Fail(prepareError);
        var saves = Path.Combine(root, "LocalSaves");
        var share = ctx.State.BackupDir is { } backup ? _host.BackedUpShareLines(backup) : null;
        if (share == null) return SelfTestResult.Fail($"Could not read ShareLinesWithCommunityDb from the backed-up {E2eInstallHost.ConfigFileName}.");
        await StepAsync(() =>
        {
            _wizard.SetInstallRoot(root);
            _wizard.SetLocalSaveLocation(saves);
            _wizard.SetShareLines(share.Value);
        }, ct).ConfigureAwait(false);
        ctx.Progress($"step 1: root {root} (emptied, marker written), saves {saves}, community DB {share.Value}");

        // 4. Install (the controller runs stop + install on the thread pool; a console window opens [L5]).
        ctx.Progress(ctx.Request.TestWrapper ? $"install started (test wrapper {_engine.TestWrapperZipPath})" : "install started");
        // Frame times over the whole install (freeze watchdog, plan › B2): the wizard's install must never block the game.
        string? installError = null;
        async Task Install()
        {
            await _wizard.StartInstall(ctx.Request.TestWrapper).ConfigureAwait(false);
            installError = await _wait.WaitForReadyAsync(ctx, "install", _installWait, ct).ConfigureAwait(false);
        }
        if (_frames == null) await Install().ConfigureAwait(false);
        else
        {
            var op = await _frames.MeasureAsync("install", Install).ConfigureAwait(false);
            ctx.State.Telemetry.Ops.RemoveAll(o => o.Op == op.Op);
            ctx.State.Telemetry.Ops.Add(op);
            ctx.Save();
            ctx.Progress($"install frames: {op.Frames}");
        }
        if (installError != null) return SelfTestResult.Fail(installError);

        // 5. The engine that answers is the one just installed, not a leftover [BLK-2].
        var port = Uri.TryCreate(_config.EchokrauTts.BaseUrl, UriKind.Absolute, out var uri) ? uri.Port : BackupStage.DefaultEnginePort;
        var exe = _host.ListenerExecutable(port);
        if (exe == null) return SelfTestResult.Fail($"Could not tell which process listens on port {port}.");
        if (!IsUnder(exe, root)) return SelfTestResult.Fail($"Port {port} is served by {exe}, not by the install under {root}.");
        ctx.Progress($"port {port} served by {exe}");

        // 6. Voices [H5]: RefreshBackend blocks (HTTP), we are on the thread pool.
        var voices = await _wait.WaitForVoicesAsync(_voicesWait, ct).ConfigureAwait(false);
        if (voices == 0) return SelfTestResult.Fail($"No enabled voice within {_voicesWait.TotalSeconds:0} s after RefreshBackend.");
        ctx.Progress($"{voices} enabled voice(s)");

        // 7. Step 1 → 2 → finish.
        var advanced = false;
        await StepAsync(() => advanced = _wizard.Next(), ct).ConfigureAwait(false);
        if (!advanced) return SelfTestResult.Fail($"Next refused on step {_wizard.Step} (LocalInstall {_config.EchokrauTts.LocalInstall}).");
        var finished = false;
        await StepAsync(() => finished = _wizard.Finish(), ct).ConfigureAwait(false);
        if (!finished) return SelfTestResult.Fail($"Finish refused on step {_wizard.Step}.");
        await WaitForAsync(() => !_wizard.IsWindowOpen, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

        // 8. Result [H2].
        var problems = Verify(root);
        if (ctx.Request.WrapperVersion is { } wanted && _config.EchokrauTts.InstalledWrapperVersion != wanted)
            problems.Add($"installed wrapper {_config.EchokrauTts.InstalledWrapperVersion}, requested {wanted}");
        foreach (var freeze in ctx.State.Telemetry.Freezes().Where(o => o.Op == "install"))
            problems.Add($"install froze the game ({freeze.Frames.MaxMs:0} ms frame > {ctx.State.Telemetry.FreezeLimitMs:0} ms)");
        if (_backendCheck != null)
        {
            var entry = await SelfTestRunner.RunCheckAsync(_backendCheck, ct).ConfigureAwait(false);
            if (entry.Outcome != SelfTestOutcome.Pass) problems.Add($"backend check {E2eLogFormat.Kind(entry.Outcome)}: {entry.Detail}");
        }
        return problems.Count == 0
            ? SelfTestResult.Pass($"Wizard done: EchokrauTTS Local on {root}, {voices} voice(s), {_config.EchokrauTts.InstalledWrapperVersion}.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }

    internal List<string> Verify(string root)
    {
        var problems = new List<string>();
        if (_wizard.IsWindowOpen) problems.Add("wizard still open");
        if (_config.FirstTime) problems.Add("FirstTime still true");
        if (_config.LastSeenChangelogVersion != _pluginVersion)
            problems.Add($"LastSeenChangelogVersion {_config.LastSeenChangelogVersion} != {_pluginVersion}");
        if (_config.BackendSelection != TTSBackends.EchokrauTTS) problems.Add($"BackendSelection {_config.BackendSelection}");
        if (_config.EchokrauTts.InstanceType != AlltalkInstanceType.Local) problems.Add($"InstanceType {_config.EchokrauTts.InstanceType}");
        if (!SamePath(_config.TtsInstallRoot, root)) problems.Add($"TtsInstallRoot {_config.TtsInstallRoot}");
        if (!IsUnder(_config.LocalSaveLocation, root)) problems.Add($"LocalSaveLocation {_config.LocalSaveLocation} not under the root");
        if (!_config.EchokrauTts.LocalInstall) problems.Add("LocalInstall false");
        if (!_engine.InstanceRunning) problems.Add("instance not running");
        return problems;
    }

    /// <summary>One wizard action on the framework thread, then a short pause so the window can be followed.</summary>
    private async Task StepAsync(Action action, CancellationToken ct)
    {
        await _onFramework(action).ConfigureAwait(false);
        if (_stepPause > TimeSpan.Zero) await Task.Delay(_stepPause, ct).ConfigureAwait(false);
    }

    private Task<bool> WaitForAsync(Func<bool> condition, TimeSpan wait, CancellationToken ct) => WaitUntilAsync(condition, wait, _poll, ct);

    private static string Norm(string path) =>
        path.Replace('/', '\\').TrimEnd('\\');

    internal static bool SamePath(string? a, string? b) =>
        a != null && b != null && string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Case-insensitive, Windows separators; <paramref name="path"/> equal to or inside <paramref name="root"/>.</summary>
    internal static bool IsUnder(string? path, string root)
    {
        if (path == null) return false;
        var p = Norm(path);
        var r = Norm(root);
        return string.Equals(p, r, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
