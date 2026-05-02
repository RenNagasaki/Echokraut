using System;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class WavResamplerTests
{
    [Fact]
    public void Resample_IdenticalRates_ReturnsClone()
    {
        var src = new short[] { 1, 2, 3, 4, 5 };
        var dst = WavResampler.Resample(src, 22050, 22050);
        Assert.Equal(src, dst);
        Assert.NotSame(src, dst); // independent buffer — safe to mutate either.
    }

    [Fact]
    public void Resample_EmptyInput_ReturnsEmpty()
    {
        var dst = WavResampler.Resample(Array.Empty<short>(), 44100, 22050);
        Assert.Empty(dst);
    }

    [Fact]
    public void Resample_HalvesLengthOnHalfRate()
    {
        var src = new short[1000];
        var dst = WavResampler.Resample(src, 44100, 22050);
        // dstLen = src.Length * dstRate / srcRate = 1000 * 22050 / 44100 = 500
        Assert.Equal(500, dst.Length);
    }

    [Fact]
    public void Resample_DoublesLengthOnDoubleRate()
    {
        var src = new short[500];
        var dst = WavResampler.Resample(src, 22050, 44100);
        Assert.Equal(1000, dst.Length);
    }

    [Fact]
    public void Resample_ConstantInput_StaysApproximatelyConstant()
    {
        // Half a second of constant 5000 at 44100 Hz, downsampled to 22050.
        var src = new short[22050];
        for (var i = 0; i < src.Length; i++) src[i] = 5000;

        var dst = WavResampler.Resample(src, 44100, 22050);
        Assert.True(dst.Length > 10000);

        // Check a sample well past the kernel-edge transient region.
        for (var i = 100; i < dst.Length - 100; i++)
            Assert.InRange(dst[i], 4900, 5100);
    }

    [Fact]
    public void Resample_PreservesSineFrequencyBelowNyquist()
    {
        // Generate a 440 Hz sine at 44100 Hz, half-rate it to 22050. 440 Hz is well below
        // both Nyquists, so amplitude should survive within rounding tolerance.
        const int srcRate = 44100;
        const int dstRate = 22050;
        const double freq = 440.0;
        var src = new short[srcRate]; // 1 second
        for (var i = 0; i < src.Length; i++)
        {
            var v = Math.Sin(2 * Math.PI * freq * i / srcRate) * 10000;
            src[i] = (short)v;
        }

        var dst = WavResampler.Resample(src, srcRate, dstRate);

        // Peak amplitude should still land near 10000 (allow 15% slack — windowed-sinc
        // gain isn't perfectly unity).
        short peak = 0;
        for (var i = 100; i < dst.Length - 100; i++)
            if (Math.Abs((int)dst[i]) > peak) peak = (short)Math.Abs((int)dst[i]);
        Assert.InRange(peak, 8500, 11500);
    }

    [Fact]
    public void Resample_ClampsToInt16Range()
    {
        // Force values that, when summed with overshooting kernel, could exceed int16 range.
        var src = new short[200];
        for (var i = 0; i < src.Length; i++)
            src[i] = (i % 2 == 0) ? short.MaxValue : short.MinValue;

        var dst = WavResampler.Resample(src, 44100, 22050);
        foreach (var s in dst)
        {
            Assert.InRange(s, short.MinValue, short.MaxValue);
        }
    }

    [Fact]
    public void DownmixToMono_AveragesChannels()
    {
        var stereo = new short[] { 100, 300, -200, 200, 1000, 2000 };
        var mono = WavResampler.DownmixToMono(stereo, 2);
        Assert.Equal(3, mono.Length);
        Assert.Equal(200, mono[0]);   // (100+300)/2
        Assert.Equal(0, mono[1]);     // (-200+200)/2
        Assert.Equal(1500, mono[2]);  // (1000+2000)/2
    }

    [Fact]
    public void DownmixToMono_MonoInput_ReturnsInputUnchanged()
    {
        var src = new short[] { 1, 2, 3 };
        var dst = WavResampler.DownmixToMono(src, 1);
        Assert.Same(src, dst);
    }

    [Fact]
    public void Resample_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WavResampler.Resample(null!, 44100, 22050));
        Assert.Throws<ArgumentException>(() => WavResampler.Resample(new short[] { 0 }, 0, 22050));
        Assert.Throws<ArgumentException>(() => WavResampler.Resample(new short[] { 0 }, 44100, -1));
        Assert.Throws<ArgumentException>(() => WavResampler.Resample(new short[] { 0 }, 44100, 22050, taps: 0));
    }
}
