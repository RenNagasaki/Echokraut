using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.DataClasses;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest.E2e;

public sealed class E2eRunServiceTests : IDisposable
{
    private const string Host = "tts.example.net";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ek-e2e-run-" + Guid.NewGuid().ToString("N"));
    private readonly E2eStateStore _store;
    private readonly SelfTestRunGate _gate = new();
    private readonly Mock<ICommandService> _commands = new();
    private readonly Mock<Echotools.Logging.Services.ILogService> _log = Log();
    private readonly List<string> _lines = new();
    private readonly List<string> _warnings = new();
    private DateTime _now = new(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
    private long _tick;

    public E2eRunServiceTests()
    {
        _store = new E2eStateStore(_dir);
        _log.Setup(l => l.Info(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EchoEventId>()))
            .Callback((string _, string m, EchoEventId _) => { lock (_lines) _lines.Add(m); });
        _log.Setup(l => l.Warning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EchoEventId>()))
            .Callback((string _, string m, EchoEventId _) => { lock (_lines) { _lines.Add(m); _warnings.Add(m); } });
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private sealed class FakeStage : E2eStage
    {
        private readonly Func<E2eContext, CancellationToken, Task<SelfTestResult>> _run;

        public FakeStage(string id, SelfTestResult result, string[]? needs = null, bool aborts = false)
            : this(id, (_, _) => Task.FromResult(result), needs, aborts) { }

        public FakeStage(string id, Func<E2eContext, CancellationToken, Task<SelfTestResult>> run, string[]? needs = null, bool aborts = false)
        {
            Id = id;
            _run = run;
            Needs = needs ?? [];
            AbortsRunUnlessPass = aborts;
        }

        public override string Id { get; }
        public override IReadOnlyList<string> Needs { get; }
        public override bool AbortsRunUnlessPass { get; }
        public override TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public bool Explicit { get; init; }
        public bool AfterAbort { get; init; }
        public bool Always { get; init; }
        public override bool AlwaysRuns => Always;
        public override bool ExplicitOnly => Explicit;
        public override bool RunsAfterAbort => AfterAbort;
        public int Runs;

        public override Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            return _run(ctx, ct);
        }
    }

    private E2eRunService Create(bool isDev = true, params E2eStage[] stages) =>
        new(stages, _store, _gate, _commands.Object, _log.Object, isDev, () => "German", () => _now, () => _tick);

    private void Request(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.RequestPath, json);
    }

    private void Request(string id = "r1", string? stages = null) =>
        Request($$"""{ "id": "{{id}}", "installRoot": "E:\\EkE2E", "remoteUrl": "https://{{Host}}"{{(stages == null ? "" : $", \"stages\": {stages}")}} }""");

    private static async Task Await(E2eRunService s)
    {
        if (s.CurrentRun != null) await s.CurrentRun.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Request_RunsStagesInOrder_WritesReportAndDoneLine()
    {
        var a = new FakeStage("a", SelfTestResult.Pass("ok"));
        var b = new FakeStage("b", SelfTestResult.Skip("n/a"), ["a"]);
        var service = Create(true, a, b);
        Request();

        service.PollOnce();
        await Await(service);

        Assert.False(_store.RequestExists);
        var state = _store.Load()!;
        Assert.Equal(E2ePhase.Finished, state.Phase);
        Assert.Equal(new[] { "a", "b" }, state.StageResults.Select(r => r.Id));
        Assert.Contains("EKTEST r1 run start a,b", _lines);
        Assert.Contains("EKTEST r1 a start", _lines);
        Assert.Contains("EKTEST r1 a pass ok", _lines);
        Assert.Contains("EKTEST r1 b skip n/a", _lines);
        Assert.Contains(_lines, l => l.StartsWith("EKTEST r1 done 1/0/1 ") && l.EndsWith("e2e-r1.json"));
        Assert.True(File.Exists(_store.ReportPath("r1")));
        Assert.Null(_gate.Owner);
    }

    [Fact]
    public async Task ExplicitOnlyStage_LeftOutWithoutStageList_RunsWhenListed()
    {
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var restore = new FakeStage("restore", SelfTestResult.Pass("")) { Explicit = true };
        var service = Create(true, a, restore);
        Request(id: "r1");
        service.PollOnce();
        await Await(service);
        Assert.Equal(0, restore.Runs);

        Request("r2", "[\"restore\"]");
        service.PollOnce();
        await Await(service);
        Assert.Equal(1, restore.Runs);
        Assert.Equal(1, a.Runs);
    }

    [Fact]
    public async Task RunsAfterAbortStage_StillRunsWhenAnEarlierStageEndedTheRun()
    {
        var pre = new FakeStage("preconditions", SelfTestResult.Fail("no"), aborts: true);
        var mid = new FakeStage("checks", SelfTestResult.Pass(""));
        var restore = new FakeStage("restore", SelfTestResult.Pass("")) { Explicit = true, AfterAbort = true };
        var service = Create(true, pre, mid, restore);
        Request("r1", "[\"preconditions\",\"checks\",\"restore\"]");

        service.PollOnce();
        await Await(service);

        Assert.Equal(0, mid.Runs);
        Assert.Equal(1, restore.Runs);
        Assert.Equal(E2ePhase.Aborted, _store.Load()!.Phase);
    }

    [Fact]
    public async Task AlwaysRunsStage_RunsWhenTheStageListLeavesItOut_AndAfterAnAbort()
    {
        var pre = new FakeStage("preconditions", SelfTestResult.Fail("no"), aborts: true);
        var cleanup = new FakeStage("cleanup", SelfTestResult.Pass("clean")) { Always = true, AfterAbort = true };
        var service = Create(true, pre, cleanup);
        Request("r1", "[\"preconditions\"]");

        service.PollOnce();
        await Await(service);

        Assert.Equal(1, cleanup.Runs);
        Assert.Contains("EKTEST r1 run start preconditions,cleanup", _lines);
    }

    [Fact]
    public async Task FinishOnAnE2eConfig_WarnsBeforeTheDoneLine_AndInChat()
    {
        var stage = new FakeStage("reset", (ctx, _) => { ctx.State.ConfigIsE2e = true; return Task.FromResult(SelfTestResult.Pass("")); });
        var service = Create(true, stage);
        Request();

        service.PollOnce();
        await Await(service);

        var warn = _lines.FindIndex(l => l.StartsWith("EKTEST r1 run warn The plugin runs on the E2E config until restore"));
        var done = _lines.FindIndex(l => l.StartsWith("EKTEST r1 done "));
        Assert.InRange(warn, 0, done - 1);
        _commands.Verify(c => c.PrintText("", It.Is<string>(t => t.Contains("restore.sh --live"))), Times.Once);
    }

    [Fact]
    public async Task FinishOnTheRealConfig_NoRestoreWarning()
    {
        var service = Create(true, new FakeStage("a", SelfTestResult.Pass("")));
        Request();

        service.PollOnce();
        await Await(service);

        Assert.DoesNotContain(_lines, l => l.Contains(" run warn "));
        _commands.Verify(c => c.PrintText("", It.Is<string>(t => t.Contains("restore.sh --live"))), Times.Never);
    }

    [Fact]
    public async Task NewRequest_AfterAnE2eConfig_CarriesItsBackupDir()
    {
        var previous = E2eStateStoreTests.State();
        previous.Phase = E2ePhase.Finished;
        previous.ConfigIsE2e = true;
        previous.BackupDir = @"C:\b\backup-1";
        previous.UpdatedUtc = _now;
        _store.Save(previous);
        var seen = (string?)null;
        var stage = new FakeStage("restore", (ctx, _) => { seen = ctx.State.BackupDir; return Task.FromResult(SelfTestResult.Pass("")); }) { Explicit = true };
        var service = Create(true, stage);
        Request("r2", "[\"restore\"]");

        service.PollOnce();
        await Await(service);

        Assert.Equal(@"C:\b\backup-1", seen);
    }

    [Fact]
    public async Task AbortingStageNotPassing_EndsTheRun()
    {
        var pre = new FakeStage("preconditions", SelfTestResult.Fail("not logged in"), aborts: true);
        var next = new FakeStage("checks", SelfTestResult.Pass(""));
        var service = Create(true, pre, next);
        Request();

        service.PollOnce();
        await Await(service);

        Assert.Equal(0, next.Runs);
        Assert.Equal(E2ePhase.Aborted, _store.Load()!.Phase);
        Assert.Contains("EKTEST r1 preconditions fail not logged in", _warnings);
    }

    [Fact]
    public async Task AbortingStageSkipped_AlsoEndsTheRun()
    {
        var pre = new FakeStage("backup", SelfTestResult.Skip("?"), aborts: true);
        var next = new FakeStage("x", SelfTestResult.Pass(""));
        var service = Create(true, pre, next);
        Request();

        service.PollOnce();
        await Await(service);

        Assert.Equal(0, next.Runs);
    }

    [Fact]
    public async Task FailedDependency_SkipsDependent_IndependentStillRuns()
    {
        var a = new FakeStage("a", SelfTestResult.Fail("boom"));
        var b = new FakeStage("b", SelfTestResult.Pass(""), ["a"]);
        var c = new FakeStage("c", SelfTestResult.Pass(""));
        var service = Create(true, a, b, c);
        Request();

        service.PollOnce();
        await Await(service);

        var state = _store.Load()!;
        Assert.Equal(0, b.Runs);
        Assert.Equal("needs a (fail)", state.ResultOf("b")!.Detail);
        Assert.Equal(SelfTestOutcome.Skipped, state.ResultOf("b")!.Outcome);
        Assert.Equal(1, c.Runs);
    }

    [Fact]
    public async Task StagesFilter_RunsOnlyTheNamedOnes_DependencyOutsideIsNotRun()
    {
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var b = new FakeStage("b", SelfTestResult.Pass(""), ["a"]);
        var service = Create(true, a, b);
        Request(stages: "[\"b\"]");

        service.PollOnce();
        await Await(service);

        Assert.Equal(0, a.Runs);
        Assert.Equal("needs a (not run)", _store.Load()!.ResultOf("b")!.Detail);
    }

    [Fact]
    public async Task Resume_RestartsTheRunningStage_KeepsFinishedOnes()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.Stages;
        state.UpdatedUtc = _now.AddMinutes(-1);
        state.StageResults.Add(new E2eStageReport("a", SelfTestOutcome.Pass, "", 1));
        state.RunningStage = "b";
        _store.Save(state);
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var b = new FakeStage("b", SelfTestResult.Pass(""));
        var c = new FakeStage("c", SelfTestResult.Pass(""));
        var service = Create(true, a, b, c);

        service.Start();
        await Await(service);

        Assert.Equal(0, a.Runs);
        Assert.Equal(1, b.Runs);
        Assert.Equal(1, c.Runs);
        Assert.Contains("EKTEST r1 run resume b", _lines);
        Assert.Equal(E2ePhase.Finished, _store.Load()!.Phase);
    }

    [Fact]
    public async Task Resume_StaleState_IsClosedAsAborted()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.Stages;
        state.UpdatedUtc = _now - E2eRunService.StaleAfter - TimeSpan.FromMinutes(1);
        _store.Save(state);
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var service = Create(true, a);

        service.Start();
        await Await(service);

        Assert.Equal(0, a.Runs);
        Assert.Equal(E2ePhase.Aborted, _store.Load()!.Phase);
        Assert.Contains(_lines, l => l.StartsWith("EKTEST r1 done 0/0/0"));
    }

    [Fact]
    public async Task Resume_FinishedState_DoesNothing()
    {
        var state = E2eStateStoreTests.State();
        state.Phase = E2ePhase.Finished;
        _store.Save(state);
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var service = Create(true, a);

        service.Start();
        await Await(service);

        Assert.Equal(0, a.Runs);
    }

    [Fact]
    public void NotDev_IgnoresRequestAndState_WarnsOnce()
    {
        _store.Save(E2eStateStoreTests.State());
        Request();
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var service = Create(false, a);

        service.Start();
        service.PollOnce();
        service.PollOnce();

        Assert.Equal(0, a.Runs);
        Assert.True(_store.RequestExists);
        Assert.Single(_warnings);
        Assert.Contains("dev load", _warnings[0]);
    }

    [Fact]
    public void InvalidRequest_LogsFailWithItsId_RunsNothing()
    {
        Request("""{ "id": "bad-1" }""");
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var service = Create(true, a);

        service.PollOnce();

        Assert.Null(service.CurrentRun);
        Assert.Contains(_warnings, w => w.StartsWith("EKTEST bad-1 request fail") && w.Contains("installRoot"));
        Assert.False(_store.RequestExists);
    }

    [Fact]
    public void GateHeldBySelfTest_RefusesTheRun()
    {
        _gate.TryEnter("self-test");
        Request();
        var a = new FakeStage("a", SelfTestResult.Pass(""));
        var service = Create(true, a);

        service.PollOnce();

        Assert.Null(service.CurrentRun);
        Assert.Contains(_warnings, w => w.StartsWith("EKTEST r1 run fail") && w.Contains("self-test"));
    }

    [Fact]
    public async Task Report_AndLog_NeverContainTheRemoteHost()
    {
        var a = new FakeStage("a", SelfTestResult.Fail($"HttpRequestException: {Host.ToUpperInvariant()}:443 refused"));
        var b = new FakeStage("b", (ctx, _) =>
        {
            ctx.Progress($"talking to https://{Host}/health");
            return Task.FromResult(SelfTestResult.Pass(""));
        });
        var service = Create(true, a, b);
        Request();

        service.PollOnce();
        await Await(service);

        Assert.DoesNotContain("example", File.ReadAllText(_store.ReportPath("r1")), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_lines, l => l.Contains("example", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EKTEST r1 b progress talking to <remote>/health", _lines);
    }

    [Fact]
    public async Task ThrowingStage_IsAFail_RunGoesOn()
    {
        var a = new FakeStage("a", (_, _) => throw new InvalidOperationException("kaputt"));
        var b = new FakeStage("b", SelfTestResult.Pass(""));
        var service = Create(true, a, b);
        Request();

        service.PollOnce();
        await Await(service);

        Assert.Equal("InvalidOperationException: kaputt", _store.Load()!.ResultOf("a")!.Detail);
        Assert.Equal(1, b.Runs);
    }

    [Fact]
    public async Task Dispose_DuringAStage_StopsWithoutWritingTheResult()
    {
        var entered = new TaskCompletionSource();
        var a = new FakeStage("a", async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return SelfTestResult.Pass("");
        });
        var b = new FakeStage("b", SelfTestResult.Pass(""));
        var service = Create(true, a, b);
        Request();

        service.PollOnce();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        await Await(service);

        var state = _store.Load()!;
        Assert.Equal("a", state.RunningStage); // the next load restarts it
        Assert.Empty(state.StageResults);
        Assert.Equal(0, b.Runs);
        Assert.False(File.Exists(_store.ReportPath("r1")));
    }

    [Fact]
    public async Task PreviousRunLeftAnE2eConfig_IsCarriedIntoTheNewRun()
    {
        var old = E2eStateStoreTests.State("old");
        old.Phase = E2ePhase.Finished;
        old.ConfigIsE2e = true;
        _store.Save(old);
        E2eContext? seen = null;
        var a = new FakeStage("a", (ctx, _) => { seen = ctx; return Task.FromResult(SelfTestResult.Pass("")); });
        var service = Create(true, a);
        Request();

        service.PollOnce();
        await Await(service);

        Assert.True(seen!.State.ConfigIsE2e);
        Assert.Equal("true", seen.State.Marks[PreconditionsStage.PreviousConfigIsE2eMark]);
    }

    [Fact]
    public async Task Tick_PollsAtMostEveryTwoSeconds()
    {
        var service = Create(true, new FakeStage("a", SelfTestResult.Pass("")));
        service.Tick();               // first tick polls (nothing there)
        Request();
        _tick += 1_000;
        service.Tick();               // too early
        Assert.True(_store.RequestExists);
        _tick += 1_000;
        service.Tick();
        Assert.False(_store.RequestExists);
        await Await(service);
    }

    [Fact]
    public async Task Tick_FeedsTheFrameMonitorEveryFrame_AndTheReportCarriesTheTelemetry()
    {
        long ts = 0;
        var frames = new E2eFrameMonitor(() => ts, ticksPerSecond: 1000);
        var stage = new FakeStage("a", (ctx, _) =>
        {
            ctx.State.Telemetry.VramBaselineMiB = 1234;
            return Task.FromResult(SelfTestResult.Pass(""));
        });
        var service = new E2eRunService([stage], _store, _gate, _commands.Object, _log.Object, true, () => "German",
            () => _now, () => _tick, frames);
        var w = frames.Begin();
        service.Tick();
        ts += 16;
        service.Tick(); // inside the poll interval: still a frame
        Assert.Equal(1, w.Stop().Frames);

        Request();
        service.PollOnce();
        await Await(service);
        Assert.Contains("\"VramBaselineMiB\": 1234", File.ReadAllText(_store.ReportPath("r1")));
    }

    [Fact]
    public void MissingNeed_ReportsFirstUnpassedDependency()
    {
        var state = E2eStateStoreTests.State();
        state.StageResults.Add(new E2eStageReport("a", SelfTestOutcome.Pass, "", 0));
        state.StageResults.Add(new E2eStageReport("b", SelfTestOutcome.Skipped, "", 0));

        Assert.Null(E2eRunService.MissingNeed(state, new FakeStage("x", SelfTestResult.Pass(""), ["a"])));
        Assert.Equal("needs b (skip)", E2eRunService.MissingNeed(state, new FakeStage("x", SelfTestResult.Pass(""), ["a", "b"])));
    }

    [Fact]
    public async Task ReportPart_IsStoredScrubbedAndLoggedAsItsOwnOutcome()
    {
        var stage = new FakeStage("scenarios", (ctx, _) =>
        {
            ctx.ReportPart(new E2eStageReport("scenarios:a", SelfTestOutcome.Pass, "fine", 3));
            ctx.ReportPart(new E2eStageReport("scenarios:b", SelfTestOutcome.Fail, $"host {Host} down", 4));
            return Task.FromResult(SelfTestResult.Fail("1 failed"));
        });
        var service = Create(true, stage);
        Request();

        service.PollOnce();
        await Await(service);

        var state = _store.Load()!;
        Assert.Equal(new[] { "scenarios:a", "scenarios:b", "scenarios" }, state.StageResults.Select(r => r.Id));
        Assert.DoesNotContain(Host, state.StageResults[1].Detail);
        Assert.Contains("EKTEST r1 scenarios:a pass fine", _lines);
        Assert.Contains("EKTEST r1 scenarios:b fail host <remote> down", _warnings);
        Assert.Contains(_lines, l => l.StartsWith("EKTEST r1 done 1/2/0 "));
    }
}
