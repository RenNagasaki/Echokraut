using Xunit;

namespace EchokrautLocalInstaller.Tests;

public class WrapperRestartPolicyTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private const int Restart = Constants.ECHOKRAUTTS_EXIT_RESTART_REQUIRED;

    [Fact]
    public void ExitCode_MatchesTheWrapper() => Assert.Equal(75, Restart);

    [Fact]
    public void RestartCode_IsRestarted()
        => Assert.True(new WrapperRestartPolicy().ShouldRestart(Restart, stopRequested: false, T0));

    [Theory]
    [InlineData(0)]    // normal stop
    [InlineData(1)]    // bootstrap failure — restarting would loop
    [InlineData(-1)]   // killed
    [InlineData(null)] // unknown (handle disposed by a stop / script never started)
    public void OtherExitCodes_AreFinal(int? code)
        => Assert.False(new WrapperRestartPolicy().ShouldRestart(code, stopRequested: false, T0));

    [Fact]
    public void StopRequested_WinsOverTheRestartCode()
        => Assert.False(new WrapperRestartPolicy().ShouldRestart(Restart, stopRequested: true, T0));

    [Fact]
    public void RestartsAreCappedWithinTheWindow()
    {
        var policy = new WrapperRestartPolicy();
        for (var i = 0; i < WrapperRestartPolicy.MaxRestarts; i++)
            Assert.True(policy.ShouldRestart(Restart, false, T0.AddMinutes(i)));

        Assert.False(policy.ShouldRestart(Restart, false, T0.AddMinutes(WrapperRestartPolicy.MaxRestarts)));
        Assert.Equal(WrapperRestartPolicy.MaxRestarts, policy.RecentRestarts(T0.AddMinutes(WrapperRestartPolicy.MaxRestarts)));
    }

    [Fact]
    public void OldRestarts_ExpireOutOfTheWindow()
    {
        var policy = new WrapperRestartPolicy();
        for (var i = 0; i < WrapperRestartPolicy.MaxRestarts; i++)
            Assert.True(policy.ShouldRestart(Restart, false, T0));

        var later = T0 + WrapperRestartPolicy.Window;
        Assert.Equal(0, policy.RecentRestarts(later));
        Assert.True(policy.ShouldRestart(Restart, false, later));
    }
}
