using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>Stage <c>wrapper-mixed</c> against a simulated Backend tab and wrapper button (plan › B8).</summary>
public class MixedWrapperStageTests
{
    private const string Old = "0.0.1.9";
    private const string Latest = "0.0.2.0";

    private static readonly List<TtsEngineInfo> Catalog =
    [
        new() { Id = "qwen-0.6b", Name = "Qwen 0.6B", MinVersion = Latest },
        new() { Id = "xtts", Name = "XTTS", MinVersion = "0.0.0.1" },
        new() { Id = "pocket", Name = "Pocket", MinVersion = Latest },
    ];

    private readonly FakeWindows _windows = new();
    private readonly Configuration _config = new()
    {
        FirstTime = false,
        BackendSelection = TTSBackends.EchokrauTTS,
        TtsInstallRoot = @"E:\EkE2E",
        EchokrauTts = { InstanceType = AlltalkInstanceType.Local, LocalInstall = true, TtsEngine = "xtts", InstalledWrapperVersion = Old },
    };
    private readonly Mock<IEchokrauTtsInstanceService> _ek = new();
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly List<string> _log = [];
    private readonly List<E2eStageReport> _parts = [];

    private WrapperUpdateState _state = WrapperUpdateState.NotChecked;
    private string _latest = Old;
    private bool _running = true;
    private bool _pendingUpdate;
    private bool _expanded;
    private bool _dropdownOpen;
    private int _voices = 3;

    // What the simulated windows do — the fakes the tests break on purpose.
    private string _checkFinds = Latest;
    private bool _pickIgnoresAvailability;
    private int _voicesAfterUpdate = 3;

    public MixedWrapperStageTests()
    {
        _ek.SetupGet(e => e.AvailableEngines).Returns(Catalog);
        _ek.SetupGet(e => e.LatestWrapperVersion).Returns(() => _latest);
        _ek.SetupGet(e => e.UpdateState).Returns(() => _state);
        _ek.SetupGet(e => e.UpdateCheckError).Returns("");
        _ek.SetupGet(e => e.InstanceRunning).Returns(() => _running);
        _ek.SetupGet(e => e.CurrentInstallStatus).Returns("");
        _ek.SetupGet(e => e.Installing).Returns(() =>
        {
            if (!_pendingUpdate) return false;
            // The update runs while Installing is read: the new wrapper is on disk and serving afterwards.
            _pendingUpdate = false;
            _config.EchokrauTts.InstalledWrapperVersion = _latest;
            _state = WrapperUpdatePolicy.StateAfterInstall();
            _log.Add($"Mode: updateechokrautts | url=https://github.com/o/w/releases/download/{_latest}/EchokrauTTS.zip | ttsBackend=xtts");
            _voices = _voicesAfterUpdate;
            _running = true;
            Refresh();
            return true;
        });
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(true);
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(() =>
            Enumerable.Range(0, _voices).Select(_ => new EchokrautVoice { IsEnabled = true }).ToList());
        _log.Add($"Mode: echokrautts | url=https://github.com/o/w/releases/download/{Old}/EchokrauTTS.zip | ttsBackend=xtts");

        var config = _windows[OwnedWindowIds.Config];
        config.Steps[E2eBackendTab.TabStep] = Refresh;
        config.Steps[E2eBackendTab.EkLocalToggle] = () => { _expanded = !_expanded; Refresh(); };
        config.Steps["dropdown:engine:open"] = () => { _dropdownOpen = true; Refresh(); };
        config.Steps["dropdown:engine:close"] = () => { _dropdownOpen = false; Refresh(); };
        config.Steps[OwnedWindowActions.WrapperUpdate] = ClickWrapperButton;
        foreach (var engine in Catalog)
        {
            var id = engine.Id;
            config.Steps[OwnedWindowActions.EnginePick(id)] = () => Pick(id);
        }
    }

    private IReadOnlyList<TtsEngineOption> Options() =>
        TtsEngineAvailability.ForConfig(_ek.Object.AvailableEngines, _config.EchokrauTts, _latest, _ek.Object.ClientLanguageCode, listUnavailable: true, Loc.S);

    /// <summary>What the Backend tab shows, as the builder renders it.</summary>
    private void Refresh()
    {
        var texts = new List<string> { $"{(_expanded ? E2eBackendTab.ExpandedMark : E2eBackendTab.CollapsedMark)} {Loc.S("EchokrauTTS local instance")}" };
        if (_expanded)
        {
            var options = Options();
            texts.Add(TtsEngineAvailability.LabelFor(options, _config.EchokrauTts.TtsBackendArg));
            if (_dropdownOpen) texts.AddRange(options.Select(o => o.Label));
            texts.Add(_state == WrapperUpdateState.UpdateAvailable ? Loc.S("Update") : Loc.S("Check for updates"));
            texts.Add(_state == WrapperUpdateState.UpdateAvailable
                ? WrapperUpdatePolicy.BuildVersionLabel(_config.EchokrauTts.InstalledWrapperVersion, _latest, Loc.S("Wrapper"), Loc.S("latest"))
                : $"{Loc.S("Wrapper")}: {_config.EchokrauTts.InstalledWrapperVersion}");
        }
        _windows[OwnedWindowIds.Config].Texts = texts;
    }

    private void ClickWrapperButton()
    {
        if (_state == WrapperUpdateState.NotChecked)
        {
            _latest = _checkFinds;
            _state = _latest == _config.EchokrauTts.InstalledWrapperVersion ? WrapperUpdateState.UpToDate : WrapperUpdateState.UpdateAvailable;
        }
        else if (_state == WrapperUpdateState.UpdateAvailable)
        {
            _running = false;
            _pendingUpdate = true;
        }
        Refresh();
    }

    private void Pick(string id)
    {
        if (!_pickIgnoresAvailability && Options().FirstOrDefault(o => o.Id == id) is not { Available: true }) return;
        _config.EchokrauTts.TtsEngine = id;
        _log.Add($"Mode: echokrautts | url=(none) | ttsBackend={id}");
        Refresh();
    }

    private MixedWrapperStage Stage() =>
        new(_ek.Object, _config, new E2eUiDriver(_windows.Service, a => { a(); return Task.CompletedTask; }, settle: TimeSpan.Zero,
                poll: TimeSpan.FromMilliseconds(1), wait: TimeSpan.FromMilliseconds(200)),
            new E2eEngineWait(_ek.Object, _backend.Object, _npcData.Object, TimeSpan.FromMilliseconds(1)),
            _ => _log.ToList(), poll: TimeSpan.FromMilliseconds(1), checkWait: TimeSpan.FromMilliseconds(100),
            updateWait: TimeSpan.FromSeconds(5), switchWait: TimeSpan.FromSeconds(5), refuseWait: TimeSpan.Zero);

    private E2eContext Ctx(string? wrapperVersion = Old)
    {
        var state = E2eStateStoreTests.State();
        state.Request = state.Request with { WrapperVersion = wrapperVersion, Engines = ["pocket", "xtts"] };
        state.ConfigIsE2e = true;
        return new E2eContext(state, () => { }, _ => { }, report: _parts.Add);
    }

    private SelfTestOutcome? Part(string part) => _parts.FirstOrDefault(p => p.Id == MixedWrapperStage.PartId(part))?.Outcome;

    [Fact]
    public async Task NoWrapperVersion_Skips()
    {
        var r = await Stage().RunAsync(Ctx(wrapperVersion: null), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Empty(_parts);
    }

    [Fact]
    public async Task OldWrapper_GreyedThenUpdatedThenUnlocked_Passes()
    {
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.True(r.Outcome == SelfTestOutcome.Pass, string.Join(" || ", _parts.Select(p => $"{p.Id} {p.Outcome} {p.Detail}")));
        Assert.Equal(["installed", "greyed", "update-offered", "update", "unlocked"],
            _parts.Select(p => p.Id[(MixedWrapperStage.StageId.Length + 1)..]));
        Assert.Equal(Latest, _config.EchokrauTts.InstalledWrapperVersion);
        // The request lists pocket first: that one is picked, then the original engine again.
        Assert.Equal(["ttsBackend=pocket", "ttsBackend=xtts"], _log.TakeLast(2).Select(l => l.Split(" | ").Last()));
        Assert.Equal("xtts", _config.EchokrauTts.TtsBackendArg);
        Assert.False(_expanded); // folded back after every look
    }

    [Fact]
    public async Task APickOfABlockedEngineAccepted_FailsGreyed_AndBlocksTheRest()
    {
        _pickIgnoresAvailability = true;

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(SelfTestOutcome.Fail, Part("greyed"));
        Assert.Contains("pick qwen-0.6b accepted", _parts.Single(p => p.Id.EndsWith(":greyed")).Detail);
        Assert.Equal(SelfTestOutcome.Skipped, Part("update-offered"));
        Assert.Equal(Old, _config.EchokrauTts.InstalledWrapperVersion);
    }

    [Fact]
    public async Task CheckFindsNothingNewer_FailsUpdateOffered()
    {
        _checkFinds = Old;

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, Part("greyed"));
        Assert.Equal(SelfTestOutcome.Fail, Part("update-offered"));
        Assert.Contains("state UpToDate", _parts.Single(p => p.Id.EndsWith(":update-offered")).Detail);
        Assert.Equal(SelfTestOutcome.Skipped, Part("update"));
    }

    [Fact]
    public async Task UpdateLosesVoices_FailsUpdate()
    {
        _voicesAfterUpdate = 1;

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, Part("update"));
        Assert.Contains("3 voices before the update, 1 after", _parts.Single(p => p.Id.EndsWith(":update")).Detail);
        Assert.Equal(SelfTestOutcome.Skipped, Part("unlocked"));
    }

    [Fact]
    public async Task InstallFetchedAnotherRelease_FailsInstalled()
    {
        _log[0] = $"Mode: echokrautts | url=https://github.com/o/w/releases/download/{Latest}/EchokrauTTS.zip | ttsBackend=xtts";

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, Part("installed"));
        Assert.Contains($"not release {Old}", _parts.Single(p => p.Id.EndsWith(":installed")).Detail);
    }

    [Fact]
    public async Task ACatalogWithoutNewerEngines_SkipsGreyedAndUnlocked_ButChecksTheUpdate()
    {
        _config.EchokrauTts.InstalledWrapperVersion = "0.0.1.99";
        _log[0] = "Mode: echokrautts | url=https://github.com/o/w/releases/download/0.0.1.99/EchokrauTTS.zip | ttsBackend=xtts";
        var stage = Stage();
        var ctx = Ctx(wrapperVersion: "0.0.1.99");
        _ek.SetupGet(e => e.AvailableEngines).Returns(Catalog.Where(e => e.Id == "xtts").ToList());

        var r = await stage.RunAsync(ctx, CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Skipped, Part("greyed"));
        Assert.Equal(SelfTestOutcome.Pass, Part("update"));
        Assert.Equal(SelfTestOutcome.Skipped, Part("unlocked"));
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
    }
}
