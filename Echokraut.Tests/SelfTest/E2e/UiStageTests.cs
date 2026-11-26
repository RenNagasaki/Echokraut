using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>Stage <c>ui</c> against a simulated set of own windows (the window manager's side of the probes).</summary>
public class UiStageTests
{
    // As Plugin.PluginVersion spells it (with "v"); the changelog resources and entries name it "v0.19.3.3" too.
    private const string Version = "v0.19.3.3";

    private readonly FakeWindows _windows = new();
    private readonly Configuration _config = new()
    {
        FirstTime = false,
        BackendSelection = TTSBackends.EchokrauTTS,
        LastSeenChangelogVersion = Version,
        EchokrauTts = { InstanceType = AlltalkInstanceType.Local, LocalInstall = true, TtsEngine = "xtts", AutoStartLocalInstance = false },
    };
    private readonly Mock<IEchokrauTtsInstanceService> _ek = new();
    private readonly Mock<IAlltalkInstanceService> _at = new();
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<IBatchModeService> _batch = new();
    private readonly List<string> _commandsRun = [];
    private readonly HashSet<string> _registered = ["/ek", "/ekconfig", "/ekfirst", "/ekdata", "/ekt"];
    private ClientLanguage _language = ClientLanguage.English;
    private bool _touched;
    private readonly FirstTimeWizardController _wizard;
    private readonly ChangelogService _changelog;

    public UiStageTests()
    {
        _wizard = new FirstTimeWizardController(_config, _at.Object, _ek.Object, _backend.Object, _batch.Object);
        var clientState = new Mock<IClientState>();
        clientState.Setup(c => c.ClientLanguage).Returns(ClientLanguage.English);
        _changelog = new ChangelogService(new Mock<ILogService>().Object, _config, clientState.Object, Version,
            ChangelogServiceTests.Asm(
                ("Echokraut.Resources.Changelogs.v0.19.3.2_EN.txt", "old"),
                ("Echokraut.Resources.Changelogs.v0.19.3.3_EN.txt", "new")));
    }

    private Task<bool> Process(string command)
    {
        _commandsRun.Add(command);
        string? window = command switch
        {
            "/ek" => OwnedWindowIds.VoiceClipManager,
            "/ekconfig" => OwnedWindowIds.Config,
            "/ekfirst" => OwnedWindowIds.FirstTime,
            "/ekdata" => OwnedWindowIds.GameDataTools,
            _ => null,
        };
        if (window != null) _windows[window].Open = !_windows[window].Open;
        switch (command)
        {
            case "/ekt": _config.Enabled = !_config.Enabled; break;
            case "/ekttalk": _config.VoiceDialogue = !_config.VoiceDialogue; break;
            case "/ektbtalk": _config.VoiceBattleDialogue = !_config.VoiceBattleDialogue; break;
            case "/ektbubble": _config.VoiceBubble = !_config.VoiceBubble; break;
            case "/ektchat": _config.VoiceChat = !_config.VoiceChat; break;
            case "/ektcutschoice": _config.VoicePlayerChoicesCutscene = !_config.VoicePlayerChoicesCutscene; break;
            case "/ektchoice": _config.VoicePlayerChoices = !_config.VoicePlayerChoices; break;
        }
        return Task.FromResult(true);
    }

    private UiStage Stage(string dll, Func<string, Task<bool>>? process = null) => new(
        new E2eUiDriver(_windows.Service, a => { a(); return Task.CompletedTask; }, TimeSpan.Zero, TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(200)),
        process ?? Process, () => _registered, _config, _wizard, _changelog, _ek.Object,
        new E2eSelfReload(_ek.Object, _at.Object, _config, dll, (_, _) => Task.FromResult(false),
            () => new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc), (_, _) => _touched = true,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5),
            _windows.Service, a => { a(); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(50)),
        null, a => { a(); return Task.CompletedTask; }, () => _language, Version,
        poll: TimeSpan.FromMilliseconds(1), absentWait: TimeSpan.FromMilliseconds(30));

    private static readonly string[] AllParts =
        ["commands", "alias", "backend-reopen", "engine-dropdown", "wizard", "sweep", "clamp", "loc", "changelog"];

    /// <summary>A state on an E2E config where every part except <paramref name="part"/> was already reported.</summary>
    private static E2eState OnlyPart(string part)
    {
        var s = E2eStateStoreTests.State();
        s.Phase = E2ePhase.Stages;
        s.ConfigIsE2e = true;
        s.StageResults.Add(new E2eStageReport(FirstTimeStage.StageId, SelfTestOutcome.Pass, "", 0));
        foreach (var p in AllParts.Where(p => p != part))
            s.StageResults.Add(new E2eStageReport(UiStage.PartId(p), SelfTestOutcome.Pass, "", 0));
        return s;
    }

    private async Task<E2eStageReport> RunPart(string part, E2eState? state = null, Func<string, Task<bool>>? process = null)
    {
        var dll = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
        File.WriteAllText(dll, "");
        try
        {
            state ??= OnlyPart(part);
            await Stage(dll, process).RunAsync(new E2eContext(state, () => { }, _ => { }), CancellationToken.None);
            return state.ResultOf(UiStage.PartId(part))!;
        }
        finally
        {
            File.Delete(dll);
        }
    }

    [Fact]
    public async Task RealConfig_IsSkipped_NothingRuns()
    {
        var state = OnlyPart("commands");
        state.ConfigIsE2e = false;

        var r = await Stage("x.dll").RunAsync(new E2eContext(state, () => { }, _ => { }), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Empty(_commandsRun);
    }

    // ── commands + alias ───────────────────────────────────────────────────

    [Fact]
    public async Task Commands_OpenCloseAndFlipBack_Pass()
    {
        var before = (_config.Enabled, _config.VoiceChat);

        var r = await RunPart("commands");

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(before, (_config.Enabled, _config.VoiceChat));
        Assert.All(_windows.Windows.Values, w => Assert.False(w.Open));
        Assert.Equal(2, _commandsRun.Count(c => c == "/ekfirst"));
    }

    [Fact]
    public async Task Commands_ToggleWithoutEffect_AndWindowThatStaysShut_Fail()
    {
        Task<bool> Broken(string command) =>
            command is "/ektchat" or "/ekdata" ? Task.FromResult(true) : Process(command);

        var r = await RunPart("commands", process: Broken);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("/ektchat did not flip", r.Detail);
        Assert.Contains($"/ekdata did not open {OwnedWindowIds.GameDataTools}", r.Detail);
    }

    [Fact]
    public async Task Alias_Missing_FailsAndNamesIt()
    {
        var r = await RunPart("alias");

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("/echokraut is not registered", r.Detail);

        _registered.Add(UiStage.Alias);
        Assert.Equal(SelfTestOutcome.Pass, (await RunPart("alias")).Outcome);
    }

    // ── Backend tab ────────────────────────────────────────────────────────

    private bool _ekLocalExpanded;

    /// <summary>
    /// The Backend tab: <paramref name="contentForOpening"/> (per opening, 1-based) is what the collapsible
    /// EchokrauTTS local section shows while expanded; the toggle step flips it like the header button.
    /// </summary>
    private void BackendTab(Func<int, List<string>> contentForOpening, bool keepsExpandedOnReopen = true)
    {
        var config = _windows[OwnedWindowIds.Config];
        var openings = 0;
        void Render() => config.Texts =
            [$"{(_ekLocalExpanded ? "[-]" : "[+]")} EchokrauTTS local instance", .. _ekLocalExpanded ? contentForOpening(openings) : []];
        config.Steps["tab:settings/backend"] = () =>
        {
            openings++;
            if (!keepsExpandedOnReopen && openings > 1) _ekLocalExpanded = false;
            Render();
        };
        config.Steps["section:ek-local:toggle"] = () => { _ekLocalExpanded = !_ekLocalExpanded; Render(); };
    }

    [Fact]
    public async Task BackendReopen_SameLabels_Pass_AndFoldsTheSectionBack()
    {
        BackendTab(_ => ["Reinstall", "Start", "Engine"]);

        var r = await RunPart("backend-reopen");

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.False(_ekLocalExpanded);
    }

    [Fact]
    public async Task BackendReopen_SectionLostAfterReopen_AndForeignEngineOption_Fail()
    {
        BackendTab(n => n == 1
            ? ["Reinstall", "Start"]
            : ["Reinstall", "Pocket TTS: model size"]);

        var r = await RunPart("backend-reopen");

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("lost 'Start'", r.Detail);
        Assert.Contains("'Pocket TTS: model size' shown for engine xtts", r.Detail);
    }

    [Fact]
    public async Task BackendReopen_SectionCollapsedAfterReopen_Fails()
    {
        BackendTab(_ => ["Reinstall"], keepsExpandedOnReopen: false);

        var r = await RunPart("backend-reopen");

        Assert.Contains("local section collapsed again after close + reopen", r.Detail);
    }

    [Fact]
    public async Task BackendReopen_RemoteSectionInLocalMode_Fails()
    {
        BackendTab(_ => ["Reinstall", "[+] EchokrauTTS remote connection"]);

        var r = await RunPart("backend-reopen");

        Assert.Contains("remote section 'EchokrauTTS remote connection' shown in Local mode", r.Detail);
    }

    // ── wizard ─────────────────────────────────────────────────────────────

    private void Wizard(string testResult)
    {
        var w = _windows[OwnedWindowIds.FirstTime];
        w.Texts = ["Welcome to Echokraut!", "AllTalk", "EchokrauTTS", "Remote Server", "Test"];
        w.Steps["back"] = _wizard.Back;
        w.Steps["test-connection"] = () =>
        {
            var result = _wizard.TestConnectionAsync().GetAwaiter().GetResult();
            w.Texts.Add(result.Success ? "Connection successful" : $"Error: {result.Message}");
        };
    }

    [Fact]
    public async Task Wizard_FailedConnectionTest_FailsAndPutsTheModeBack()
    {
        Wizard("refused");
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ReturnsAsync("refused");

        var r = await RunPart("wizard");

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("test connection: Error: refused", r.Detail);
        Assert.Equal(AlltalkInstanceType.Local, _config.EchokrauTts.InstanceType);
        Assert.False(_config.FirstTime);
        Assert.False(_windows[OwnedWindowIds.FirstTime].Open);
    }

    [Fact]
    public async Task Wizard_ReadyBackend_ReachesTheSummary_ModeBack()
    {
        Wizard("Ready");
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ReturnsAsync("Ready");
        _windows[OwnedWindowIds.FirstTime].Texts.Add("Engine: EchokrauTTS");

        var r = await RunPart("wizard");

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(AlltalkInstanceType.Local, _config.EchokrauTts.InstanceType);
    }

    // ── clamp ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clamp_WindowThatCanBePushedOff_FailsWithItsId()
    {
        _windows[OwnedWindowIds.GameDataTools].Clamps = false;

        var r = await RunPart("clamp");

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains(OwnedWindowIds.GameDataTools, r.Detail);
        Assert.DoesNotContain(OwnedWindowIds.Config + " at", r.Detail);
        // Each window goes back where it was.
        Assert.Equal(new Vector2(100, 100), _windows[OwnedWindowIds.Config].Position);
    }

    [Fact]
    public async Task Clamp_AllClamped_Pass()
    {
        Assert.Equal(SelfTestOutcome.Pass, (await RunPart("clamp")).Outcome);
    }

    // ── sweep + loc ────────────────────────────────────────────────────────

    [Fact]
    public async Task Sweep_StepThatClosesItsWindow_FailsAndLocSeesItsTexts()
    {
        var config = _windows[OwnedWindowIds.Config];
        config.Texts = ["General", "Logs"];
        config.Steps["tab:logs"] = () => { };
        config.Steps["tab:x"] = () => config.Open = false;
        _windows[OwnedWindowIds.VoiceClipManager].Steps["tab:npcs"] = () => { };
        _windows[OwnedWindowIds.GameDataTools].Steps["dropdown:a:open"] = () => { };
        _language = ClientLanguage.German;
        var state = OnlyPart("sweep");
        state.StageResults.RemoveAll(r => r.Id == UiStage.PartId("loc"));

        var sweep = await RunPart("sweep", state);
        var loc = state.ResultOf(UiStage.PartId("loc"))!;

        Assert.Equal(SelfTestOutcome.Fail, sweep.Outcome);
        Assert.Contains($"{OwnedWindowIds.Config} closed after tab:x", sweep.Detail);
        Assert.Equal(SelfTestOutcome.Fail, loc.Outcome);
        Assert.Contains("'General' (translation 'Allgemein' not used)", loc.Detail);
    }

    [Fact]
    public async Task Loc_EnglishClient_Skips()
    {
        Assert.Equal(SelfTestOutcome.Skipped, (await RunPart("loc")).Outcome);
    }

    // ── changelog ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Changelog_Start_ArmsThePreviousVersionAndTouches_NoReload_PutsItBack()
    {
        var state = OnlyPart("changelog");

        var r = await RunPart("changelog", state);

        Assert.True(_touched);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("No reload", r.Detail);
        Assert.Equal(Version, _config.LastSeenChangelogVersion);
        Assert.DoesNotContain(UiStage.ChangelogMark, state.Marks.Keys);
    }

    [Fact]
    public async Task Changelog_AfterFirstReload_ReadsIt_MovesTheMarkerAndReloadsAgain()
    {
        var state = OnlyPart("changelog");
        state.Marks[UiStage.ChangelogMark] = "armed";
        state.Marks[UiStage.ChangelogLastSeenMark] = Version;
        _config.LastSeenChangelogVersion = "0.19.3.2";
        var popup = _windows[OwnedWindowIds.Changelog];
        popup.Open = true;
        popup.Texts = ["What's New", $"Echokraut {Version}"];
        popup.Steps["read"] = () => { _changelog.MarkAllSeen(); popup.Open = false; };

        var r = await RunPart("changelog", state);

        Assert.True(_touched);
        Assert.Equal(Version, _config.LastSeenChangelogVersion);
        Assert.False(popup.Open);
        // Only the missing reload fails it: title, version, the one unseen entry and "read" were all right.
        Assert.DoesNotContain("First reload", r.Detail);
    }

    private static E2eState AfterSecondReload(string firstProblems = "")
    {
        var state = OnlyPart("changelog");
        state.Marks[UiStage.ChangelogMark] = "read";
        state.Marks["ui-changelog-problems"] = firstProblems;
        state.Marks[UiStage.ChangelogLastSeenMark] = Version;
        return state;
    }

    [Fact]
    public async Task Changelog_AfterSecondReload_NothingOpens_Pass_MarksGone()
    {
        var state = AfterSecondReload();

        var r = await RunPart("changelog", state);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.DoesNotContain(UiStage.ChangelogMark, state.Marks.Keys);
        Assert.False(_touched);
    }

    [Fact]
    public async Task Changelog_AfterSecondReload_PopupAgain_OrEarlierProblem_Fail()
    {
        _windows[OwnedWindowIds.Changelog].Open = true;

        var r = await RunPart("changelog", AfterSecondReload("version 'Echokraut 0.19.3.3' not in the title bar"));

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("not in the title bar", r.Detail);
        Assert.Contains($"{OwnedWindowIds.Changelog} opened again", r.Detail);
    }
}
