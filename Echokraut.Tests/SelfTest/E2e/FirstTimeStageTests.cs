using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Moq;
using Xunit;
using static Echokraut.Services.SelfTest.E2e.E2eEngineWait;
using static Echokraut.Services.SelfTest.E2e.Stages.FirstTimeStage;

namespace Echokraut.Tests.SelfTest.E2e;

public class FirstTimeStageTests
{
    private const string Root = @"E:\EkE2E";
    private const string Version = "v9.9.9.9";

    private readonly Configuration _config = new();
    private readonly Mock<IAlltalkInstanceService> _alltalk = new();
    private readonly Mock<IEchokrauTtsInstanceService> _engine = new();
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly Mock<ICommandService> _commands = new();
    private readonly Mock<IBatchModeService> _batch = new();
    private readonly Mock<IE2eInstallHost> _host = new();
    private readonly FirstTimeWizardController _wizard;
    private readonly List<string> _progress = new();

    private bool _running;
    private bool _installing;
    private string _status = "";

    public FirstTimeStageTests()
    {
        _wizard = new FirstTimeWizardController(_config, _alltalk.Object, _engine.Object, _backend.Object, _batch.Object);
        // What NativeWindowManager does on Completed.
        _wizard.Completed += () =>
        {
            _config.LastSeenChangelogVersion = Version;
            _wizard.ReportWindowOpen(false);
        };
        _wizard.ReportWindowOpen(true);

        _engine.SetupGet(e => e.InstanceRunning).Returns(() => _running);
        _engine.SetupGet(e => e.Installing).Returns(() => _installing);
        _engine.SetupGet(e => e.CurrentInstallStatus).Returns(() => _status);
        _engine.Setup(e => e.Install()).Callback(() =>
        {
            _config.EchokrauTts.LocalInstall = true;
            _config.FirstTime = false; // MarkInstalled does this too
            _running = true;
            _status = "Done";
        });
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(true);
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns([new EchokrautVoice { IsEnabled = true }]);
        _host.Setup(h => h.PrepareRoot(Root)).Returns((string?)null);
        _host.Setup(h => h.BackedUpShareLines(It.IsAny<string>())).Returns(false);
        _host.Setup(h => h.ListenerExecutable(8765)).Returns(@"E:\EkE2E\echokrautts\.venv\Scripts\python.exe");
    }

    private FirstTimeStage Stage(SelfTestCheck? backendCheck = null) =>
        new(_wizard, _config, _engine.Object, _backend.Object, _npcData.Object, _commands.Object, backendCheck,
            _host.Object, a => { a(); return Task.CompletedTask; }, Version,
            pollInterval: TimeSpan.FromMilliseconds(1), stepPause: TimeSpan.Zero,
            installWait: TimeSpan.FromSeconds(5), windowWait: TimeSpan.FromMilliseconds(30),
            voicesWait: TimeSpan.FromMilliseconds(50));

    private E2eContext Ctx(string? backupDir = @"C:\x\backup-1", bool testWrapper = false, string? wrapperVersion = null)
    {
        var state = E2eStateStoreTests.State();
        state.Request = state.Request with { TestWrapper = testWrapper, WrapperVersion = wrapperVersion };
        state.BackupDir = backupDir;
        return new E2eContext(state, () => { }, _progress.Add);
    }

    [Fact]
    public async Task HappyPath_WalksTheWizardAndPasses()
    {
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(TTSBackends.EchokrauTTS, _config.BackendSelection);
        Assert.Equal(AlltalkInstanceType.Local, _config.EchokrauTts.InstanceType);
        Assert.Equal(Root, _config.TtsInstallRoot);
        Assert.True(IsUnder(_config.LocalSaveLocation, Root));
        Assert.False(_config.ShareLinesWithCommunityDb); // from the backed-up JSON
        Assert.False(_config.FirstTime);
        Assert.Equal(2, _wizard.Step);
        _host.Verify(h => h.PrepareRoot(Root), Times.Once);
        _backend.Verify(b => b.RefreshBackend(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task TestWrapper_InstallsTheLocalZip()
    {
        _engine.SetupGet(e => e.TestInstallAvailable).Returns(true);
        _engine.Setup(e => e.InstallTestWrapper()).Callback(() =>
        {
            _config.EchokrauTts.LocalInstall = true;
            _config.FirstTime = false;
            _running = true;
            _status = "Done";
        });

        var r = await Stage().RunAsync(Ctx(testWrapper: true), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        _engine.Verify(e => e.InstallTestWrapper(), Times.Once);
        _engine.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public async Task TestWrapper_ZipMissing_FailsBeforeTouchingTheRoot()
    {
        // The release install would pass and the run would report a wrapper it never tested.
        _engine.SetupGet(e => e.TestInstallAvailable).Returns(false);

        var r = await Stage().RunAsync(Ctx(testWrapper: true), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("test zip is missing", r.Detail);
        _host.Verify(h => h.PrepareRoot(It.IsAny<string>()), Times.Never);
        _engine.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public async Task WrapperVersion_SelectedBeforeTheInstall_AndRecorded()
    {
        var order = new List<string>();
        string? selected = null;
        _engine.Setup(e => e.SelectWrapperReleaseAsync("0.0.1.9"))
            .Callback(() => { order.Add("select"); selected = "0.0.1.9"; })
            .ReturnsAsync((string?)null);
        _engine.Setup(e => e.Install()).Callback(() =>
        {
            order.Add("install");
            _config.EchokrauTts.LocalInstall = true;
            _config.EchokrauTts.InstalledWrapperVersion = selected ?? "0.0.2.0";
            _config.FirstTime = false;
            _running = true;
            _status = "Done";
        });

        var r = await Stage().RunAsync(Ctx(wrapperVersion: "0.0.1.9"), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(["select", "install"], order);
        Assert.Contains("0.0.1.9", r.Detail);
    }

    [Fact]
    public async Task WrapperVersion_UnknownRelease_FailsBeforeTouchingTheRoot()
    {
        _engine.Setup(e => e.SelectWrapperReleaseAsync("9.9.9.9")).ReturnsAsync("release 9.9.9.9: GitHub returned 404");

        var r = await Stage().RunAsync(Ctx(wrapperVersion: "9.9.9.9"), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal("wrapperVersion 9.9.9.9: release 9.9.9.9: GitHub returned 404.", r.Detail);
        _host.Verify(h => h.PrepareRoot(It.IsAny<string>()), Times.Never);
        _engine.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public async Task WrapperVersion_InstallRecordsAnotherTag_Fails()
    {
        // The selection was lost (e.g. the zip URL came from the baseline): the run must not report the old wrapper.
        _engine.Setup(e => e.SelectWrapperReleaseAsync("0.0.1.9")).ReturnsAsync((string?)null);
        _config.EchokrauTts.InstalledWrapperVersion = "0.0.2.0";

        var r = await Stage().RunAsync(Ctx(wrapperVersion: "0.0.1.9"), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("installed wrapper 0.0.2.0, requested 0.0.1.9", r.Detail);
    }

    [Fact]
    public async Task RootPreparedBeforeInstall()
    {
        var order = new List<string>();
        _host.Setup(h => h.PrepareRoot(Root)).Callback(() => order.Add("prepare")).Returns((string?)null);
        _engine.Setup(e => e.Install()).Callback(() =>
        {
            order.Add("install");
            _config.EchokrauTts.LocalInstall = true;
            _running = true;
        });

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(new[] { "prepare", "install" }, order);
    }

    [Fact]
    public async Task WindowNeverOpens_TogglesOnceThenFails()
    {
        _wizard.ReportWindowOpen(false);

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("did not open", r.Detail);
        _commands.Verify(c => c.ToggleFirstTimeUi(), Times.Once);
        _engine.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public async Task WindowOpensAfterToggle_Continues()
    {
        _wizard.ReportWindowOpen(false);
        _commands.Setup(c => c.ToggleFirstTimeUi()).Callback(() => _wizard.ReportWindowOpen(true));

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
    }

    [Fact]
    public async Task PrepareRootRefuses_FailsWithoutInstall()
    {
        _host.Setup(h => h.PrepareRoot(Root)).Returns("exists without marker");

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("marker", r.Detail);
        _engine.Verify(e => e.Install(), Times.Never);
    }

    [Fact]
    public async Task NoBackup_Fails()
    {
        var r = await Stage().RunAsync(Ctx(backupDir: null), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("ShareLinesWithCommunityDb", r.Detail);
    }

    [Fact]
    public async Task InstallFails_FailsFast()
    {
        _engine.Setup(e => e.Install()).Callback(() => _status = "Failed: install did not complete");

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("Failed: install did not complete", r.Detail);
    }

    [Fact]
    public async Task ListenerOutsideTheRoot_Fails()
    {
        _host.Setup(h => h.ListenerExecutable(8765)).Returns(@"C:\LocalTTS\echokrautts\python.exe");

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains(@"C:\LocalTTS", r.Detail);
    }

    [Fact]
    public async Task ListenerUnknown_Fails()
    {
        _host.Setup(h => h.ListenerExecutable(8765)).Returns((string?)null);

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("which process", r.Detail);
    }

    [Fact]
    public async Task NoVoices_Fails()
    {
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns([new EchokrautVoice { IsEnabled = false }]);

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("No enabled voice", r.Detail);
        Assert.Equal(1, _wizard.Step); // never advanced
    }

    [Fact]
    public async Task BackendCheckFails_Fails()
    {
        var check = new Mock<SelfTestCheck>();
        check.SetupGet(c => c.Id).Returns("backend");
        check.SetupGet(c => c.Timeout).Returns(TimeSpan.FromSeconds(5));
        check.Setup(c => c.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(SelfTestResult.Fail("does not answer"));

        var r = await Stage(check.Object).RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("does not answer", r.Detail);
    }

    [Fact]
    public async Task ChangelogNotMarkedSeen_Fails()
    {
        var stage = Stage();
        _wizard.Completed += () => _config.LastSeenChangelogVersion = "v0.0.0.1"; // runs after the ctor's handler

        var r = await stage.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("LastSeenChangelogVersion", r.Detail);
    }

    // ── Judge ────────────────────────────────────────────────────────────────────────────────────

    private static InstallSnapshot Snap(bool installing = false, bool starting = false, bool running = false,
        string status = "", bool reachable = false) => new(installing, starting, running, status, reachable);

    [Fact]
    public void Judge_RunningAndReachable_Ready() =>
        Assert.Equal(InstallVerdict.Ready, Judge(Snap(running: true, reachable: true), true, 0, false));

    [Fact]
    public void Judge_RunningNotYetReachable_Waiting() =>
        Assert.Equal(InstallVerdict.Waiting, Judge(Snap(running: true), true, 0, false));

    [Fact]
    public void Judge_FailedStatus_Failed() =>
        Assert.Equal(InstallVerdict.Failed, Judge(Snap(installing: true, status: "Failed: boom"), true, 0, false));

    [Fact]
    public void Judge_UrlNotConfigured_Failed() =>
        Assert.Equal(InstallVerdict.Failed, Judge(Snap(status: "EchokrauTTS download URL not configured"), false, 1, false));

    [Fact]
    public void Judge_OneInactivePollAfterActive_StillWaiting() =>
        Assert.Equal(InstallVerdict.Waiting, Judge(Snap(), true, 1, false));

    [Fact]
    public void Judge_TwoInactivePollsAfterActive_Failed() =>
        Assert.Equal(InstallVerdict.Failed, Judge(Snap(), true, 2, false));

    [Fact]
    public void Judge_NeverActive_FailsOnlyAfterGrace()
    {
        Assert.Equal(InstallVerdict.Waiting, Judge(Snap(), false, 5, false));
        Assert.Equal(InstallVerdict.Failed, Judge(Snap(), false, 5, true));
    }

    [Theory]
    [InlineData(@"E:\EkE2E", true)]
    [InlineData(@"e:\eke2e\LocalSaves", true)]
    [InlineData(@"E:\EkE2E/LocalSaves", true)]
    [InlineData(@"E:\EkE2E2\x", false)]
    [InlineData(@"C:\LocalTTS", false)]
    public void IsUnder_Cases(string path, bool expected) => Assert.Equal(expected, IsUnder(path, Root));
}

public class E2eInstallHostTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eke2e-host-" + Guid.NewGuid().ToString("N"));
    private readonly E2eInstallHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Marker => Path.Combine(_dir, E2eRootGuard.MarkerFileName);

    [Fact]
    public void LauncherOrSelf_VenvLauncherParent_GivesLauncherPath()
    {
        // Measured 2026-11-21: the listener is uv's base python, its parent the venv launcher.
        Assert.Equal(@"E:\EkE2E\echokrautts\.venv\Scripts\python.exe",
            E2eInstallHost.LauncherOrSelf(
                @"C:\Users\x\AppData\Roaming\uv\python\cpython-3.11-windows-x86_64-none\python.exe",
                @"E:\EkE2E\echokrautts\.venv\Scripts\python.exe"));
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(null)]
    public void LauncherOrSelf_OtherOrNoParent_GivesListenerPath(string? parent)
    {
        Assert.Equal(@"C:\uv\python.exe", E2eInstallHost.LauncherOrSelf(@"C:\uv\python.exe", parent));
    }

    [Fact]
    public void PrepareRoot_NewFolder_CreatedWithMarker()
    {
        Assert.Null(_host.PrepareRoot(_dir));
        Assert.True(File.Exists(Marker));
        Assert.Single(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void PrepareRoot_ExistingWithoutMarker_Untouched()
    {
        Directory.CreateDirectory(_dir);
        var keep = Path.Combine(_dir, "keep.txt");
        File.WriteAllText(keep, "x");

        Assert.Contains("without", _host.PrepareRoot(_dir));
        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void PrepareRoot_ExistingWithMarker_EmptiedMarkerRewritten()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "echokrautts", "models"));
        File.WriteAllText(Path.Combine(_dir, "echokrautts", "models", "m.bin"), "x");
        File.WriteAllText(Path.Combine(_dir, "old.txt"), "x");
        File.WriteAllText(Marker, "old");

        Assert.Null(_host.PrepareRoot(_dir));

        Assert.Equal(new[] { Marker }, Directory.GetFileSystemEntries(_dir));
    }

    private string WriteConfig(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, E2eInstallHost.ConfigFileName), json);
        return _dir;
    }

    [Fact]
    public void BackedUpShareLines_False() =>
        Assert.False(_host.BackedUpShareLines(WriteConfig("{\"ShareLinesWithCommunityDb\": false}")));

    [Fact]
    public void BackedUpShareLines_True() =>
        Assert.True(_host.BackedUpShareLines(WriteConfig("{\"ShareLinesWithCommunityDb\": true}")));

    [Fact]
    public void BackedUpShareLines_Absent_DefaultTrue() =>
        Assert.True(_host.BackedUpShareLines(WriteConfig("{\"Enabled\": true}")));

    [Fact]
    public void BackedUpShareLines_NoFile_Null() => Assert.Null(_host.BackedUpShareLines(_dir));

    [Fact]
    public void BackedUpShareLines_Broken_Null() => Assert.Null(_host.BackedUpShareLines(WriteConfig("{ nope")));
}
