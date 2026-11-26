using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eGpuProbeTests
{
    [Theory]
    [InlineData("5123, 12288, 37\r\n", 5123, 12288, 37)]
    [InlineData("\n  800,8192,0\n900, 8192, 1\n", 800, 8192, 0)]
    public void Parse_TakesTheFirstGpu(string output, long used, long total, int util)
    {
        Assert.Equal(new E2eGpuSample(used, total, util), NvidiaSmiGpuProbe.Parse(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[N/A], 8192, 3")]
    [InlineData("100, 200")]
    public void Parse_Garbage_IsNull(string output) => Assert.Null(NvidiaSmiGpuProbe.Parse(output));

    [Fact]
    public void Stats_PeakAndMean()
    {
        var s = E2eGpuStats.Of(new List<E2eGpuSample> { new(1000, 8000, 10), new(3000, 8000, 90) })!;
        Assert.Equal(3000, s.VramPeakMiB);
        Assert.Equal(50, s.GpuUtilMean);
        Assert.Equal(90, s.GpuUtilMax);
        Assert.Null(E2eGpuStats.Of([]));
    }

    private sealed class CountingProbe : IE2eGpuProbe
    {
        public int Calls;

        public Task<E2eGpuSample?> SampleAsync(CancellationToken ct)
        {
            var n = Interlocked.Increment(ref Calls);
            return Task.FromResult<E2eGpuSample?>(new(100 * n, 8000, 50));
        }
    }

    [Fact]
    public async Task Sampler_CollectsUntilStopped()
    {
        var probe = new CountingProbe();
        var sampler = E2eGpuSampler.Start(probe, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        while (Volatile.Read(ref probe.Calls) < 3) await Task.Delay(1);
        var stats = await sampler.StopAsync();
        Assert.NotNull(stats);
        Assert.True(stats!.Samples >= 3);
    }
}
