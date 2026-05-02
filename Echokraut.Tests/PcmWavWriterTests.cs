using System;
using System.IO;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class PcmWavWriterTests
{
    [Fact]
    public void Build_HasCorrectMagicAndStructure()
    {
        var samples = new short[] { 0, 100, -100, 200 };
        var bytes = PcmWavWriter.Build(samples, 22050, 1);

        // RIFF/WAVE/fmt /data magic at expected offsets.
        Assert.Equal((byte)'R', bytes[0]);
        Assert.Equal((byte)'I', bytes[1]);
        Assert.Equal((byte)'F', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
        Assert.Equal((byte)'W', bytes[8]);
        Assert.Equal((byte)'A', bytes[9]);
        Assert.Equal((byte)'V', bytes[10]);
        Assert.Equal((byte)'E', bytes[11]);
        Assert.Equal((byte)'f', bytes[12]);
        Assert.Equal((byte)'m', bytes[13]);
        Assert.Equal((byte)'t', bytes[14]);
        Assert.Equal((byte)' ', bytes[15]);
        Assert.Equal((byte)'d', bytes[36]);
        Assert.Equal((byte)'a', bytes[37]);
        Assert.Equal((byte)'t', bytes[38]);
        Assert.Equal((byte)'a', bytes[39]);

        // 44 bytes header + 4 samples * 2 bytes = 52 bytes total.
        Assert.Equal(52, bytes.Length);
    }

    [Fact]
    public void Build_WavInspectorRoundTrip_DurationMatches()
    {
        // 22050 mono samples at 22050 Hz = exactly 1 second.
        var samples = new short[22050];
        for (var i = 0; i < samples.Length; i++) samples[i] = (short)(i % 100);

        var bytes = PcmWavWriter.Build(samples, 22050, 1);
        using var ms = new MemoryStream(bytes);
        var seconds = WavInspector.GetDurationSeconds(ms);
        Assert.Equal(1.0, seconds, 3);
    }

    [Fact]
    public void Build_StereoLayoutDoublesByteCount()
    {
        // 100 interleaved samples for 2-channel stereo = 50 frames × 2 bytes × 2 channels = 200 data bytes.
        var samples = new short[100];
        var bytes = PcmWavWriter.Build(samples, 44100, 2);
        Assert.Equal(244, bytes.Length); // 44 header + 200 data
    }

    [Fact]
    public void Build_PreservesSampleValuesLittleEndian()
    {
        var samples = new short[] { 0x1234, unchecked((short)0xABCD) };
        var bytes = PcmWavWriter.Build(samples, 44100, 1);
        // First sample at offset 44 = 0x34, 0x12
        Assert.Equal(0x34, bytes[44]);
        Assert.Equal(0x12, bytes[45]);
        // Second sample at offset 46 = 0xCD, 0xAB
        Assert.Equal(0xCD, bytes[46]);
        Assert.Equal(0xAB, bytes[47]);
    }

    [Fact]
    public void Build_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => PcmWavWriter.Build(null!, 44100, 1));
        Assert.Throws<ArgumentException>(() => PcmWavWriter.Build(Array.Empty<short>(), 0, 1));
        Assert.Throws<ArgumentException>(() => PcmWavWriter.Build(Array.Empty<short>(), 44100, 0));
    }
}
