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
using Echotools.Logging.DataClasses;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>PreconditionsStage, ChecksStage, BackupStage, ResetStage, RestoreStage, ChatHookSpikeStage.</summary>
public class E2eStagesTests
{
    private static readonly Func<string, (bool, string)> AnyPathValid = _ => (true, "");
    private readonly List<string> _progress = new();
    private int _saves;

    private E2eContext Ctx(E2eState? state = null) =>
        new(state ?? E2eStateStoreTests.State(), () => _saves++, _progress.Add);

    private static Mock<IE2eGameProbe> Probe(E2eGameSnapshot snapshot)
    {
        var probe = new Mock<IE2eGameProbe>();
        probe.Setup(p => p.SnapshotAsync()).ReturnsAsync(snapshot);
        return probe;
    }

    private static readonly E2eGameSnapshot Ready = new(true, "Tataru Taru", false, false, false, false);

    private static PreconditionsStage Pre(E2eGameSnapshot snapshot, bool isDev = true, Configuration? config = null) =>
        new(Probe(snapshot).Object, config ?? new Configuration(), isDev, new E2eRootGuardTests.FakeFs(), AnyPathValid);

    [Fact]
    public async Task Preconditions_AllFine_Pass()
    {
        var r = await Pre(Ready).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
    }

    [Fact]
    public async Task Preconditions_ReportsEveryProblem()
    {
        var r = await Pre(new E2eGameSnapshot(false, "", true, true, true, true), isDev: false)
            .RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        foreach (var expected in new[] { "dev load", "not logged in", "cutscene", "duty", "Talk window", "BattleTalk" })
            Assert.Contains(expected, r.Detail);
    }

    [Fact]
    public async Task Preconditions_ConfigAlreadyOnTheE2eRoot_Fail()
    {
        var config = new Configuration { TtsInstallRoot = @"E:\EkE2E" };
        var r = await Pre(Ready, config: config).RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("overlaps", r.Detail);
    }

    [Fact]
    public async Task Preconditions_PreviousRunLeftE2eConfig_Fail()
    {
        var state = E2eStateStoreTests.State();
        state.Marks[PreconditionsStage.PreviousConfigIsE2eMark] = "true";

        var r = await Pre(Ready).RunAsync(Ctx(state), CancellationToken.None);

        Assert.Contains("restore.ps1", r.Detail);
    }

    [Fact]
    public void Preconditions_EndTheRunUnlessPass() => Assert.True(Pre(Ready).AbortsRunUnlessPass);

    [Fact]
    public async Task Checks_RunsOnlyAutomaticOnes_ReportsEachAsProgress()
    {
        var interactive = FakeCheck.Returning("talk", SelfTestResult.Pass(""), interactive: true);
        var stage = new ChecksStage("checks", [
            FakeCheck.Returning("player", SelfTestResult.Pass("ok")),
            FakeCheck.Returning("voices", SelfTestResult.Skip("none")),
            interactive,
        ], ["preconditions"]);

        var r = await stage.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal("1 passed, 0 failed, 1 skipped.", r.Detail);
        Assert.Equal(0, interactive.Runs);
        Assert.Equal(2, _progress.Count);
        Assert.StartsWith("player pass (", _progress[0]);
        Assert.Equal(new[] { "preconditions" }, stage.Needs);
    }

    [Fact]
    public async Task Checks_AnyFail_FailsAndNamesIt()
    {
        var stage = new ChecksStage("checks", [
            FakeCheck.Returning("backend", SelfTestResult.Fail("down")),
            FakeCheck.Returning("player", SelfTestResult.Pass("")),
        ], []);

        var r = await stage.RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.EndsWith(": backend.", r.Detail);
    }

    [Theory]
    [InlineData(false, Dalamud.Game.Text.XivChatType.FreeCompany)]
    [InlineData(true, Dalamud.Game.Text.XivChatType.Echo)]
    public void ChatHook_ProbesWithATypeThatIsNotVoiced(bool fcVoiced, Dalamud.Game.Text.XivChatType expected)
    {
        var config = new Configuration { VoiceChat = true, VoiceChatFreeCompany = fcVoiced };
        Assert.Equal(expected, ChatHookSpikeStage.ProbeType(config));
    }

    // --- backup ---------------------------------------------------------------------------

    private bool? _suppress;
    private readonly Mock<IEchokrauTtsInstanceService> _ek = new();
    private readonly Mock<IAlltalkInstanceService> _at = new();

    private E2eContext BackupCtx(E2eState state) =>
        new(state, () => _saves++, _progress.Add, v => _suppress = v);

    private BackupStage Backup(string dll, Func<int, bool> portOpen, Action<string, DateTime>? touch = null,
        Configuration? config = null) =>
        new(_ek.Object, _at.Object, config ?? new Configuration(), dll, (p, _) => Task.FromResult(portOpen(p)),
            () => new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc), touch ?? ((_, _) => { }),
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5));

    [Fact]
    public async Task Backup_PhaseA_StopsEngineSuppressesSaveTouchesDll_RevertsWithoutReload()
    {
        var dll = Path.GetTempFileName();
        try
        {
            var state = E2eStateStoreTests.State();
            string? touched = null;
            E2ePhase phaseAtTouch = default;
            bool? suppressAtTouch = null;
            var stage = Backup(dll, _ => false, (p, _) => { touched = p; phaseAtTouch = state.Phase; suppressAtTouch = _suppress; });

            var r = await stage.RunAsync(BackupCtx(state), CancellationToken.None);

            _ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Once);
            _at.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Once);
            Assert.Equal(dll, touched);
            Assert.Equal(E2ePhase.ResetPending, phaseAtTouch);
            Assert.True(suppressAtTouch);
            Assert.Contains("reload-requested", _progress);
            // no reload came: everything back
            Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
            Assert.Contains("No reload", r.Detail);
            Assert.Equal(E2ePhase.Requested, state.Phase);
            Assert.False(_suppress);
            Assert.False(state.Marks.ContainsKey(E2eStartupReset.ResetRequestedMark));
        }
        finally
        {
            File.Delete(dll);
        }
    }

    [Fact]
    public async Task Backup_PortStillInUse_FailsWithoutTouching()
    {
        var dll = Path.GetTempFileName();
        try
        {
            var touched = false;
            var state = E2eStateStoreTests.State();
            var r = await Backup(dll, p => p == BackupStage.DefaultEnginePort, (_, _) => touched = true)
                .RunAsync(BackupCtx(state), CancellationToken.None);

            Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
            Assert.Contains("8765", r.Detail);
            Assert.False(touched);
            Assert.Equal(E2ePhase.Requested, state.Phase);
            Assert.Null(_suppress);
        }
        finally
        {
            File.Delete(dll);
        }
    }

    [Fact]
    public void Backup_Ports_IncludeTheConfiguredLocalPort()
    {
        var config = new Configuration();
        config.EchokrauTts.BaseUrl = "http://127.0.0.1:9001";
        Assert.Equal(new[] { 8765, 9001 }, Backup("x.dll", _ => false, config: config).Ports());
        config.EchokrauTts.BaseUrl = "https://tts.example.net";
        Assert.Equal(new[] { 8765 }, Backup("x.dll", _ => false, config: config).Ports());
    }

    [Fact]
    public async Task Backup_AfterPhaseB_PassesWithoutStopping()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.ResetDone;
        state.BackupDir = "backup-1";
        state.Marks[E2eStartupReset.BackupFilesMark] = "7";

        var r = await Backup("nope.dll", _ => true).RunAsync(BackupCtx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("7 files", r.Detail);
        _ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Never);
    }

    [Fact]
    public async Task Backup_PhaseBFailed_Fails()
    {
        var state = E2eStateStoreTests.State();
        state.Marks[E2eStartupReset.BackupErrorMark] = "IOException: SHA-256 differs";

        var r = await Backup("nope.dll", _ => false).RunAsync(BackupCtx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("SHA-256", r.Detail);
    }

    [Fact]
    public async Task Backup_ReloadedButPhaseBDidNotAct_FailsAndReverts()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.ResetPending;
        state.Marks[E2eStartupReset.ResetRequestedMark] = "r1";

        var r = await Backup("nope.dll", _ => false).RunAsync(BackupCtx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(E2ePhase.Requested, state.Phase);
        Assert.False(state.Marks.ContainsKey(E2eStartupReset.ResetRequestedMark));
    }

    [Fact]
    public async Task Backup_DllMissing_Fails()
    {
        var r = await Backup(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll"), _ => false)
            .RunAsync(BackupCtx(E2eStateStoreTests.State()), CancellationToken.None);
        Assert.Contains("not found", r.Detail);
    }

    // --- restore (phase A; the ctor part is in E2eStartupResetTests) ----------------------------

    private RestoreStage Restore(string dll, string configDir, Func<int, bool>? portOpen = null,
        Action<string, DateTime>? touch = null, IOwnedWindowsService? windows = null) =>
        new(new E2eSelfReload(_ek.Object, _at.Object, new Configuration(), dll,
            (p, _) => Task.FromResult((portOpen ?? (_ => false))(p)),
            () => new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc), touch ?? ((_, _) => { }),
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5),
            windows, windowWait: TimeSpan.FromMilliseconds(50)), configDir);

    /// <summary>Restore phase A up to the touch, with the given window service; returns (result, touched).</summary>
    private async Task<(SelfTestResult Result, bool Touched)> RestoreWithWindows(IOwnedWindowsService windows)
    {
        var root = TempRoot();
        var dll = Path.Combine(root, "Echokraut.dll");
        try
        {
            var (configDir, backup) = RestoreFixture(root);
            File.WriteAllText(dll, "");
            var state = E2eStateStoreTests.State();
            state.Phase = E2ePhase.Stages;
            state.ConfigIsE2e = true;
            state.BackupDir = backup;
            var touched = false;
            var r = await Restore(dll, configDir, touch: (_, _) => touched = true, windows: windows)
                .RunAsync(BackupCtx(state), CancellationToken.None);
            return (r, touched);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Restore_WindowOpen_ClosesItAndWaitsForTheFinalizeBeforeTouching()
    {
        // Run 20261123-130938: the config window opened by the wizard was still alive at unload and
        // crashed the game when finalized afterwards.
        var windows = new OwnedWindowsService();
        windows.ReportAlive(true);
        var closeRequests = 0;
        windows.CloseAllRequested += () => { closeRequests++; windows.ReportAlive(false); };

        var (r, touched) = await RestoreWithWindows(windows);

        Assert.Equal(1, closeRequests);
        Assert.True(touched);
        Assert.False(windows.AnyAlive);
        Assert.Contains("No reload", r.Detail);
    }

    [Fact]
    public async Task Restore_WindowStaysAlive_FailsWithoutTouching()
    {
        var windows = new OwnedWindowsService();
        windows.ReportAlive(true);

        var (r, touched) = await RestoreWithWindows(windows);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("still alive", r.Detail);
        Assert.False(touched);
    }

    [Fact]
    public async Task Restore_NoWindowAlive_DoesNotAskToClose()
    {
        var windows = new OwnedWindowsService();
        var closeRequests = 0;
        windows.CloseAllRequested += () => closeRequests++;

        var (_, touched) = await RestoreWithWindows(windows);

        Assert.Equal(0, closeRequests);
        Assert.True(touched);
    }

    /// <summary>A pluginConfigs folder with a one-file backup + manifest; returns (configDir, backupDir).</summary>
    private static (string ConfigDir, string BackupDir) RestoreFixture(string root)
    {
        var configDir = Path.Combine(root, "Echokraut");
        var backup = Path.Combine(root, "Echokraut-e2e", "backup-1");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(backup);
        var file = Path.Combine(backup, "Echokraut.json");
        File.WriteAllText(file, "{}");
        File.WriteAllText(Path.Combine(backup, E2eStartupReset.ManifestFileName),
            $"{{\"id\":\"r1\",\"createdUtc\":\"2026-11-22T10:00:00Z\",\"files\":[{{\"path\":\"Echokraut.json\",\"sha256\":\"{E2eStartupReset.Sha256(file)}\",\"size\":2}}]}}");
        return (configDir, backup);
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "ek-e2e-restore-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Restore_RealConfigAlready_Skips()
    {
        var r = await Restore("x.dll", "cfg").RunAsync(BackupCtx(E2eStateStoreTests.State()), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        _ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Never);
    }

    [Fact]
    public async Task Restore_PhaseA_StopsEngineTouchesDllAsRestorePending_RevertsWithoutReload()
    {
        var root = TempRoot();
        var dll = Path.Combine(root, "Echokraut.dll");
        try
        {
            var (configDir, backup) = RestoreFixture(root);
            File.WriteAllText(dll, "");
            var state = E2eStateStoreTests.State();
            state.Phase = E2ePhase.Stages;
            state.ConfigIsE2e = true;
            state.BackupDir = backup;
            E2ePhase phaseAtTouch = default;
            bool? suppressAtTouch = null;

            var r = await Restore(dll, configDir, touch: (_, _) => { phaseAtTouch = state.Phase; suppressAtTouch = _suppress; })
                .RunAsync(BackupCtx(state), CancellationToken.None);

            _ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Once);
            Assert.Equal(E2ePhase.RestorePending, phaseAtTouch);
            Assert.True(suppressAtTouch);
            Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
            Assert.Contains("No reload", r.Detail);
            Assert.Equal(E2ePhase.Stages, state.Phase);
            Assert.False(_suppress);
            Assert.False(state.Marks.ContainsKey(E2eStartupReset.RestoreRequestedMark));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Restore_BackupChanged_FailsBeforeStoppingTheEngine()
    {
        var root = TempRoot();
        try
        {
            var (configDir, backup) = RestoreFixture(root);
            File.WriteAllText(Path.Combine(backup, "Echokraut.json"), "changed");
            var state = E2eStateStoreTests.State();
            state.ConfigIsE2e = true;
            state.BackupDir = backup;

            var r = await Restore("x.dll", configDir).RunAsync(BackupCtx(state), CancellationToken.None);

            Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
            Assert.Contains("SHA-256", r.Detail);
            _ek.Verify(e => e.StopInstance(It.IsAny<EKEventId>()), Times.Never);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Restore_AfterTheCtorRestored_Passes()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.RestoreDone;
        state.BackupDir = "backup-1";
        state.Marks[E2eStartupReset.RestoreFilesMark] = "5";

        var r = await Restore("x.dll", "cfg").RunAsync(BackupCtx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("5 files restored", r.Detail);
    }

    [Theory]
    [InlineData(E2ePhase.RestoreStarted, "resumes on the next plugin load")]
    [InlineData(E2ePhase.Stages, "E2E config untouched")]
    public async Task Restore_CtorFailed_Fails(E2ePhase phase, string expected)
    {
        var state = E2eStateStoreTests.State();
        state.Phase = phase;
        state.ConfigIsE2e = true;
        state.Marks[E2eStartupReset.RestoreErrorMark] = "IOException: x";

        var r = await Restore("x.dll", "cfg").RunAsync(BackupCtx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains(expected, r.Detail);
    }

    [Fact]
    public async Task Restore_ReloadedButCtorDidNotAct_FailsAndReverts()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.RestorePending;
        state.ConfigIsE2e = true;
        state.Marks[E2eStartupReset.RestoreRequestedMark] = "r1";

        var r = await Restore("x.dll", "cfg").RunAsync(BackupCtx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(E2ePhase.Stages, state.Phase);
        Assert.False(state.Marks.ContainsKey(E2eStartupReset.RestoreRequestedMark));
    }

    [Fact]
    public void Restore_IsOptInAndRunsAfterAnAbort()
    {
        var stage = Restore("x.dll", "cfg");
        Assert.True(stage.ExplicitOnly);
        Assert.True(stage.RunsAfterAbort);
        Assert.False(stage.AbortsRunUnlessPass);
    }

    [Fact]
    public void BackupAndReset_EndTheRunUnlessPass()
    {
        Assert.True(Backup("x.dll", _ => false).AbortsRunUnlessPass);
        Assert.True(new ResetStage(new Configuration(), new Mock<IDatabaseService>().Object).AbortsRunUnlessPass);
    }

    // --- reset ----------------------------------------------------------------------------

    private static ResetStage Reset(bool firstTime, int characters, int clips)
    {
        var db = new Mock<IDatabaseService>();
        db.Setup(d => d.GetAllCharacters()).Returns(Enumerable.Range(0, characters)
            .Select(_ => new Echokraut.DataClasses.Database.CharacterEntity()).ToList());
        db.Setup(d => d.GetVoiceClipCount(null, null, null, null)).Returns(clips);
        return new ResetStage(new Configuration { FirstTime = firstTime }, db.Object);
    }

    [Fact]
    public async Task Reset_FreshConfigAndEmptyDb_PassesAndMovesToStages()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.ResetDone;

        var r = await Reset(true, 0, 0).RunAsync(Ctx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(E2ePhase.Stages, state.Phase);
        Assert.Equal(1, _saves);
    }

    [Fact]
    public async Task Reset_LeftoversInConfigOrDb_Fail()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.ResetDone;

        var r = await Reset(false, 2, 3).RunAsync(Ctx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        foreach (var expected in new[] { "FirstTime", "2 characters", "3 voice clips" }) Assert.Contains(expected, r.Detail);
        Assert.Equal(E2ePhase.ResetDone, state.Phase);
    }

    [Theory]
    [InlineData(E2ePhase.WipeStarted, true)]
    [InlineData(E2ePhase.BackupDone, false)]
    public async Task Reset_WipeNotFinished_Fails(E2ePhase phase, bool withError)
    {
        var state = E2eStateStoreTests.State();
        state.Phase = phase;
        if (withError) state.Marks[E2eStartupReset.ResetErrorMark] = "IOException: locked";

        var r = await Reset(true, 0, 0).RunAsync(Ctx(state), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains(withError ? "locked" : "did not finish", r.Detail);
    }
}
