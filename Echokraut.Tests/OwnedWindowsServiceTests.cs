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
    public void RequestCloseAll_WithoutSubscriber_DoesNothing()
    {
        new OwnedWindowsService().RequestCloseAll();
    }
}
