using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest.E2e;

public class EngineStageTests
{
    private readonly Configuration _config = new();
    private readonly Mock<IEchokrauTtsInstanceService> _engine = new();
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly E2eState _state = E2eStateStoreTests.State();
    private readonly List<string> _switches = new();

    /// <summary>Engines whose start never gets ready (status Failed) / never ends (Starting forever).</summary>
    private readonly HashSet<string> _failing = new();
    private readonly HashSet<string> _hanging = new();
    private readonly Dictionary<string, SelfTestResult> _generation = new();
    private bool _running = true;
    private bool _starting;
    private string _status = "";
    private int _voices = 3;
    private long? _free = 100L * 1024 * 1024 * 1024;

    public EngineStageTests()
    {
        _state.ConfigIsE2e = true;
        _config.EchokrauTts.InstalledWrapperVersion = "0.0.2.0";
        _engine.SetupGet(e => e.AvailableEngines).Returns(() =>
        [
            new TtsEngineInfo { Id = "xtts" },
            new TtsEngineInfo { Id = "f5" },
            new TtsEngineInfo { Id = "moss", MinVersion = "0.0.3.0" },
        ]);
        _engine.SetupGet(e => e.InstanceRunning).Returns(() => _running);
        _engine.SetupGet(e => e.InstanceStarting).Returns(() => _starting);
        _engine.SetupGet(e => e.CurrentInstallStatus).Returns(() => _status);
        _engine.Setup(e => e.SwitchTtsBackend(It.IsAny<string>())).Callback((string id) =>
        {
            _switches.Add(id);
            if (_config.EchokrauTts.TtsBackendArg == id) return;
            _config.EchokrauTts.TtsEngine = id;
            if (_running || _starting) Start(id); // restart, like RestartLocalIfRunning
        });
        _engine.Setup(e => e.StartInstance()).Callback(() => Start(_config.EchokrauTts.TtsBackendArg));
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(true);
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(() =>
            Enumerable.Range(0, _voices).Select(_ => new EchokrautVoice { IsEnabled = true }).ToList());
    }

    private void Start(string id)
    {
        _running = !_failing.Contains(id) && !_hanging.Contains(id);
        _starting = _hanging.Contains(id);
        _status = _failing.Contains(id) ? "Failed: boom" : "";
    }

    private EngineStage Stage(bool withChecks = true) =>
        new(_engine.Object, _config, new E2eEngineWait(_engine.Object, _backend.Object, _npcData.Object, TimeSpan.FromMilliseconds(1)),
            withChecks ? new FakeCheck("generation", _ => Task.FromResult(
                _generation.GetValueOrDefault(_config.EchokrauTts.TtsBackendArg, SelfTestResult.Pass("ok")))) : null,
            withChecks ? FakeCheck.Returning("pipeline", SelfTestResult.Pass("played")) : null,
            () => _free, readyWait: TimeSpan.FromMilliseconds(200), voicesWait: TimeSpan.FromMilliseconds(30));

    private E2eContext Ctx() => new(_state, () => { }, _ => { });

    private E2eStageReport Part(string id) => _state.ResultOf(EngineStage.PartId(id))!;

    private void Request(params string[] engines) =>
        _state.Request = _state.Request with { Engines = engines };

    [Fact]
    public async Task RealConfig_IsSkipped_WithoutASwitch()
    {
        _state.ConfigIsE2e = false;
        Request("xtts");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Empty(_switches);
    }

    [Fact]
    public async Task EachEngine_PassesOnItsOwn_AndIsMarkedActive()
    {
        Request("xtts", "f5");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Part("xtts").Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Part("f5").Outcome);
        Assert.Equal(new[] { "xtts", "f5" }, _switches);
        Assert.Equal("f5", _state.Marks[EngineStage.ActiveEngineMark]);
    }

    [Fact]
    public async Task NotRunning_IsStartedAfterTheSwitch()
    {
        _running = false;
        Request("f5");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        _engine.Verify(e => e.StartInstance(), Times.Once);
        Assert.Equal(SelfTestOutcome.Pass, Part("f5").Outcome);
    }

    [Fact]
    public async Task MissingFromCatalog_And_TooNew_AreSkippedWithTheReason()
    {
        Request("pocket", "moss");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Contains("Not in the engine catalog", Part("pocket").Detail);
        Assert.Equal(SelfTestOutcome.Skipped, Part("pocket").Outcome);
        Assert.Contains("Needs wrapper 0.0.3.0, installed 0.0.2.0", Part("moss").Detail);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Empty(_switches);
    }

    [Fact]
    public async Task NoVoice_IsSkipped()
    {
        _voices = 0;
        Request("f5");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, Part("f5").Outcome);
        Assert.Contains("No enabled voice", Part("f5").Detail);
    }

    [Fact]
    public async Task FailedStart_Fails_ThenTheLastPassedEngineIsSwitchedBack()
    {
        _failing.Add("f5");
        Request("xtts", "f5");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, Part("f5").Outcome);
        Assert.Contains("Failed: boom", Part("f5").Detail);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(new[] { "xtts", "f5", "xtts" }, _switches);
        Assert.Equal("xtts", _config.EchokrauTts.TtsBackendArg);
        Assert.Equal("xtts", _state.Marks[EngineStage.ActiveEngineMark]);
    }

    [Fact]
    public async Task NeverReady_FailsOnTheReadyWait()
    {
        _hanging.Add("f5");
        Request("f5");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, Part("f5").Outcome);
        Assert.Contains("not ready after", Part("f5").Detail);
        Assert.False(_state.Marks.ContainsKey(EngineStage.ActiveEngineMark));
    }

    [Fact]
    public async Task FailedGeneration_FailsTheEngine()
    {
        _generation["f5"] = SelfTestResult.Fail("silence");
        Request("f5");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, Part("f5").Outcome);
        Assert.Contains("silence", Part("f5").Detail);
    }

    [Fact]
    public async Task LowSystemDrive_SkipsEveryFurtherEngine()
    {
        _free = 1L * 1024 * 1024 * 1024;
        Request("xtts", "f5");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Contains("System drive", Part("xtts").Detail);
        Assert.Equal(SelfTestOutcome.Skipped, Part("f5").Outcome);
        Assert.Empty(_switches);
    }

    [Fact]
    public async Task RestartedStage_KeepsReportedEngines()
    {
        _state.StageResults.Add(new E2eStageReport(EngineStage.PartId("xtts"), SelfTestOutcome.Pass, "earlier", 1));
        Request("xtts", "f5");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(new[] { "f5" }, _switches);
        Assert.Single(_state.StageResults, x => x.Id == EngineStage.PartId("xtts"));
    }

    [Fact]
    public async Task MissingChecks_Fail()
    {
        Request("xtts");
        var r = await Stage(withChecks: false).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
    }

    // ── scenarios depend on the last passed engine [M4] ──────────────────────────────────────────

    private static ScenariosStage Scenarios() => new([], new Configuration());

    [Fact]
    public void Scenarios_WithoutEngineStageInTheRun_NeedChecksLocal()
    {
        _state.Request = _state.Request with { Stages = ["checks:local", "scenarios"] };
        Assert.Equal("needs checks:local (not run)", Scenarios().UnmetNeed(_state));
        _state.StageResults.Add(new E2eStageReport("checks:local", SelfTestOutcome.Pass, "", 1));
        Assert.Null(Scenarios().UnmetNeed(_state));
    }

    [Fact]
    public void Scenarios_WithEngineStage_NeedAPassedEngineThatIsActive()
    {
        _state.StageResults.Add(new E2eStageReport("checks:local", SelfTestOutcome.Pass, "", 1));
        Assert.Contains("none passed", Scenarios().UnmetNeed(_state));

        _state.StageResults.Add(new E2eStageReport(EngineStage.PartId("xtts"), SelfTestOutcome.Pass, "", 1));
        _state.StageResults.Add(new E2eStageReport(EngineStage.PartId("f5"), SelfTestOutcome.Fail, "", 1));
        Assert.Contains("switch back failed", Scenarios().UnmetNeed(_state));

        _state.Marks[EngineStage.ActiveEngineMark] = "xtts";
        Assert.Null(Scenarios().UnmetNeed(_state));
    }
}
