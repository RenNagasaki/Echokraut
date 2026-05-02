using System;
using System.IO;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Tests for the MS-ADPCM decoder. We build synthetic RIFF/WAVE buffers byte-by-byte so the
/// decoder is exercised without any game files.
/// </summary>
public class MsAdpcmDecoderTests
{
    /// <summary>Build a minimal MS-ADPCM RIFF/WAVE buffer with a 16-byte fmt chunk
    /// (matching SCD's ADPCM layout).</summary>
    private static byte[] BuildAdpcmWav(int channels, int sampleRate, int blockAlign, byte[] data)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(0x46464952); // RIFF
        bw.Write(36 + data.Length);
        bw.Write(0x45564157); // WAVE
        bw.Write(0x20746D66); // "fmt "
        bw.Write(16);
        bw.Write((short)0x0002); // WAVE_FORMAT_ADPCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * blockAlign);
        bw.Write((short)blockAlign);
        bw.Write((short)4); // bitsPerSample
        bw.Write(0x61746164); // "data"
        bw.Write(data.Length);
        bw.Write(data);
        return ms.ToArray();
    }

    /// <summary>Build a single mono ADPCM block (7 preamble + nibble bytes).</summary>
    private static byte[] BuildMonoBlock(byte predictor, short delta, short sample1, short sample2, byte[] nibbles)
    {
        var block = new byte[7 + nibbles.Length];
        block[0] = predictor;
        block[1] = (byte)(delta & 0xFF);
        block[2] = (byte)((delta >> 8) & 0xFF);
        block[3] = (byte)(sample1 & 0xFF);
        block[4] = (byte)((sample1 >> 8) & 0xFF);
        block[5] = (byte)(sample2 & 0xFF);
        block[6] = (byte)((sample2 >> 8) & 0xFF);
        Array.Copy(nibbles, 0, block, 7, nibbles.Length);
        return block;
    }

    [Fact]
    public void Decode_RejectsBufferTooSmall()
    {
        Assert.Null(MsAdpcmDecoder.Decode(new byte[10]));
        Assert.Null(MsAdpcmDecoder.Decode(Array.Empty<byte>()));
        Assert.Null(MsAdpcmDecoder.Decode(null!));
    }

    [Fact]
    public void Decode_ParsesHeader()
    {
        // 4 nibble bytes = 8 frames + 2 preamble = 10 frames.
        var block = BuildMonoBlock(predictor: 0, delta: 16, sample1: 0, sample2: 0,
            nibbles: new byte[] { 0, 0, 0, 0 });
        var wav = BuildAdpcmWav(channels: 1, sampleRate: 44100, blockAlign: 11, data: block);

        var pcm = MsAdpcmDecoder.Decode(wav);
        Assert.NotNull(pcm);
        Assert.Equal(1, pcm!.Channels);
        Assert.Equal(44100, pcm.SampleRate);
        Assert.Equal(10, pcm.Samples.Length);
    }

    [Fact]
    public void Decode_AllZeroNibbles_PreservesInitialSamplesAndProducesZeroes()
    {
        // All-zero nibble bytes with predictor index 0 (coef1=256, coef2=0):
        //   predictor_value = (sample1 * 256) >> 8 = sample1
        //   new = sample1 + 0 * delta = sample1
        // So once sample1 stabilises, every subsequent decoded sample equals it.
        var block = BuildMonoBlock(predictor: 0, delta: 100, sample1: 1234, sample2: -500,
            nibbles: new byte[] { 0, 0, 0, 0 });
        var wav = BuildAdpcmWav(1, 22050, blockAlign: 11, data: block);

        var pcm = MsAdpcmDecoder.Decode(wav);
        Assert.NotNull(pcm);
        Assert.Equal(10, pcm!.Samples.Length);
        // Preamble emits sample2 first, then sample1.
        Assert.Equal(-500, pcm.Samples[0]);
        Assert.Equal(1234, pcm.Samples[1]);
        // Every subsequent decoded nibble stays at sample1 because the predictor mirrors it.
        for (var i = 2; i < pcm.Samples.Length; i++)
            Assert.Equal(1234, pcm.Samples[i]);
    }

    [Fact]
    public void Decode_OutputCountScalesWithBlockCount()
    {
        // Three identical zero blocks at blockAlign=11 → 30 frames.
        var oneBlock = BuildMonoBlock(0, 16, 0, 0, new byte[] { 0, 0, 0, 0 });
        var data = new byte[oneBlock.Length * 3];
        Array.Copy(oneBlock, 0, data, 0, oneBlock.Length);
        Array.Copy(oneBlock, 0, data, oneBlock.Length, oneBlock.Length);
        Array.Copy(oneBlock, 0, data, oneBlock.Length * 2, oneBlock.Length);

        var wav = BuildAdpcmWav(1, 44100, 11, data);
        var pcm = MsAdpcmDecoder.Decode(wav);
        Assert.NotNull(pcm);
        Assert.Equal(30, pcm!.Samples.Length);
    }

    [Fact]
    public void Decode_Stereo_ProducesInterleavedSamples()
    {
        // Stereo block layout: predictor[L,R] (2) + delta[L,R] (4) + sample1[L,R] (4)
        // + sample2[L,R] (4) = 14 preamble bytes. Each nibble byte = 1 stereo frame.
        // blockAlign = 14 + 2 nibble bytes = 16 bytes → samplesPerBlock = 4 frames = 8 interleaved.
        var block = new byte[16];
        // predictors L=0 R=0
        block[0] = 0; block[1] = 0;
        // delta L=16 R=16
        block[2] = 16; block[3] = 0; block[4] = 16; block[5] = 0;
        // sample1 L=100 R=200
        block[6] = 100; block[7] = 0; block[8] = (byte)200; block[9] = 0;
        // sample2 L=10 R=20
        block[10] = 10; block[11] = 0; block[12] = 20; block[13] = 0;
        // nibbles
        block[14] = 0x00; block[15] = 0x00;

        var wav = BuildAdpcmWav(2, 44100, 16, block);
        var pcm = MsAdpcmDecoder.Decode(wav);
        Assert.NotNull(pcm);
        Assert.Equal(2, pcm!.Channels);
        // 4 frames × 2 channels = 8 interleaved samples.
        Assert.Equal(8, pcm.Samples.Length);
        // Frame 0 = sample2[L], sample2[R]
        Assert.Equal(10, pcm.Samples[0]);
        Assert.Equal(20, pcm.Samples[1]);
        // Frame 1 = sample1[L], sample1[R]
        Assert.Equal(100, pcm.Samples[2]);
        Assert.Equal(200, pcm.Samples[3]);
    }

    [Fact]
    public void Decode_RejectsNonAdpcmBitDepth()
    {
        // bitsPerSample=16 should cause the decoder to bail (it's a PCM-shaped header,
        // not ADPCM).
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(0x46464952); bw.Write(40); bw.Write(0x45564157);
        bw.Write(0x20746D66); bw.Write(16);
        bw.Write((short)1); bw.Write((short)1); bw.Write(44100);
        bw.Write(44100 * 2); bw.Write((short)2); bw.Write((short)16); // 16-bit PCM
        bw.Write(0x61746164); bw.Write(4);
        bw.Write(new byte[] { 0, 0, 0, 0 });

        Assert.Null(MsAdpcmDecoder.Decode(ms.ToArray()));
    }

    [Fact]
    public void DecodedPcm_SecondsMatchesSampleCountAndRate()
    {
        var block = BuildMonoBlock(0, 16, 0, 0, new byte[] { 0, 0, 0, 0 });
        var wav = BuildAdpcmWav(1, 1000, 11, block); // 1000 Hz, 10 frames → 0.01s
        var pcm = MsAdpcmDecoder.Decode(wav);
        Assert.NotNull(pcm);
        Assert.Equal(10, pcm!.FrameCount);
        Assert.Equal(0.01, pcm.Seconds, 4);
    }
}
