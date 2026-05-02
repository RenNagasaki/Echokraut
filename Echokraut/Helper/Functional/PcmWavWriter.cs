using System;
using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Build a PCM (uncompressed) 16-bit WAV byte buffer from int16 samples. Standard
/// RIFF/WAVE/<c>fmt </c>/<c>data</c> chunk layout — what AllTalk's voice-cloning
/// pipeline expects as input.
/// </summary>
public static class PcmWavWriter
{
    public static byte[] Build(short[] samples, int sampleRate, int channels)
    {
        if (samples == null) throw new ArgumentNullException(nameof(samples));
        if (sampleRate <= 0) throw new ArgumentException("Sample rate must be positive.", nameof(sampleRate));
        if (channels <= 0) throw new ArgumentException("Channels must be positive.", nameof(channels));

        const int bitsPerSample = 16;
        var bytesPerSample = bitsPerSample / 8;
        var blockAlign = channels * bytesPerSample;
        var byteRate = sampleRate * blockAlign;
        var dataSize = samples.Length * bytesPerSample;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);
        bw.Write(0x46464952);                  // "RIFF"
        bw.Write(36 + dataSize);               // file size minus 8
        bw.Write(0x45564157);                  // "WAVE"
        bw.Write(0x20746D66);                  // "fmt "
        bw.Write(16);                          // PCM fmt chunk size
        bw.Write((short)1);                    // wFormatTag = WAVE_FORMAT_PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write(0x61746164);                  // "data"
        bw.Write(dataSize);
        for (var i = 0; i < samples.Length; i++) bw.Write(samples[i]);
        return ms.ToArray();
    }
}
