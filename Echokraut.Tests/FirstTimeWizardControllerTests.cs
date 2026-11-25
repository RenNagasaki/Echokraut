using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class FirstTimeWizardControllerTests
{
    private readonly Configuration _config = new();
    private readonly Mock<IAlltalkInstanceService> _alltalk = new();
    private readonly Mock<IEchokrauTtsInstanceService> _ek = new();
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<IBatchModeService> _batch = new();

    private FirstTimeWizardController Create() =>
        new(_config, _alltalk.Object, _ek.Object, _backend.Object, _batch.Object);

    [Fact]
    public void StartsOnStepZero()
    {
        var w = Create();
        Assert.Equal(0, w.Step);
        Assert.False(w.IsWindowOpen);
    }

    [Fact]
    public void ChooseEngine_SetsBackendSelection()
    {
        var w = Create();
        w.ChooseEngine(TTSBackends.Alltalk);
        Assert.Equal(TTSBackends.Alltalk, _config.BackendSelection);
        w.ChooseEngine(TTSBackends.EchokrauTTS);
        Assert.Equal(TTSBackends.EchokrauTTS, _config.BackendSelection);
    }

    [Fact]
    public void ChooseMode_WritesTheActiveEnginesTypeAndMovesToStepOne()
    {
        var w = Create();
        w.ChooseEngine(TTSBackends.EchokrauTTS);
        var alltalkBefore = _config.Alltalk.InstanceType;

        w.ChooseMode(AlltalkInstanceType.Remote);

        Assert.Equal(AlltalkInstanceType.Remote, _config.EchokrauTts.InstanceType);
        Assert.Equal(alltalkBefore, _config.Alltalk.InstanceType);
        Assert.Equal(1, w.Step);
    }

    [Fact]
    public void ChooseMode_AllTalk_WritesAllTalksType()
    {
        var w = Create();
        w.ChooseEngine(TTSBackends.Alltalk);
        w.ChooseMode(AlltalkInstanceType.None);
        Assert.Equal(AlltalkInstanceType.None, _config.Alltalk.InstanceType);
    }

    [Fact]
    public void Setters_WriteTheConfig()
    {
        var w = Create();
        w.SetInstallRoot(@"E:\EkE2E");
        w.SetLocalSaveLocation(@"E:\EkE2E\LocalSaves");
        w.SetShareLines(false);

        Assert.Equal(@"E:\EkE2E", _config.TtsInstallRoot);
        Assert.Equal(@"E:\EkE2E\LocalSaves", _config.LocalSaveLocation);
        Assert.False(_config.ShareLinesWithCommunityDb);
    }

    [Fact]
    public void Next_LocalNotInstalled_Refused()
    {
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.Local);
        _config.EchokrauTts.LocalInstall = false;

        Assert.False(w.CanAdvance);
        Assert.False(w.Next());
        Assert.Equal(1, w.Step);
    }

    [Fact]
    public void Next_LocalInstalled_MovesToStepTwo()
    {
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.Local);
        _config.EchokrauTts.LocalInstall = true;

        Assert.True(w.Next());
        Assert.Equal(2, w.Step);
    }

    [Fact]
    public void Next_None_AlwaysAllowed()
    {
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.None);
        Assert.True(w.Next());
    }

    [Fact]
    public void Next_DuringBatchOp_Refused()
    {
        _batch.SetupGet(b => b.IsActive).Returns(true);
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.None);
        Assert.False(w.Next());
    }

    [Fact]
    public void Next_OnStepZero_Refused()
    {
        var w = Create();
        _config.EchokrauTts.InstanceType = AlltalkInstanceType.None;
        Assert.False(w.Next());
        Assert.Equal(0, w.Step);
    }

    [Fact]
    public void Back_GoesOneStepDownAndStopsAtZero()
    {
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.None);
        w.Next();
        w.Back();
        Assert.Equal(1, w.Step);
        w.Back();
        w.Back();
        Assert.Equal(0, w.Step);
    }

    [Fact]
    public async Task TestConnection_Ready_SnapshotsTheUrlAndAllowsNext()
    {
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ReturnsAsync("Ready");
        _config.EchokrauTts.BaseUrl = "http://remote:8765";
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.Remote);

        var r = await w.TestConnectionAsync();

        Assert.True(r.Success);
        Assert.True(w.RemoteTestSucceeded);
        Assert.Equal("http://remote:8765", w.RemoteTestUrlSnapshot);
        Assert.False(w.RemoteTestRunning);
        Assert.True(w.CanAdvance);
    }

    [Fact]
    public async Task TestConnection_UrlEditedAfterTheTest_NextRefused()
    {
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ReturnsAsync("Ready");
        _config.EchokrauTts.BaseUrl = "http://remote:8765";
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.Remote);
        await w.TestConnectionAsync();

        _config.EchokrauTts.BaseUrl = "http://other:8765";

        Assert.False(w.CanAdvance);
        Assert.False(w.Next());
    }

    [Fact]
    public async Task TestConnection_ErrorText_Fails()
    {
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ReturnsAsync("Connection refused");
        var w = Create();
        w.ChooseMode(AlltalkInstanceType.Remote);

        var r = await w.TestConnectionAsync();

        Assert.False(r.Success);
        Assert.Equal("Connection refused", r.Message);
        Assert.Equal(string.Empty, w.RemoteTestUrlSnapshot);
        Assert.False(w.CanAdvance);
    }

    [Fact]
    public async Task TestConnection_Throws_FailsWithTheMessage()
    {
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ThrowsAsync(new InvalidOperationException("boom"));
        var w = Create();

        var r = await w.TestConnectionAsync();

        Assert.False(r.Success);
        Assert.Equal("boom", r.Message);
        Assert.False(w.RemoteTestRunning);
    }

    [Fact]
    public async Task StartInstall_DoesNotStopOnTheCallingThread()
    {
        using var gate = new ManualResetEventSlim(false);
        var callerThread = Environment.CurrentManagedThreadId;
        int? stopThread = null;
        _ek.SetupGet(e => e.InstanceRunning).Returns(true);
        _ek.Setup(e => e.StopInstance(It.IsAny<EKEventId>())).Callback(() =>
        {
            stopThread = Environment.CurrentManagedThreadId;
            gate.Wait(TimeSpan.FromSeconds(5));
        });
        var w = Create();

        var task = w.StartInstall(); // returns although StopInstance blocks
        Assert.False(task.IsCompleted);
        gate.Set();
        await task;

        Assert.NotEqual(callerThread, stopThread);
        _ek.Verify(e => e.Install(), Times.Once);
    }

    [Fact]
    public async Task StartInstall_NotRunning_InstallsWithoutStop()
    {
        var w = Create();
        await w.StartInstall();
        _ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Never);
        _ek.Verify(e => e.Install(), Times.Once);
    }

    [Fact]
    public async Task StartInstall_TestWrapper_InstallsTheLocalZipInsteadOfTheRelease()
    {
        var w = Create();
        await w.StartInstall(testWrapper: true);
        _ek.Verify(e => e.InstallTestWrapper(), Times.Once);
        _ek.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public async Task StartInstall_AllTalk_InstallsAllTalk()
    {
        var w = Create();
        w.ChooseEngine(TTSBackends.Alltalk);
        await w.StartInstall();
        _alltalk.Verify(a => a.Install(), Times.Once);
        _ek.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public void Finish_OnStepTwo_ClearsFirstTimeAndRaisesCompleted()
    {
        var w = Create();
        var completed = 0;
        w.Completed += () => completed++;
        w.ChooseMode(AlltalkInstanceType.None);
        w.Next();

        Assert.True(w.Finish());
        Assert.False(_config.FirstTime);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Finish_BeforeStepTwo_Refused()
    {
        var w = Create();
        var completed = 0;
        w.Completed += () => completed++;

        Assert.False(w.Finish());
        Assert.True(_config.FirstTime);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void ReportWindowOpen_IsReflected()
    {
        var w = Create();
        w.ReportWindowOpen(true);
        Assert.True(w.IsWindowOpen);
        w.ReportWindowOpen(false);
        Assert.False(w.IsWindowOpen);
    }
}
