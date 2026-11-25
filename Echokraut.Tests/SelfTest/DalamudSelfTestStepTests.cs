using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Plugin.SelfTest;
using Echokraut.Services.SelfTest;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest;

// The checks here have no instruction, so RunStep never calls ImGui (no ImGui context in tests).
public class DalamudSelfTestStepTests
{
    private readonly List<SelfTestReportEntry> _finished = new();

    [Fact]
    public async Task Waits_UntilTheCheckIsDone_ThenPassesAndReportsOnce()
    {
        var gate = new TaskCompletionSource<SelfTestResult>();
        var step = new DalamudSelfTestStep(new FakeCheck("a", _ => gate.Task), _finished.Add);

        Assert.Equal(SelfTestStepResult.Waiting, step.RunStep());
        Assert.Equal(SelfTestStepResult.Waiting, step.RunStep());
        gate.SetResult(SelfTestResult.Pass("ok"));
        await Task.Delay(10);

        Assert.Equal(SelfTestStepResult.Pass, step.RunStep());
        Assert.Single(_finished);
    }

    [Theory]
    [InlineData(SelfTestOutcome.Pass, SelfTestStepResult.Pass)]
    [InlineData(SelfTestOutcome.Skipped, SelfTestStepResult.Pass)]
    [InlineData(SelfTestOutcome.Fail, SelfTestStepResult.Fail)]
    public void Outcome_MapsToTheWindowsTwoStates(SelfTestOutcome outcome, SelfTestStepResult expected)
    {
        Assert.Equal(expected, DalamudSelfTestStep.ToDalamud(outcome));
    }

    [Fact]
    public async Task AfterAResult_TheNextRunStartsFresh()
    {
        var check = FakeCheck.Returning("a", SelfTestResult.Pass(""));
        var step = new DalamudSelfTestStep(check, _finished.Add);

        await RunToEnd(step);
        await RunToEnd(step);

        Assert.Equal(2, check.Runs);
    }

    [Fact]
    public void Name_CarriesThePluginPrefix()
    {
        var step = new DalamudSelfTestStep(FakeCheck.Returning("a", SelfTestResult.Pass("")), _finished.Add);
        Assert.Equal("Echokraut: Fake a", step.Name);
    }

    [Fact]
    public async Task CleanUp_CancelsARunningCheck()
    {
        var cancelled = new TaskCompletionSource<bool>();
        var step = new DalamudSelfTestStep(new FakeCheck("a", async ct =>
        {
            ct.Register(() => cancelled.TrySetResult(true));
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return SelfTestResult.Pass("");
        }), _finished.Add);

        step.RunStep();
        step.CleanUp();

        Assert.True(await cancelled.Task.WaitAsync(System.TimeSpan.FromSeconds(2)));
    }

    private static async Task RunToEnd(DalamudSelfTestStep step)
    {
        for (var i = 0; i < 100 && step.RunStep() == SelfTestStepResult.Waiting; i++)
            await Task.Delay(5);
    }
}
