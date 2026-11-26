using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class OwnedWindowsServiceTests
{
    [Fact]
    public void AnyAlive_FollowsTheLastReport()
    {
        var s = new OwnedWindowsService();
        Assert.False(s.AnyAlive);
        s.ReportAlive(true);
        Assert.True(s.AnyAlive);
        s.ReportAlive(false);
        Assert.False(s.AnyAlive);
    }

    [Fact]
    public void RequestCloseAll_RaisesTheEvent()
    {
        var s = new OwnedWindowsService();
        var raised = 0;
        s.CloseAllRequested += () => raised++;
        s.RequestCloseAll();
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Probe_WithoutAWindowManager_ReportsWhy()
    {
        var s = new OwnedWindowsService();
        var probe = new OwnedWindowsProbe(OwnedWindowProbeKind.Snapshot);

        s.Probe(probe);

        Assert.False(probe.Handled);
        Assert.Equal("no window manager", probe.Error);
    }

    [Fact]
    public void Probe_HandsTheSameRequestToTheHandler()
    {
        var s = new OwnedWindowsService();
        s.ProbeRequested += p => { p.Handled = true; p.Steps = ["tab:x"]; };
        var probe = new OwnedWindowsProbe(OwnedWindowProbeKind.SweepSteps, OwnedWindowIds.Config);

        s.Probe(probe);

        Assert.True(probe.Handled);
        Assert.Null(probe.Error);
        Assert.Equal(["tab:x"], probe.Steps);
    }
}
