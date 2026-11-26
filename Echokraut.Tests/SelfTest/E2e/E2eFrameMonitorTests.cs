using System.Linq;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eFrameMonitorTests
{
    private long _now;

    private E2eFrameMonitor Monitor() => new(() => _now, ticksPerSecond: 1000);

    private void Frame(E2eFrameMonitor m, long ms)
    {
        _now += ms;
        m.OnFrame();
    }

    [Fact]
    public void Window_CountsOnlyFramesThatEndInsideIt()
    {
        var m = Monitor();
        Frame(m, 16);
        Frame(m, 500); // before Begin: not counted
        var w = m.Begin();
        Frame(m, 16);
        Frame(m, 300);
        Frame(m, 20);
        var stats = w.Stop();
        Frame(m, 2000); // after Stop: not counted

        Assert.Equal(3, stats.Frames);
        Assert.Equal(300, stats.MaxMs);
        Assert.Equal(1, stats.Hitches);
        Assert.Equal(112, stats.MeanMs);
        Assert.Same(stats, w.Stop());
    }

    [Fact]
    public void FirstFrameEver_HasNoDelta()
    {
        var m = Monitor();
        var w = m.Begin();
        Frame(m, 5000);
        Assert.Equal(0, w.Stop().Frames);
    }

    [Fact]
    public void Summarize_P95_And_Fps()
    {
        var ms = new double[100];
        for (var i = 0; i < 100; i++) ms[i] = i < 95 ? 10 : 100;
        var s = E2eFrameMonitor.Summarize(ms);
        Assert.Equal(10, s.P95Ms);
        Assert.Equal(100, s.MaxMs);
        Assert.Equal(14.5, s.MeanMs);
        Assert.Equal(69, s.MeanFps);
        Assert.Equal("no frames", E2eFrameMonitor.Summarize([]).ToString());
    }

    [Fact]
    public void Freezes_AreOpsAboveTheLimit()
    {
        var t = new E2eTelemetry();
        t.Ops.Add(new E2eOpTelemetry("stop", 1, new E2eFrameStats(1, 900, 900, 900, 1)));
        t.Ops.Add(new E2eOpTelemetry("install", 1, new E2eFrameStats(1, 1200, 1200, 1200, 1)));
        Assert.Equal(new[] { "install" }, t.Freezes().Select(o => o.Op));
    }
}
