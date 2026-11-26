using System;
using System.Collections.Generic;
using System.IO;
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

public class EngineStageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-engine-" + Guid.NewGuid().ToString("N"));
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
        _state.Request = _state.Request with { InstallRoot = _root };
        WriteVoicePack();
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
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
        // Same shape as RestartLocalIfRunning: persist, then restart a running engine.
        _engine.Setup(e => e.SetQwenTemperature(It.IsAny<double>())).Callback((double t) =>
        {
            _config.EchokrauTts.QwenTemperature = t;
            Restart();
        });
        _engine.Setup(e => e.SetPocketLayers(It.IsAny<int>())).Callback((int l) =>
        {
            _config.EchokrauTts.PocketLayers = l;
            Restart();
        });
        _engine.Setup(e => e.SetPocketDecodeSteps(It.IsAny<int>())).Callback((int st) =>
        {
            if (_config.EchokrauTts.TrySetPocketDecodeSteps(st)) Restart();
        });
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(true);
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(() =>
            Enumerable.Range(0, _voices).Select(_ => new EchokrautVoice { IsEnabled = true }).ToList());
        Start(_config.EchokrauTts.TtsBackendArg); // the first-time install's Mode line
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string LogPath => E2eInstallerLog.PathFor(_root);

    /// <summary>Overrides what the fake installer writes into its Mode line (a plugin that drops an arg).</summary>
    private readonly Dictionary<string, string> _logOverride = new();

    private void WriteVoicePack(bool withLicense = true)
    {
        var samples = Echokraut.Helper.Functional.TtsPaths.EchokrauTtsSamples(_root);
        Directory.CreateDirectory(samples);
        File.WriteAllText(Path.Combine(samples, "Male_All_NPC001.wav"), "");
        File.WriteAllText(Path.Combine(samples, "Male_All_NPC001.txt"), "Hallo.");
        File.WriteAllText(Path.Combine(samples, "ATTRIBUTION.csv"), "");
        if (withLicense) File.WriteAllText(Path.Combine(samples, "LICENSE.txt"), "");
    }

    private void Restart()
    {
        if (_running || _starting) Start(_config.EchokrauTts.TtsBackendArg);
    }

    private void Start(string id)
    {
        var d = _config.EchokrauTts;
        string V(string key, string value) => _logOverride.GetValueOrDefault(key, value);
        File.AppendAllText(LogPath,
            $"[2026-11-25 10:00:00.000] Mode: echokrautts | installRoot={_root} | url=(none) | port=7851 | language=de | parentPid=1 " +
            $"| ttsBackend={id} | xttsFp16={d.XttsFp16Arg} | qwenTemperature={V("qwenTemperature", d.QwenTemperatureArg)} " +
            $"| pocketDecodeSteps={V("pocketDecodeSteps", d.PocketDecodeStepsArg)} | pocketLayers={d.PocketLayersArg} " +
            "| responseCacheMinutes= | preserveUserData=False | isWindows=True\n" +
            "[2026-11-25 10:00:01.000] {\"event\":\"progress\",\"step\":\"deps\",\"message\":\"Abhängigkeiten installiert\",\"key\":\"step.deps_installed\"}\n");
        _running = !_failing.Contains(id) && !_hanging.Contains(id);
        _starting = _hanging.Contains(id);
        _status = _failing.Contains(id) ? "Failed: boom" : "";
    }

    private EngineStage Stage(bool withChecks = true,
        Func<E2eContext, string, CancellationToken, Task<SelfTestResult>>? gameVoice = null) =>
        new(_engine.Object, _config, new E2eEngineWait(_engine.Object, _backend.Object, _npcData.Object, TimeSpan.FromMilliseconds(1)),
            withChecks ? new FakeCheck("generation", _ => Task.FromResult(
                _generation.GetValueOrDefault(_config.EchokrauTts.TtsBackendArg, SelfTestResult.Pass("ok")))) : null,
            withChecks ? FakeCheck.Returning("pipeline", SelfTestResult.Pass("played")) : null,
            () => _free, readyWait: TimeSpan.FromMilliseconds(200), voicesWait: TimeSpan.FromMilliseconds(30), gameVoice: gameVoice);

    [Fact]
    public async Task GameVoicePart_RunsWhileEachPassedEngineIsActive_AndOnlyWithTheGameVoiceStageInTheRun()
    {
        _failing.Add("f5");
        Request("xtts", "f5");
        _state.Request = _state.Request with { Stages = ["engine", GameVoiceStage.StageId] };
        var seen = new List<string>();
        var stage = Stage(gameVoice: (_, id, _) =>
        {
            seen.Add($"{id}={_config.EchokrauTts.TtsBackendArg}");
            return Task.FromResult(SelfTestResult.Fail("game audio on disk"));
        });

        var r = await stage.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(new[] { "xtts=xtts" }, seen);
        Assert.Equal(SelfTestOutcome.Fail, _state.ResultOf("game-voice:engine-xtts")!.Outcome);
        Assert.Null(_state.ResultOf("game-voice:engine-f5"));
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("game-voice:engine-xtts", r.Detail);
        // A failed game-voice part is not a passed engine: the loop still switches back to xtts.
        Assert.Equal("xtts", _state.Marks[EngineStage.ActiveEngineMark]);
    }

    [Fact]
    public async Task GameVoicePart_NotInTheRun_IsNotCalled()
    {
        Request("xtts");
        _state.Request = _state.Request with { Stages = ["engine"] };
        var called = false;

        await Stage(gameVoice: (_, _, _) => { called = true; return Task.FromResult(SelfTestResult.Pass("")); })
            .RunAsync(Ctx(), CancellationToken.None);

        Assert.False(called);
        Assert.Null(_state.ResultOf("game-voice:engine-xtts"));
    }

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

    // ── telemetry (plan › B2) ────────────────────────────────────────────────────────────────────

    private sealed class FakeGpu(long used) : IE2eGpuProbe
    {
        public long Used { get; set; } = used;
        public Task<E2eGpuSample?> SampleAsync(CancellationToken ct) => Task.FromResult<E2eGpuSample?>(new(Used, 12000, 40));
    }

    private sealed class FakeFootprint : IE2eInstallFootprint
    {
        public bool? F5 { get; set; } = false;
        public long? SizeBytes(string installRoot) => 1234;
        public IReadOnlyList<string> Venvs(string installRoot) => [".venv"];
        public bool? F5InClassicVenv(string installRoot) => F5;
    }

    private long _frameTime;
    private readonly FakeGpu _gpu = new(3000);
    private readonly FakeFootprint _footprint = new();

    private E2eTelemetryProbes Probes(E2eFrameMonitor frames) =>
        new(frames, _gpu, _ => Task.FromResult<EchokrauTtsHealthResponse?>(new() { device = "cuda", backend = "xtts" }),
            () => 0, _footprint, idleWindow: TimeSpan.FromMilliseconds(5), gpuInterval: TimeSpan.FromMilliseconds(1));

    private EngineStage TelemetryStage(E2eFrameMonitor frames) =>
        new(_engine.Object, _config, new E2eEngineWait(_engine.Object, _backend.Object, _npcData.Object, TimeSpan.FromMilliseconds(1)),
            new FakeCheck("generation", _ => Task.FromResult(SelfTestResult.Pass("ok"))),
            FakeCheck.Returning("pipeline", SelfTestResult.Pass("played")),
            () => _free, readyWait: TimeSpan.FromMilliseconds(200), voicesWait: TimeSpan.FromMilliseconds(30),
            probes: Probes(frames));

    [Fact]
    public async Task Telemetry_StopsForTheBaseline_ThenRecordsEveryEngine()
    {
        _engine.Setup(e => e.StopInstance(It.IsAny<Echokraut.DataClasses.EKEventId>())).Callback(() =>
        {
            _running = false;
            _gpu.Used = 1500;
        });
        _engine.Setup(e => e.StartInstance()).Callback(() =>
        {
            Start(_config.EchokrauTts.TtsBackendArg);
            _gpu.Used = 5000;
        });
        Request("xtts", "f5");
        var r = await TelemetryStage(new E2eFrameMonitor()).RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        var t = _state.Telemetry;
        Assert.Equal(1500, t.VramBaselineMiB);
        Assert.Contains(t.Ops, o => o.Op == "stop");
        Assert.Contains(t.Ops, o => o.Op == "start:xtts");
        Assert.Contains(t.Ops, o => o.Op == "switch:f5");
        Assert.Contains(t.Ops, o => o.Op == "ready:f5");
        Assert.Equal(new[] { "xtts", "f5" }, t.Engines.Select(e => e.Engine));
        var xtts = t.Engines[0];
        Assert.Equal("cuda", xtts.HealthDevice);
        Assert.Equal(3500, xtts.VramOverBaselineMiB);
        Assert.Equal(1234, xtts.InstallRootBytes);
        Assert.Equal(0, xtts.Underruns);
        Assert.False(xtts.F5InClassicVenvBefore);
        Assert.Equal("xtts", _state.Marks[EngineStage.InitialEngineMark]);
    }

    [Fact]
    public async Task Telemetry_F5AlreadyInTheClassicVenv_FailsF5()
    {
        _footprint.F5 = true;
        Request("f5");
        await TelemetryStage(new E2eFrameMonitor()).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, Part("f5").Outcome);
        Assert.Contains("lazy install", Part("f5").Detail);
    }

    [Fact]
    public async Task Telemetry_FreezeDuringTheSwitch_FailsThatEngine()
    {
        // The switch blocks the "framework thread" for 2 s of fake time.
        var frames = new E2eFrameMonitor(() => _frameTime, ticksPerSecond: 1000);
        _engine.Setup(e => e.SwitchTtsBackend("f5")).Callback(() =>
        {
            frames.OnFrame();
            _frameTime += 2000;
            frames.OnFrame();
            _config.EchokrauTts.TtsEngine = "f5";
            Start("f5");
        });
        Request("f5");
        await TelemetryStage(frames).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, Part("f5").Outcome);
        Assert.Contains("switch:f5 froze the game", Part("f5").Detail);
    }

    [Fact]
    public void Judge_IgnoresFreezesOfOtherEngines_AndF5WhenTheRunStartedOnF5()
    {
        _state.Telemetry.Ops.Add(new E2eOpTelemetry("switch:xtts", 10, new E2eFrameStats(2, 1000, 1500, 1500, 1)));
        _state.Marks[EngineStage.InitialEngineMark] = "f5";
        Assert.Empty(EngineStage.Judge(_state, "f5", new E2eEngineTelemetry { Engine = "f5", F5InClassicVenvBefore = true }));
        Assert.Single(EngineStage.Judge(_state, "xtts", new E2eEngineTelemetry { Engine = "xtts" }));
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

    // ── engine options + install files (plan › B3) ───────────────────────────────────────────────

    private void WithEngines(params string[] ids) =>
        _engine.SetupGet(e => e.AvailableEngines).Returns(() => ids.Select(i => new TtsEngineInfo { Id = i }).ToList());

    private E2eStageReport Result(string id) => _state.ResultOf(id)!;

    [Fact]
    public async Task Files_PassWithAGoodPackAndLog()
    {
        Request("xtts", "f5");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Result(EngineStage.VoicePackPart).Outcome);
        var log = Result(EngineStage.InstallerLogPart);
        Assert.Equal(SelfTestOutcome.Pass, log.Outcome);
        Assert.Contains("backends xtts/f5", log.Detail);
        Assert.Contains("language de", log.Detail);
    }

    [Fact]
    public async Task Files_MissingLicense_And_Mojibake_FailTheStage()
    {
        File.Delete(Path.Combine(Echokraut.Helper.Functional.TtsPaths.EchokrauTtsSamples(_root), "LICENSE.txt"));
        File.AppendAllText(LogPath, "[x] Abh\u00C3\u00A4ngigkeiten\n"); // UTF-8 read as Latin-1
        Request("xtts");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("LICENSE.txt missing", Result(EngineStage.VoicePackPart).Detail);
        Assert.Contains("1 mojibake line", Result(EngineStage.InstallerLogPart).Detail);
    }

    [Fact]
    public async Task QwenTemperature_RestartsWith07_ThenPutsTheOriginalBack()
    {
        WithEngines("qwen-1.7b", "qwen-0.6b");
        Request("qwen-1.7b", "qwen-0.6b");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        var part = Result(EngineStage.QwenTemperaturePart);
        Assert.Equal(SelfTestOutcome.Pass, part.Outcome);
        Assert.Contains("temperature 0.7", part.Detail);
        Assert.Contains("qwenTemperature=0.9", part.Detail);
        Assert.Equal(EchokrauTtsData.DefaultQwenTemperature, _config.EchokrauTts.QwenTemperature);
        _engine.Verify(e => e.SetQwenTemperature(EchokrauTtsData.CalmQwenTemperature), Times.Once); // once, not per qwen engine
        Assert.False(_state.Marks.ContainsKey(EngineStage.QwenOriginalMark));
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
    }

    [Fact]
    public async Task QwenTemperature_NotInTheInstallerArgs_Fails()
    {
        _logOverride["qwenTemperature"] = "0.9";
        WithEngines("qwen-0.6b");
        Request("qwen-0.6b");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        var part = Result(EngineStage.QwenTemperaturePart);
        Assert.Equal(SelfTestOutcome.Fail, part.Outcome);
        Assert.Contains("qwenTemperature=0.9, expected 0.7", part.Detail);
        // Put back at once, the next engines must not run on 0.7.
        Assert.Contains("temperature put back to 0.9, restarted", part.Detail);
        Assert.Equal(EchokrauTtsData.DefaultQwenTemperature, _config.EchokrauTts.QwenTemperature);
        Assert.False(_state.Marks.ContainsKey(EngineStage.QwenOriginalMark));
    }

    [Fact]
    public async Task PocketLayers_SixAndBack_KeepsTheStepsOfEachSize()
    {
        WithEngines("pocket");
        Request("pocket");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        var part = Result(EngineStage.PocketLayersPart);
        Assert.Equal(SelfTestOutcome.Pass, part.Outcome);
        Assert.Contains("steps 8", part.Detail);
        Assert.Contains("layers 6", part.Detail);
        Assert.Contains("pocketLayers=6 pocketDecodeSteps=4", part.Detail);
        Assert.Contains("layers 24: restart + ready", part.Detail);
        var d = _config.EchokrauTts;
        Assert.Equal((24, 4, 4), (d.PocketLayersEffective, d.PocketDecodeSteps24, d.PocketDecodeSteps6));
    }

    [Fact]
    public async Task PocketLayers_StepsLost_Fails()
    {
        // Back on 24 the steps of 24 should be 8 again; the fake installer reports 4.
        WithEngines("pocket");
        _engine.Setup(e => e.SetPocketLayers(24)).Callback(() =>
        {
            _config.EchokrauTts.PocketLayers = 24;
            _logOverride["pocketDecodeSteps"] = "4";
            Restart();
        });
        Request("pocket");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        var part = Result(EngineStage.PocketLayersPart);
        Assert.Equal(SelfTestOutcome.Fail, part.Outcome);
        Assert.Contains("layers 24: installer log pocketDecodeSteps=4, expected 8", part.Detail);
        Assert.Equal(4, _config.EchokrauTts.PocketDecodeSteps24);
    }

    [Fact]
    public async Task InterruptedOption_IsPutBackAndSkipped()
    {
        _config.EchokrauTts.QwenTemperature = 0.7;
        _state.Marks[EngineStage.QwenOriginalMark] = "0.9";
        _config.EchokrauTts.PocketLayers = 6;
        _state.Marks[EngineStage.PocketOriginalMark] = "24|1|8";
        Request("xtts");
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(0.9, _config.EchokrauTts.QwenTemperature);
        Assert.Equal((24, 1, 8), (_config.EchokrauTts.PocketLayers, _config.EchokrauTts.PocketDecodeSteps24, _config.EchokrauTts.PocketDecodeSteps6));
        Assert.Equal(SelfTestOutcome.Skipped, Result(EngineStage.QwenTemperaturePart).Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, Result(EngineStage.PocketLayersPart).Outcome);
        Assert.Empty(_state.Marks.Keys.Where(k => k.StartsWith("options-")));
    }
}
