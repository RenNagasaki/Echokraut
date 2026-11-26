using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage <c>wrapper-mixed</c> (plan docs/plans/ingame-e2e.md › B8): the mixed state "this plugin + the older wrapper
/// release <c>first-time</c> installed from the request's <c>wrapperVersion</c>", walked like a user on the Backend tab.
/// Parts: <c>installed</c> (recorded tag + the installer's download URL), <c>greyed</c> (engines whose
/// <c>minVersion</c> is above that tag are listed with the reason text and a pick is refused), <c>update-offered</c>
/// (the wrapper button checks and offers the newest release), <c>update</c> (the second click updates, voices kept),
/// <c>unlocked</c> (the same entries without reason; a pick switches the engine and it starts, then the original is
/// picked back). Nothing in it names a release: the expectations come from the catalog, so it serves every release.
/// </summary>
public sealed class MixedWrapperStage : E2eStage
{
    public const string StageId = "wrapper-mixed";

    internal static readonly TimeSpan CheckWait = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan UpdateWait = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan SwitchWait = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan RefuseWait = TimeSpan.FromSeconds(3);

    private readonly IEchokrauTtsInstanceService _engine;
    private readonly Configuration _config;
    private readonly E2eUiDriver _ui;
    private readonly E2eBackendTab _tab;
    private readonly E2eEngineWait _wait;
    private readonly Func<string, IReadOnlyList<string>> _installerLog;
    private readonly E2eFrameMonitor? _frames;
    private readonly TimeSpan _poll;
    private readonly TimeSpan _checkWait;
    private readonly TimeSpan _updateWait;
    private readonly TimeSpan _switchWait;
    private readonly TimeSpan _refuseWait;

    /// <param name="installerLog">Lines of the installer log under an install root (<see cref="E2eInstallerLog"/>).</param>
    public MixedWrapperStage(IEchokrauTtsInstanceService engine, Configuration config, E2eUiDriver ui, E2eEngineWait wait,
        Func<string, IReadOnlyList<string>> installerLog, E2eFrameMonitor? frames = null, TimeSpan? poll = null,
        TimeSpan? checkWait = null, TimeSpan? updateWait = null, TimeSpan? switchWait = null, TimeSpan? refuseWait = null)
    {
        _engine = engine;
        _config = config;
        _ui = ui;
        _tab = new E2eBackendTab(ui, config);
        _wait = wait;
        _installerLog = installerLog;
        _frames = frames;
        _poll = poll ?? TimeSpan.FromMilliseconds(500);
        _checkWait = checkWait ?? CheckWait;
        _updateWait = updateWait ?? UpdateWait;
        _switchWait = switchWait ?? SwitchWait;
        _refuseWait = refuseWait ?? RefuseWait;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [FirstTimeStage.StageId];
    public override TimeSpan Timeout => _updateWait + 2 * _switchWait + TimeSpan.FromMinutes(5);

    public static string PartId(string part) => $"{StageId}:{part}";

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (ctx.Request.WrapperVersion is not { } old)
            return SelfTestResult.Skip("No wrapperVersion in the request - first-time installed the current release, there is no mixed state.");
        if (!ctx.State.ConfigIsE2e) return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");

        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        var blocked = E2eMixedWrapperChecks.Blocked(_engine.AvailableEngines, old);
        // A failed part blocks the rest (each builds on the one before); a skipped greyed part (a release that adds no
        // engine) does not: the update itself is still worth checking.
        string? Blocker() => outcomes.FirstOrDefault(o => o.Outcome == SelfTestOutcome.Fail) is { Id: { } id } ? $"needs {id}" : null;

        await RunPartAsync(ctx, outcomes, PartId("installed"), null, c => Task.FromResult(Installed(old)), ct).ConfigureAwait(false);
        await RunPartAsync(ctx, outcomes, PartId("greyed"), Blocker() ?? NothingBlocked(blocked, old),
            c => GreyedAsync(blocked, c), ct).ConfigureAwait(false);
        await RunPartAsync(ctx, outcomes, PartId("update-offered"), Blocker(), c => UpdateOfferedAsync(old, c), ct).ConfigureAwait(false);
        await RunPartAsync(ctx, outcomes, PartId("update"), Blocker(), c => UpdateAsync(ctx, old, c), ct).ConfigureAwait(false);
        await RunPartAsync(ctx, outcomes, PartId("unlocked"), Blocker() ?? NothingBlocked(blocked, old),
            c => UnlockedAsync(ctx, blocked, c), ct).ConfigureAwait(false);
        return FromParts(outcomes);
    }

    private static string? NothingBlocked(IReadOnlyList<TtsEngineInfo> blocked, string old) =>
        blocked.Count == 0 ? $"the catalog lists no engine above wrapper {old} - nothing to grey out" : null;

    private string UnavailableFormat => Loc.S("{0} (from wrapper version {1})");

    private IReadOnlyList<TtsEngineOption> Options() =>
        TtsEngineAvailability.ForConfig(_engine.AvailableEngines, _config.EchokrauTts, _engine.LatestWrapperVersion,
            _engine.ClientLanguageCode, listUnavailable: true, Loc.S);

    private IReadOnlyList<E2eInstallerLog.ModeLine> Modes() =>
        E2eInstallerLog.WrapperModeLines(_installerLog(_config.TtsInstallRoot));

    // ── installed ───────────────────────────────────────────────────────────

    private SelfTestResult Installed(string old)
    {
        var problems = new List<string>();
        if (_config.EchokrauTts.InstalledWrapperVersion != old)
            problems.Add($"recorded wrapper {_config.EchokrauTts.InstalledWrapperVersion}, requested {old}");
        if (E2eMixedWrapperChecks.DownloadProblem(Modes(), "echokrautts", old) is { } download) problems.Add(download);
        if (!_engine.InstanceRunning) problems.Add("engine not running");
        return FromProblems(problems, $"wrapper {old} installed and running on {_config.EchokrauTts.TtsBackendArg}.");
    }

    // ── greyed ──────────────────────────────────────────────────────────────

    /// <summary>The catalog's blocked engines are listed with their reason, the dropdown shows exactly that, and a pick is refused.</summary>
    private async Task<SelfTestResult> GreyedAsync(IReadOnlyList<TtsEngineInfo> blocked, CancellationToken ct)
    {
        var options = Options();
        var problems = E2eMixedWrapperChecks.EntryProblems(options, blocked, UnavailableFormat).ToList();
        if (await RenderedProblemAsync(options, ct).ConfigureAwait(false) is { } rendered) problems.Add(rendered);

        var engineBefore = _config.EchokrauTts.TtsBackendArg;
        var current = TtsEngineAvailability.LabelFor(options, engineBefore);
        foreach (var engine in blocked)
        {
            if (await PickAsync(engine.Id, ct).ConfigureAwait(false) is { } pickError)
            {
                problems.Add($"pick {engine.Id}: {pickError}");
                continue;
            }
            await Task.Delay(_refuseWait, ct).ConfigureAwait(false);
            if (_config.EchokrauTts.TtsBackendArg != engineBefore)
                problems.Add($"pick {engine.Id} accepted: engine is now {_config.EchokrauTts.TtsBackendArg}");
            if (_engine.InstanceStarting || !_engine.InstanceRunning) problems.Add($"pick {engine.Id} restarted the engine");
        }
        if (await ClosedLabelAsync(ct).ConfigureAwait(false) is { } shown && !shown.Contains(current))
            problems.Add($"dropdown shows '{string.Join("|", shown)}' after the refused picks, not '{current}'");

        var detail = $"{blocked.Count} blocked ({string.Join(", ", blocked.Select(b => $"{b.Id} >= {b.MinVersion}"))}), " +
                     $"{options.Count(o => o.Available)} offered, picks refused, engine still {engineBefore}.";
        return FromProblems(problems, detail);
    }

    // ── update offered ──────────────────────────────────────────────────────

    /// <summary>First click on the wrapper button: the check finds a newer release, the button says "Update", the label names both.</summary>
    private async Task<SelfTestResult> UpdateOfferedAsync(string old, CancellationToken ct)
    {
        if (_engine.UpdateState is not (WrapperUpdateState.NotChecked or WrapperUpdateState.CheckFailed))
            return SelfTestResult.Fail($"button state {_engine.UpdateState} before the first click, expected 'Check for updates'.");
        if (await ClickUpdateAsync(ct).ConfigureAwait(false) is { } clickError) return SelfTestResult.Fail(clickError);
        if (!await WaitAsync(() => _engine.UpdateState is not (WrapperUpdateState.NotChecked or WrapperUpdateState.Checking), _checkWait, ct)
                .ConfigureAwait(false))
            return SelfTestResult.Fail($"update check still {_engine.UpdateState} after {_checkWait.TotalSeconds:0} s.");

        var latest = _engine.LatestWrapperVersion;
        var problems = new List<string>();
        if (_engine.UpdateState != WrapperUpdateState.UpdateAvailable)
            problems.Add($"state {_engine.UpdateState} ({_engine.UpdateCheckError}), expected UpdateAvailable");
        if (latest == old) problems.Add($"latest release {latest} is the installed one");
        var label = WrapperUpdatePolicy.BuildVersionLabel(old, latest, Loc.S("Wrapper"), Loc.S("latest"));
        if (await ShownAsync([Loc.S("Update"), label], ct).ConfigureAwait(false) is { } missing) problems.Add(missing);
        return FromProblems(problems, $"check offered {latest} over {old}; Backend tab shows '{label}'.");
    }

    // ── update ──────────────────────────────────────────────────────────────

    /// <summary>Second click: stop + update to the release the check found, ready again, voices kept, button back to "Check for updates".</summary>
    private async Task<SelfTestResult> UpdateAsync(E2eContext ctx, string old, CancellationToken ct)
    {
        var latest = _engine.LatestWrapperVersion;
        var voicesBefore = await _wait.WaitForVoicesAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        string? error = null;
        async Task Update()
        {
            error = await ClickUpdateAsync(ct).ConfigureAwait(false);
            if (error != null) return;
            if (!await WaitAsync(() => _engine.Installing, _checkWait, ct).ConfigureAwait(false))
            {
                error = $"no update started within {_checkWait.TotalSeconds:0} s of the click (state {_engine.UpdateState})";
                return;
            }
            error = await _wait.WaitForReadyAsync(ctx, "update", _updateWait, ct).ConfigureAwait(false);
        }
        var problems = new List<string>();
        if (_frames == null) await Update().ConfigureAwait(false);
        else
        {
            var op = await _frames.MeasureAsync("update", Update).ConfigureAwait(false);
            ctx.State.Telemetry.Ops.RemoveAll(o => o.Op == op.Op);
            ctx.State.Telemetry.Ops.Add(op);
            ctx.Save();
            foreach (var freeze in ctx.State.Telemetry.Freezes().Where(o => o.Op == op.Op))
                problems.Add($"update froze the game ({freeze.Frames.MaxMs:0} ms frame > {ctx.State.Telemetry.FreezeLimitMs:0} ms)");
        }
        if (error != null) return SelfTestResult.Fail(error);

        if (_config.EchokrauTts.InstalledWrapperVersion != latest)
            problems.Add($"recorded wrapper {_config.EchokrauTts.InstalledWrapperVersion}, expected {latest}");
        if (E2eMixedWrapperChecks.DownloadProblem(Modes(), "updateechokrautts", latest) is { } download) problems.Add(download);
        var voicesAfter = await _wait.WaitForVoicesAsync(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
        if (voicesAfter != voicesBefore) problems.Add($"{voicesBefore} voices before the update, {voicesAfter} after");
        if (_engine.UpdateState != WrapperUpdatePolicy.StateAfterInstall())
            problems.Add($"button state {_engine.UpdateState} after the update");
        if (await ShownAsync([Loc.S("Check for updates")], ct).ConfigureAwait(false) is { } missing) problems.Add(missing);
        return FromProblems(problems, $"{old} -> {latest}, ready, {voicesAfter} voices kept.");
    }

    // ── unlocked ────────────────────────────────────────────────────────────

    /// <summary>
    /// After the update the formerly blocked entries are plain, and picking one switches the engine for real (it starts
    /// with that <c>--tts-backend</c>). The pick goes to the first blocked engine the request lists (else the first
    /// blocked one); the original engine is picked back at the end.
    /// </summary>
    private async Task<SelfTestResult> UnlockedAsync(E2eContext ctx, IReadOnlyList<TtsEngineInfo> blocked, CancellationToken ct)
    {
        var options = Options();
        var problems = E2eMixedWrapperChecks.EntryProblems(options, [], UnavailableFormat).ToList();
        if (await RenderedProblemAsync(options, ct).ConfigureAwait(false) is { } rendered) problems.Add(rendered);
        if (problems.Count > 0) return FromProblems(problems, $"{options.Count} entries after the update.");

        var target = ctx.Request.Engines.Select(id => blocked.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(b => b != null) ?? blocked[0];
        var original = _config.EchokrauTts.TtsBackendArg;
        var switched = await SwitchByPickAsync(ctx, target.Id, ct).ConfigureAwait(false);
        var back = await SwitchByPickAsync(ctx, original, ct).ConfigureAwait(false);
        if (switched != null) problems.Add(switched);
        if (back != null) problems.Add($"back to {original}: {back}");
        return FromProblems(problems, $"{blocked.Count} entries unlocked; picked {target.Id} (started), then {original} again.");
    }

    private async Task<string?> SwitchByPickAsync(E2eContext ctx, string engineId, CancellationToken ct)
    {
        if (await PickAsync(engineId, ct).ConfigureAwait(false) is { } pickError) return $"pick {engineId}: {pickError}";
        if (!await WaitAsync(() => _config.EchokrauTts.TtsBackendArg == engineId, _checkWait, ct).ConfigureAwait(false))
            return $"pick {engineId} refused (engine still {_config.EchokrauTts.TtsBackendArg})";
        // The restart may not have begun yet; wait for it to start before waiting for ready.
        await WaitAsync(() => _engine.InstanceStarting || _engine.Installing || !_engine.InstanceRunning, _checkWait, ct).ConfigureAwait(false);
        if (await _wait.WaitForReadyAsync(ctx, $"switch:{engineId}", _switchWait, ct).ConfigureAwait(false) is { } readyError)
            return readyError;
        var last = Modes().LastOrDefault();
        return last?.Get("ttsBackend") == engineId ? null : $"last installer start ran ttsBackend={last?.Get("ttsBackend") ?? "-"}, not {engineId}";
    }

    // ── UI helpers ──────────────────────────────────────────────────────────

    /// <summary>The engine dropdown as rendered equals <paramref name="options"/> (labels, order, selected entry).</summary>
    private async Task<string?> RenderedProblemAsync(IReadOnlyList<TtsEngineOption> options, CancellationToken ct)
    {
        var read = await _tab.ReadEngineDropdownAsync(ct).ConfigureAwait(false);
        if (read.Error != null) return read.Error;
        var selected = TtsEngineAvailability.LabelFor(options, _config.EchokrauTts.TtsBackendArg);
        return E2eUiChecks.DropdownProblem(read.Closed!.Texts, read.Open!.Texts, options.Select(o => o.Label).ToList(), selected)
            is { } problem ? "rendered dropdown: " + problem : null;
    }

    /// <summary>Texts of the expanded Backend tab (dropdown closed), or null when it could not be read.</summary>
    private async Task<IReadOnlyList<string>?> ClosedLabelAsync(CancellationToken ct)
    {
        var opened = await _tab.OpenAsync(expand: true, ct).ConfigureAwait(false);
        try
        {
            return opened.State?.Texts.Select(t => t.Text).ToList();
        }
        finally
        {
            await _tab.FoldBackAsync(opened.Expanded, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Null when the expanded Backend tab shows every text within the check wait (it updates per frame), else which are missing.</summary>
    private async Task<string?> ShownAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var opened = await _tab.OpenAsync(expand: true, ct).ConfigureAwait(false);
        if (opened.Error != null) return opened.Error;
        try
        {
            var state = await _ui.WaitForAsync(OwnedWindowIds.Config, s => texts.All(t => s.Shows(t)), _checkWait, ct).ConfigureAwait(false);
            if (state != null) return null;
            var last = await _ui.StateAsync(OwnedWindowIds.Config).ConfigureAwait(false);
            return $"Backend tab does not show {string.Join(", ", texts.Where(t => last?.Shows(t) != true).Select(t => $"'{t}'"))}";
        }
        finally
        {
            await _tab.FoldBackAsync(opened.Expanded, ct).ConfigureAwait(false);
        }
    }

    /// <summary>A click on the wrapper button, on the open Backend tab like a user.</summary>
    private Task<string?> ClickUpdateAsync(CancellationToken ct) => RunActionAsync(OwnedWindowActions.WrapperUpdate, ct);

    private Task<string?> PickAsync(string engineId, CancellationToken ct) => RunActionAsync(OwnedWindowActions.EnginePick(engineId), ct);

    private async Task<string?> RunActionAsync(string action, CancellationToken ct)
    {
        var opened = await _tab.OpenAsync(expand: true, ct).ConfigureAwait(false);
        if (opened.Error != null) return opened.Error;
        try
        {
            return await _ui.StepAsync(OwnedWindowIds.Config, action, ct).ConfigureAwait(false);
        }
        finally
        {
            await _tab.FoldBackAsync(opened.Expanded, ct).ConfigureAwait(false);
        }
    }

    private Task<bool> WaitAsync(Func<bool> condition, TimeSpan wait, CancellationToken ct) => WaitUntilAsync(condition, wait, _poll, ct);
}
