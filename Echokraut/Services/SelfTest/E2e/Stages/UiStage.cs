using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage <c>ui</c> (plan docs/plans/ingame-e2e.md › B5): the plugin's own windows as a user sees them, through
/// <see cref="E2eUiDriver"/> (probes on the framework thread, never game UI). Parts: commands, the alias,
/// Backend tab close + reopen, engine dropdown texts, wizard sections + "Test connection", a sweep over every
/// window's side-effect-free steps under the frame watchdog, the screen clamp, untranslated texts in a
/// non-English client, and the changelog popup across two self-reloads (last, it restarts the stage).
/// Only on an E2E config; every setting a part touches is put back by that part.
/// </summary>
public sealed partial class UiStage : E2eStage
{
    public const string StageId = "ui";
    public const string ChangelogMark = "ui-changelog";
    public const string ChangelogLastSeenMark = "ui-changelog-last-seen";
    public const string ReloadMark = "ui-reload";
    /// <summary>The alias the tester checklist names (<c>inst-cmd</c>: "Both /echokraut and /ek open the window").</summary>
    public const string Alias = "/echokraut";

    private readonly E2eUiDriver _ui;
    private readonly E2eBackendTab _backendTab;
    private readonly Func<string, Task<bool>> _processCommand;
    private readonly Func<IReadOnlyCollection<string>> _registeredCommands;
    private readonly Configuration _config;
    private readonly IFirstTimeWizardController _wizard;
    private readonly IChangelogService _changelog;
    private readonly IEchokrauTtsInstanceService _engine;
    private readonly E2eSelfReload? _reload;
    private readonly E2eEngineWait? _engineWait;
    private readonly Func<Action, Task> _onFramework;
    private readonly Func<ClientLanguage> _language;
    private readonly string _pluginVersion;
    private readonly E2eFrameMonitor? _frames;
    private readonly TimeSpan _poll;
    private readonly TimeSpan _absentWait;

    /// <summary>Every text the parts saw (for the untranslated-text part); lost on a reload, which comes last.</summary>
    private readonly List<string> _seenTexts = [];

    public UiStage(E2eUiDriver ui, Func<string, Task<bool>> processCommand, Func<IReadOnlyCollection<string>> registeredCommands,
        Configuration config, IFirstTimeWizardController wizard, IChangelogService changelog, IEchokrauTtsInstanceService engine,
        E2eSelfReload? reload, E2eEngineWait? engineWait, Func<Action, Task> onFramework, Func<ClientLanguage> language,
        string pluginVersion, E2eFrameMonitor? frames = null, TimeSpan? poll = null, TimeSpan? absentWait = null)
    {
        _ui = ui;
        _backendTab = new E2eBackendTab(ui, config);
        _processCommand = processCommand;
        _registeredCommands = registeredCommands;
        _config = config;
        _wizard = wizard;
        _changelog = changelog;
        _engine = engine;
        _reload = reload;
        _engineWait = engineWait;
        _onFramework = onFramework;
        _language = language;
        _pluginVersion = pluginVersion;
        _frames = frames;
        _poll = poll ?? TimeSpan.FromMilliseconds(250);
        _absentWait = absentWait ?? TimeSpan.FromSeconds(10);
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [FirstTimeStage.StageId];
    public override TimeSpan Timeout => TimeSpan.FromMinutes(6) + (_reload?.StageTimeout ?? TimeSpan.Zero);

    public static string PartId(string part) => $"{StageId}:{part}";

    private sealed record Part(string Id, Func<E2eContext, CancellationToken, Task<SelfTestResult>> Run);

    private IEnumerable<Part> Parts() =>
    [
        new("commands", CommandsAsync),
        new("alias", (_, _) => Task.FromResult(AliasResult())),
        new("backend-reopen", BackendReopenAsync),
        new("engine-dropdown", EngineDropdownAsync),
        new("wizard", WizardAsync),
        new("sweep", SweepAsync),
        new("clamp", ClampAsync),
        new("loc", (_, _) => Task.FromResult(LocResult())),
        new("changelog", ChangelogAsync),
    ];

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");
        if (_config.FirstTime) return SelfTestResult.Fail("FirstTime is still true - the wizard was not finished.");

        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        foreach (var part in Parts())
        {
            var id = PartId(part.Id);
            // A stage restarted by its own reload keeps the parts it already reported.
            if (ctx.State.ResultOf(id) is { } done)
            {
                outcomes.Add((id, done.Outcome));
                continue;
            }
            var started = DateTime.UtcNow;
            SelfTestResult result;
            try
            {
                result = await part.Run(ctx, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = SelfTestResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
            ct.ThrowIfCancellationRequested();
            // Whatever a part left open must not stand in the next part's way (or survive a reload).
            await CloseAllAsync(ct).ConfigureAwait(false);
            ctx.ReportPart(new E2eStageReport(id, result.Outcome, result.Detail, (long)(DateTime.UtcNow - started).TotalMilliseconds));
            outcomes.Add((id, result.Outcome));
        }

        var (summary, failed, passed) = SummarizeParts(outcomes);
        if (failed.Count > 0) return SelfTestResult.Fail($"{summary}: {string.Join(", ", failed)}.");
        return passed > 0 ? SelfTestResult.Pass(summary + ".") : SelfTestResult.Skip(summary + ".");
    }

    // ── commands ────────────────────────────────────────────────────────────

    /// <summary>The window commands (each opens, then closes its window) and the voicing toggles (flip, flip back).</summary>
    private async Task<SelfTestResult> CommandsAsync(E2eContext ctx, CancellationToken ct)
    {
        var problems = new List<string>();
        (string Command, string Window)[] windows =
        [
            ("/ek", OwnedWindowIds.VoiceClipManager),
            ("/ekconfig", OwnedWindowIds.Config),
            ("/ekfirst", OwnedWindowIds.FirstTime),
            ("/ekdata", OwnedWindowIds.GameDataTools),
        ];
        await CloseAllAsync(ct).ConfigureAwait(false);
        foreach (var (command, window) in windows)
        {
            if (!await _processCommand(command).ConfigureAwait(false)) { problems.Add($"{command} not handled"); continue; }
            var open = await _ui.WaitForAsync(window, w => w.IsOpen, _ui.Wait, ct).ConfigureAwait(false);
            if (open == null) { problems.Add($"{command} did not open {window}"); continue; }
            Seen(open);
            await _ui.SettleAsync(ct).ConfigureAwait(false);
            await _processCommand(command).ConfigureAwait(false);
            if (await _ui.WaitForAsync(window, w => !w.Alive, _ui.Wait, ct).ConfigureAwait(false) == null)
                problems.Add($"{command} a second time did not close {window}");
        }

        (string Command, Func<bool> Get)[] toggles =
        [
            ("/ekt", () => _config.Enabled),
            ("/ekttalk", () => _config.VoiceDialogue),
            ("/ektbtalk", () => _config.VoiceBattleDialogue),
            ("/ektbubble", () => _config.VoiceBubble),
            ("/ektchat", () => _config.VoiceChat),
            ("/ektcutschoice", () => _config.VoicePlayerChoicesCutscene),
            ("/ektchoice", () => _config.VoicePlayerChoices),
        ];
        foreach (var (command, get) in toggles)
        {
            var before = get();
            if (!await _processCommand(command).ConfigureAwait(false)) { problems.Add($"{command} not handled"); continue; }
            var flipped = get();
            await _processCommand(command).ConfigureAwait(false);
            var back = get();
            if (flipped == before) problems.Add($"{command} did not flip ({before})");
            if (back != before) problems.Add($"{command} twice left {back} instead of {before}");
        }
        return problems.Count == 0
            ? SelfTestResult.Pass($"{windows.Length} window commands open + close, {toggles.Length} toggles flip and flip back.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }

    private SelfTestResult AliasResult()
    {
        var registered = _registeredCommands();
        return registered.Contains(Alias)
            ? SelfTestResult.Pass($"{Alias} is registered.")
            : SelfTestResult.Fail($"{Alias} is not registered (tester checklist inst-cmd expects it next to /ek); /ek* registered: {string.Join(" ", registered.Where(c => c.StartsWith("/ek", StringComparison.Ordinal)).OrderBy(c => c, StringComparer.Ordinal))}.");
    }

    // ── Backend tab close + reopen ─────────────────────────────────────────

    /// <summary>
    /// Backend tab (EchokrauTTS local section expanded) → labels → close → reopen → same labels and the section
    /// still expanded; engine-only options only for the active engine. The section is folded back as it was.
    /// </summary>
    private async Task<SelfTestResult> BackendReopenAsync(E2eContext ctx, CancellationToken ct)
    {
        var labels = LocalizedLabels();
        var first = await BackendTabAsync(expand: true, ct).ConfigureAwait(false);
        if (first.Error != null) return SelfTestResult.Fail(first.Error);
        try
        {
            if (await _ui.CloseAsync(OwnedWindowIds.Config, ct).ConfigureAwait(false) is { } closeError) return SelfTestResult.Fail(closeError);
            var second = await BackendTabAsync(expand: false, ct).ConfigureAwait(false);
            if (second.Error != null) return SelfTestResult.Fail("after reopen: " + second.Error);

            var problems = new List<string>();
            var before = E2eUiChecks.StaticLabels(first.State!.Texts, labels);
            var after = E2eUiChecks.StaticLabels(second.State!.Texts, labels);
            if (E2eUiChecks.LabelDiff(before, after) is { } diff) problems.Add("after close + reopen " + diff);
            problems.AddRange(SectionProblems(second.State));
            // How much the probe saw: zero labels means the reading went wrong, not the window.
            return problems.Count == 0
                ? SelfTestResult.Pass($"{after.Count} labels identical after close + reopen; {ModeLabel()} section and only {_config.EchokrauTts.TtsBackendArg}'s options shown.")
                : SelfTestResult.Fail($"{string.Join("; ", problems)} ({second.State.Texts.Count} texts, {after.Count} labels seen).");
        }
        finally
        {
            await FoldBackAsync(first.Expanded, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Config window → Backend tab (see <see cref="E2eBackendTab.OpenAsync"/>); its texts count as seen.</summary>
    private async Task<(OwnedWindowState? State, string? Error, bool Expanded)> BackendTabAsync(bool expand, CancellationToken ct)
    {
        var opened = await _backendTab.OpenAsync(expand, ct).ConfigureAwait(false);
        if (opened.State != null) Seen(opened.State);
        return (opened.State, opened.Error, opened.Expanded);
    }

    private Task FoldBackAsync(bool expandedByStage, CancellationToken ct) => _backendTab.FoldBackAsync(expandedByStage, ct);

    private static OwnedWindowText? Header(OwnedWindowState state, string mark) => E2eBackendTab.Header(state, mark);

    private const string CollapsedMark = E2eBackendTab.CollapsedMark;
    private const string ExpandedMark = E2eBackendTab.ExpandedMark;

    /// <summary>What the Backend tab must and must not show for EchokrauTTS in its current mode and engine (local section expanded).</summary>
    private IEnumerable<string> SectionProblems(OwnedWindowState state)
    {
        var engineId = _config.EchokrauTts.TtsBackendArg;
        var local = _config.EchokrauTts.InstanceType == AlltalkInstanceType.Local;
        if (!local) yield break;
        var remoteHeader = Loc.S("EchokrauTTS remote connection");
        if (state.Texts.Any(t => t.Text.EndsWith(remoteHeader, StringComparison.Ordinal))) yield return $"remote section '{remoteHeader}' shown in Local mode";
        if (Header(state, ExpandedMark) == null)
        {
            yield return Header(state, CollapsedMark) != null
                ? "local section collapsed again after close + reopen"
                : "local section header not shown";
            yield break;
        }
        var install = Loc.S(_config.EchokrauTts.LocalInstall ? "Reinstall" : "Install");
        if (!state.Shows(install)) yield return $"local section missing ('{install}' not shown)";
        (EngineOptionGroup Group, string Label)[] options =
        [
            (EngineOptionGroup.Pocket, Loc.S("Pocket TTS: model size")),
            (EngineOptionGroup.Qwen, Loc.S("Qwen: calmer delivery (temperature 0.7 instead of 0.9)")),
        ];
        foreach (var (group, label) in options)
        {
            var shown = state.Shows(label);
            var applies = EngineOptionScope.AppliesTo(group, engineId);
            // Qwen's checkbox exists only with a GPU, so only "shown although not its engine" is judged for it.
            if (shown && !applies) yield return $"'{label}' shown for engine {engineId}";
            if (!shown && applies && group == EngineOptionGroup.Pocket) yield return $"'{label}' missing for engine {engineId}";
        }
    }

    private string ModeLabel() => _config.EchokrauTts.InstanceType.ToString();

    // ── engine dropdown ─────────────────────────────────────────────────────

    private async Task<SelfTestResult> EngineDropdownAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!_config.EchokrauTts.LocalInstall) return SelfTestResult.Skip("No local EchokrauTTS install - the engine dropdown lives in its section.");
        var read = await _backendTab.ReadEngineDropdownAsync(ct).ConfigureAwait(false);
        if (read.Closed != null) Seen(read.Closed);
        if (read.Error != null) return SelfTestResult.Fail(read.Error);
        var (closed, open) = (read.Closed!, read.Open!);
        Seen(open);

        var options = TtsEngineAvailability.ForConfig(_engine.AvailableEngines, _config.EchokrauTts,
            _engine.LatestWrapperVersion, _engine.ClientLanguageCode, listUnavailable: true, Loc.S);
        var expected = options.Select(o => o.Label).ToList();
        var selected = TtsEngineAvailability.LabelFor(options, _config.EchokrauTts.TtsBackendArg);
        var problem = E2eUiChecks.DropdownProblem(closed.Texts, open.Texts, expected, selected);
        return problem == null
            ? SelfTestResult.Pass($"{expected.Count} engines as in the catalog, best first, none cut off; selected '{selected}'.")
            : SelfTestResult.Fail($"{problem} ({closed.Texts.Count} texts closed, {open.Texts.Count} open).");
    }

    // ── wizard ──────────────────────────────────────────────────────────────

    /// <summary>
    /// /ekfirst on a finished config: back to step 0 (both engines offered) → Remote → step 1 shows only the
    /// EchokrauTTS remote section → "Test connection" against the configured (local) URL → Next → summary.
    /// The mode is put back to what it was in every case.
    /// </summary>
    private async Task<SelfTestResult> WizardAsync(E2eContext ctx, CancellationToken ct)
    {
        var mode = _config.EchokrauTts.InstanceType;
        var engine = _config.BackendSelection;
        var problems = new List<string>();
        try
        {
            if (await _ui.OpenAsync(OwnedWindowIds.FirstTime, ct).ConfigureAwait(false) is { } openError) return SelfTestResult.Fail(openError);
            for (var i = 0; i < 3 && _wizard.Step > 0; i++)
                await _ui.StepAsync(OwnedWindowIds.FirstTime, "back", ct).ConfigureAwait(false);
            var step0 = await _ui.StateAsync(OwnedWindowIds.FirstTime).ConfigureAwait(false);
            if (step0 == null || _wizard.Step != 0) return SelfTestResult.Fail($"wizard not on step 0 after Back (step {_wizard.Step}).");
            Seen(step0);
            foreach (var label in new[] { Loc.S("Welcome to Echokraut!"), "AllTalk", "EchokrauTTS" })
                if (!step0.Shows(label)) problems.Add($"step 0 does not show '{label}'");

            await _onFramework(() =>
            {
                _wizard.ChooseEngine(TTSBackends.EchokrauTTS);
                _wizard.ChooseMode(AlltalkInstanceType.Remote);
            }).ConfigureAwait(false);
            await _ui.SettleAsync(ct).ConfigureAwait(false);
            var step1 = await _ui.StateAsync(OwnedWindowIds.FirstTime).ConfigureAwait(false);
            if (step1 == null) return SelfTestResult.Fail("wizard closed after choosing Remote.");
            Seen(step1);
            if (!step1.Shows(Loc.S("Remote Server"))) problems.Add("step 1 title is not 'Remote Server'");
            // The URL field's label is only its placeholder (hidden while a URL is set): the section's Test button marks it.
            if (!step1.Shows(Loc.S("Test"))) problems.Add("EchokrauTTS remote section not shown (no 'Test' button)");
            foreach (var local in new[] { Loc.S("Install"), Loc.S("Reinstall") })
                if (step1.Shows(local)) problems.Add($"local section shown in Remote mode ('{local}')");

            var tested = await TestConnectionAsync(ct).ConfigureAwait(false);
            if (tested != null) problems.Add(tested);
            else
            {
                var next = false;
                await _onFramework(() => next = _wizard.Next()).ConfigureAwait(false);
                await _ui.SettleAsync(ct).ConfigureAwait(false);
                var step2 = await _ui.StateAsync(OwnedWindowIds.FirstTime).ConfigureAwait(false);
                if (!next || step2 == null) problems.Add($"Next refused after a successful test (step {_wizard.Step})");
                else
                {
                    Seen(step2);
                    if (!step2.Texts.Any(t => t.Text.Contains("EchokrauTTS", StringComparison.Ordinal)))
                        problems.Add("step 2 summary does not name EchokrauTTS");
                }
            }
        }
        finally
        {
            await _onFramework(() =>
            {
                _wizard.ChooseEngine(engine);
                _wizard.ChooseMode(mode);
            }).ConfigureAwait(false);
            await _ui.CloseAsync(OwnedWindowIds.FirstTime, ct).ConfigureAwait(false);
        }
        if (_config.EchokrauTts.InstanceType != mode || _config.BackendSelection != engine)
            problems.Add($"mode not put back ({_config.BackendSelection}/{_config.EchokrauTts.InstanceType})");
        if (_config.FirstTime) problems.Add("FirstTime true again");
        return problems.Count == 0
            ? SelfTestResult.Pass("Step 0 offers both engines; Remote shows only the EchokrauTTS remote section; 'Test connection' succeeds against the configured URL; Next -> summary; mode put back.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }

    /// <summary>Presses "Test connection" and waits for the result label; null when it reads "Connection successful".</summary>
    private async Task<string?> TestConnectionAsync(CancellationToken ct)
    {
        if (await _ui.StepAsync(OwnedWindowIds.FirstTime, "test-connection", ct).ConfigureAwait(false) is { } error) return error;
        var success = Loc.S("Connection successful");
        var errorPrefix = string.Format(Loc.S("Error: {0}"), "");
        var state = await _ui.WaitForAsync(OwnedWindowIds.FirstTime,
            w => !_wizard.RemoteTestRunning && (w.Shows(success) || w.Texts.Any(t => t.Text.StartsWith(errorPrefix, StringComparison.Ordinal))),
            _ui.Wait * 1.5, ct).ConfigureAwait(false);
        if (state == null) return $"no test result within {(_ui.Wait * 1.5).TotalSeconds:0} s";
        Seen(state);
        if (!state.Shows(success))
            return "test connection: " + (state.Texts.FirstOrDefault(t => t.Text.StartsWith(errorPrefix, StringComparison.Ordinal))?.Text ?? "?");
        return _wizard.CanAdvance ? null : "test succeeded but Next stays locked";
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Every translation key in the client language: the fixed labels a window can show.</summary>
    private static HashSet<string> LocalizedLabels() => Loc.Keys.Select(Loc.S).ToHashSet(StringComparer.Ordinal);

    private void Seen(OwnedWindowState state)
    {
        lock (_seenTexts) _seenTexts.AddRange(state.Texts.Select(t => t.Text));
    }

    private async Task CloseAllAsync(CancellationToken ct)
    {
        foreach (var w in await _ui.SnapshotAsync().ConfigureAwait(false))
            if (w.Alive) await _ui.CloseAsync(w.Id, ct).ConfigureAwait(false);
    }
}
