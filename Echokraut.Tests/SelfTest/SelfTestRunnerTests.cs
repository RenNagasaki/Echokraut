using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Services.SelfTest;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest;

public class SelfTestRunnerTests
{
    [Fact]
    public async Task Pass_IsReportedWithIdNameAndDetail()
    {
        var entry = await SelfTestRunner.RunCheckAsync(FakeCheck.Returning("a", SelfTestResult.Pass("fine")), CancellationToken.None);

        Assert.Equal("a", entry.Id);
        Assert.Equal("Fake a", entry.Name);
        Assert.Equal(SelfTestOutcome.Pass, entry.Outcome);
        Assert.Equal("fine", entry.Detail);
    }

    [Fact]
    public async Task Exception_BecomesAFailure()
    {
        var check = new FakeCheck("a", _ => throw new InvalidOperationException("boom"));

        var entry = await SelfTestRunner.RunCheckAsync(check, CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, entry.Outcome);
        Assert.Equal("InvalidOperationException: boom", entry.Detail);
    }

    [Fact]
    public async Task Timeout_BecomesAFailureNamingTheLimit()
    {
        var check = new FakeCheck("slow", async ct => { await Task.Delay(Timeout.Infinite, ct); return SelfTestResult.Pass(""); },
            timeout: TimeSpan.FromMilliseconds(50));

        var entry = await SelfTestRunner.RunCheckAsync(check, CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, entry.Outcome);
        Assert.StartsWith("Timed out", entry.Detail);
    }

    [Fact]
    public async Task OuterCancellation_IsNotCalledATimeout()
    {
        using var cts = new CancellationTokenSource();
        var check = new FakeCheck("a", async ct => { await Task.Delay(Timeout.Infinite, ct); return SelfTestResult.Pass(""); });
        var run = SelfTestRunner.RunCheckAsync(check, cts.Token);

        cts.Cancel();
        var entry = await run;

        Assert.Equal(SelfTestOutcome.Fail, entry.Outcome);
        Assert.Equal("Cancelled.", entry.Detail);
    }

    [Fact]
    public async Task Skip_StaysSkipped()
    {
        var entry = await SelfTestRunner.RunCheckAsync(FakeCheck.Returning("a", SelfTestResult.Skip("off")), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Skipped, entry.Outcome);
    }
}
