using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest;

public sealed class SelfTestServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ek-selftest-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ICommandService> _commands = new();
    private readonly List<string> _chat = new();
    private readonly SelfTestRunGate _gate = new();
    private DateTime _now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    public SelfTestServiceTests()
    {
        _commands.Setup(c => c.PrintText(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string _, string text) => _chat.Add(text));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private SelfTestService Create(params SelfTestCheck[] checks) =>
        new(checks, _commands.Object, Log().Object, _dir, () => "German", _gate, () => _now);

    [Fact]
    public void Checks_InteractiveOnesRunLast_OrderOtherwiseKept()
    {
        var service = Create(
            FakeCheck.Returning("i1", SelfTestResult.Pass(""), interactive: true),
            FakeCheck.Returning("a1", SelfTestResult.Pass("")),
            FakeCheck.Returning("a2", SelfTestResult.Pass("")));

        Assert.Equal(new[] { "a1", "a2", "i1" }, service.Checks.Select(c => c.Id));
    }

    [Fact]
    public async Task Run_WithoutInteractive_LeavesThemOut()
    {
        var interactive = FakeCheck.Returning("i", SelfTestResult.Pass(""), interactive: true);
        var service = Create(FakeCheck.Returning("a", SelfTestResult.Pass("")), interactive);

        var report = await service.RunAsync(includeInteractive: false);

        Assert.NotNull(report);
        Assert.Single(report!.Entries);
        Assert.Equal(0, interactive.Runs);
        Assert.False(report.IncludedInteractive);
    }

    [Fact]
    public async Task Run_WithInteractive_PrintsTheInstructionFirst()
    {
        var interactive = new FakeCheck("i", _ => Task.FromResult(SelfTestResult.Pass("")), interactive: true, instruction: "Talk to someone.");
        var service = Create(interactive);

        var report = await service.RunAsync(includeInteractive: true);

        Assert.Equal(1, interactive.Runs);
        Assert.Contains(_chat, l => l.EndsWith("Talk to someone."));
        Assert.True(report!.IncludedInteractive);
    }

    [Fact]
    public async Task Run_CountsOutcomes_AndPrintsFailuresAndSummary()
    {
        var service = Create(
            FakeCheck.Returning("p", SelfTestResult.Pass("")),
            FakeCheck.Returning("f", SelfTestResult.Fail("broken")),
            FakeCheck.Returning("s", SelfTestResult.Skip("off")));

        var report = await service.RunAsync(false);

        Assert.Equal((1, 1, 1), (report!.Passed, report.Failed, report.Skipped));
        Assert.Contains(_chat, l => l.Contains("Fake f") && l.Contains("broken"));
        Assert.Contains(_chat, l => l.StartsWith("Self-test: 1 passed, 1 failed, 1 skipped."));
    }

    [Fact]
    public async Task Run_WritesRunFileAndLatest_WithEnumsAsText()
    {
        var service = Create(FakeCheck.Returning("p", SelfTestResult.Pass("ok")));

        await service.RunAsync(false);

        var latest = Path.Combine(_dir, SelfTestService.LatestFileName);
        Assert.True(File.Exists(latest));
        Assert.True(File.Exists(Path.Combine(_dir, "selftest-20261118-120000.json")));
        using var doc = JsonDocument.Parse(File.ReadAllText(latest));
        Assert.Equal("German", doc.RootElement.GetProperty("Language").GetString());
        Assert.Equal("Pass", doc.RootElement.GetProperty("Entries")[0].GetProperty("Outcome").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("Passed").GetInt32());
    }

    [Fact]
    public async Task Run_KeepsOnlyTheNewestReports()
    {
        var service = Create(FakeCheck.Returning("p", SelfTestResult.Pass("")));
        for (var i = 0; i < SelfTestService.KeptReports + 3; i++)
        {
            _now = _now.AddMinutes(1);
            await service.RunAsync(false);
        }

        var runs = Directory.GetFiles(_dir, "selftest-*.json").Where(f => !f.EndsWith(SelfTestService.LatestFileName)).ToList();
        Assert.Equal(SelfTestService.KeptReports, runs.Count);
        Assert.True(File.Exists(Path.Combine(_dir, $"selftest-{_now:yyyyMMdd-HHmmss}.json")));
    }

    [Fact]
    public async Task Run_WhileRunning_ReturnsNullAndRunsNothingTwice()
    {
        var gate = new TaskCompletionSource<SelfTestResult>();
        var check = new FakeCheck("slow", _ => gate.Task);
        var service = Create(check);

        var first = service.RunAsync(false);
        Assert.True(service.IsRunning);
        var second = await service.RunAsync(false);
        gate.SetResult(SelfTestResult.Pass(""));
        await first;

        Assert.Null(second);
        Assert.Equal(1, check.Runs);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task Run_WhileAnE2eRunHoldsTheGate_ReturnsNullAndRunsNothing()
    {
        var check = FakeCheck.Returning("a", SelfTestResult.Pass(""));
        var service = Create(check);
        _gate.TryEnter("e2e r1");

        var report = await service.RunAsync(false);

        Assert.Null(report);
        Assert.Equal(0, check.Runs);
        Assert.False(service.IsRunning);
        Assert.Contains(_chat, c => c.Contains("E2E"));
    }

    [Fact]
    public async Task Run_UnwritableDirectory_StillReturnsTheReport()
    {
        File.WriteAllText(_dir, "a file where the directory should be");
        try
        {
            var report = await Create(FakeCheck.Returning("p", SelfTestResult.Pass("fine"))).RunAsync(false);

            var entry = Assert.Single(report!.Entries);
            Assert.Equal(("p", SelfTestOutcome.Pass, "fine"), (entry.Id, entry.Outcome, entry.Detail));
            Assert.Equal(1, report.Passed);
            // The chat summary still comes, just without a report path.
            Assert.DoesNotContain(_chat, l => l.Contains("Report:"));
            Assert.Contains(_chat, l => l == SelfTestService.Summary(report));
        }
        finally
        {
            File.Delete(_dir);
        }
    }
}
