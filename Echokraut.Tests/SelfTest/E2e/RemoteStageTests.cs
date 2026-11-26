using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest.E2e;

public class RemoteStageTests
{
    private const string Remote = "https://tts.example.org";

    private readonly Configuration _config = new() { BackendSelection = TTSBackends.EchokrauTTS };
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<IEchokrauTtsInstanceService> _engine = new();
    private readonly Mock<IGameVoiceReferenceService> _gameVoice = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly E2eState _state = E2eStateStoreTests.State();
    private readonly List<string> _calls = new();
    private string? _portBusy;
    private bool _trackedAfterStop;
    private int _voices = 3;
    private SelfTestResult _backendCheck = SelfTestResult.Pass("up");
    private SelfTestResult _gameVoiceCheck = SelfTestResult.Pass("decodes");
    private bool? _gameVoiceOnDuringGeneration;

    public RemoteStageTests()
    {
        _state.ConfigIsE2e = true;
        _state.Request = _state.Request with { RemoteUrl = Remote };
        _config.EchokrauTts.InstanceType = AlltalkInstanceType.Local;
        _engine.SetupGet(e => e.InstanceRunning).Returns(() => _trackedAfterStop);
        _backend.Setup(b => b.SwitchInstanceTypeAsync(It.IsAny<AlltalkInstanceType>(), It.IsAny<string?>(), It.IsAny<EKEventId>()))
            .Returns((AlltalkInstanceType t, string? url, EKEventId _) =>
            {
                _calls.Add($"switch {t} {url}");
                _config.EchokrauTts.InstanceType = t;
                if (url != null) _config.EchokrauTts.BaseUrl = url;
                return Task.CompletedTask;
            });
        _backend.Setup(b => b.RefreshBackend()).Callback(() => _calls.Add("refresh"));
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(() =>
            Enumerable.Range(0, _voices).Select(_ => new EchokrautVoice { IsEnabled = true }).ToList());
    }

    private RemoteStage Stage() => new(_backend.Object, _engine.Object, _gameVoice.Object, _config,
        (_, _) =>
        {
            _calls.Add("stop");
            return Task.FromResult(_portBusy);
        },
        new E2eEngineWait(_engine.Object, _backend.Object, _npcData.Object, TimeSpan.FromMilliseconds(1)),
        [
            new FakeCheck("backend", _ => { _calls.Add("backend"); return Task.FromResult(_backendCheck); }),
            new FakeCheck("generation", _ => { _calls.Add("generation"); return Task.FromResult(SelfTestResult.Pass("bytes")); }),
            new FakeCheck("pipeline", _ => { _calls.Add("pipeline"); return Task.FromResult(SelfTestResult.Pass("played")); }),
        ],
        new FakeCheck("game-voice", _ =>
        {
            _calls.Add($"game-voice {_config.GameVoiceReferenceEnabled}");
            return Task.FromResult(_gameVoiceCheck);
        }),
        (_, _) =>
        {
            _gameVoiceOnDuringGeneration = _config.GameVoiceReferenceEnabled && _config.GameVoiceConsentVersion == GameVoiceConsent.CurrentVersion;
            return Task.FromResult(SelfTestResult.Pass("game voice"));
        },
        voicesWait: TimeSpan.FromMilliseconds(30));

    private E2eContext Ctx() => new(_state, () => { }, _ => { });

    private SelfTestOutcome? Part(string id) => _state.ResultOf(id)?.Outcome;

    [Fact]
    public void Needs_TheReset() => Assert.Equal(new[] { "reset" }, Stage().Needs);

    [Fact]
    public void ChecksRemote_NeedsOnlyTheSwitchPart_NotTheWholeStage()
    {
        var checksRemote = new ChecksStage("checks:remote", [], [RemoteStage.SwitchPart]);
        _state.StageResults.Add(new E2eStageReport(RemoteStage.SwitchPart, SelfTestOutcome.Pass, "", 1));
        _state.StageResults.Add(new E2eStageReport(RemoteStage.GameVoicePart, SelfTestOutcome.Fail, "", 1));
        _state.StageResults.Add(new E2eStageReport(RemoteStage.StageId, SelfTestOutcome.Fail, "", 1));
        Assert.Null(E2eRunService.MissingNeed(_state, checksRemote));

        _state.StageResults[0] = _state.StageResults[0] with { Outcome = SelfTestOutcome.Fail };
        Assert.Equal("needs remote:switch (fail)", E2eRunService.MissingNeed(_state, checksRemote));
    }

    [Fact]
    public async Task RealConfig_IsSkipped_WithoutTouchingAnything()
    {
        _state.ConfigIsE2e = false;
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task NoRemoteUrl_IsSkipped()
    {
        _state.Request = _state.Request with { RemoteUrl = null };
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task AllParts_Pass_InTheFixedOrder_StopBeforeTheSwitch()
    {
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(new[] { "stop", $"switch Remote {Remote}", "refresh", "backend", "generation", "pipeline", "game-voice True" },
            _calls.Distinct());
        Assert.Equal(AlltalkInstanceType.Remote, _config.EchokrauTts.InstanceType);
        foreach (var id in new[] { RemoteStage.SwitchPart, "remote:backend", "remote:generation", "remote:pipeline",
                     RemoteStage.GameVoicePart, RemoteStage.GameVoiceGenerationPart })
            Assert.Equal(SelfTestOutcome.Pass, Part(id));
        Assert.True(_gameVoiceOnDuringGeneration);
    }

    [Fact]
    public async Task GameVoiceOptIn_IsSwitchedOffAgain_AndMemoryCleared()
    {
        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.False(_config.GameVoiceReferenceEnabled);
        Assert.Equal(0, _config.GameVoiceConsentVersion);
        Assert.Null(_config.GameVoiceConsentUtc);
        Assert.Empty(_state.TouchedOptions);
        _gameVoice.Verify(g => g.ClearMemory(), Times.Once);
    }

    [Fact]
    public async Task PortStillBusy_FailsTheSwitch_AndSkipsEverythingElse()
    {
        _portBusy = "Port 8765 still answers";
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(SelfTestOutcome.Fail, Part(RemoteStage.SwitchPart));
        Assert.Equal(new[] { "stop" }, _calls);
        Assert.Equal(AlltalkInstanceType.Local, _config.EchokrauTts.InstanceType);
        Assert.Equal(SelfTestOutcome.Skipped, Part("remote:backend"));
        Assert.StartsWith("needs remote:switch", _state.ResultOf(RemoteStage.GameVoiceGenerationPart)!.Detail);
    }

    [Fact]
    public async Task LocalStillTracked_AfterTheStop_IsNotSwitched()
    {
        _trackedAfterStop = true;
        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, Part(RemoteStage.SwitchPart));
        _backend.Verify(b => b.SwitchInstanceTypeAsync(It.IsAny<AlltalkInstanceType>(), It.IsAny<string?>(), It.IsAny<EKEventId>()), Times.Never);
    }

    [Fact]
    public async Task NoVoiceOnTheRemote_FailsTheSwitch()
    {
        _voices = 0;
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, Part(RemoteStage.SwitchPart));
    }

    [Fact]
    public async Task BackendDown_SkipsTheGameVoice()
    {
        _backendCheck = SelfTestResult.Fail("does not answer");
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, Part(RemoteStage.GameVoicePart));
        Assert.Equal(SelfTestOutcome.Skipped, Part(RemoteStage.GameVoiceGenerationPart));
        Assert.Null(_gameVoiceOnDuringGeneration);
    }

    [Fact]
    public async Task GameVoiceCheckFails_SkipsTheGeneration()
    {
        _gameVoiceCheck = SelfTestResult.Fail("ServerUnsupported");
        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, Part(RemoteStage.GameVoicePart));
        Assert.Equal("needs remote:game-voice (fail)", _state.ResultOf(RemoteStage.GameVoiceGenerationPart)!.Detail);
        Assert.False(_config.GameVoiceReferenceEnabled);
    }

    [Fact]
    public async Task Restarted_KeepsReportedParts_AndRestoresLeftoverOptions()
    {
        _state.StageResults.Add(new E2eStageReport(RemoteStage.SwitchPart, SelfTestOutcome.Pass, "", 1));
        _state.TouchedOptions[nameof(Configuration.GameVoiceReferenceEnabled)] = "false";
        _config.GameVoiceReferenceEnabled = true;

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.DoesNotContain("stop", _calls);
        Assert.Contains("game-voice True", _calls);
        Assert.False(_config.GameVoiceReferenceEnabled);
    }

    [Fact]
    public async Task SwitchDetail_NeverCarriesTheRemoteUrl()
    {
        await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.DoesNotContain("example.org", _state.ResultOf(RemoteStage.SwitchPart)!.Detail);
    }
}
