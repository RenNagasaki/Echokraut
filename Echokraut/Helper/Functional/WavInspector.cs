using System;
using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Minimal WAV header parser — duration only. Avoids pulling in NAudio just to read a file
/// length. Supports the standard PCM / MS-ADPCM / IEEE-float WAV layouts FFXIV's SCD
/// decoder produces.
/// </summary>
public static class WavInspector
{
    /// <summary>Reads the duration in seconds from a WAV file's header chunks.</summary>
    /// <returns>Duration in seconds, or 0 if the file cannot be parsed.</returns>
    public static double GetDurationSeconds(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return GetDurationSeconds(fs);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Reads duration from an open stream positioned at the start of the WAV. Reads the
    /// fmt chunk for sample rate / channels / bytes-per-sample and the data chunk size.
    /// </summary>
    public static double GetDurationSeconds(Stream stream)
    {
        try
        {
            using var br = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

            // RIFF header: "RIFF" + size + "WAVE"
            if (br.ReadInt32() != 0x46464952) return 0; // "RIFF"
            br.ReadInt32(); // file size
            if (br.ReadInt32() != 0x45564157) return 0; // "WAVE"

            int sampleRate = 0;
            short channels = 0;
            short bitsPerSample = 0;
            int dataSize = 0;
            int? bytesPerSecondHint = null; // some MS-ADPCM streams give us avg bytes/sec directly

            while (stream.Position < stream.Length - 8)
            {
                var chunkId = br.ReadInt32();
                var chunkSize = br.ReadInt32();
                var nextChunk = stream.Position + chunkSize;

                if (chunkId == 0x20746D66) // "fmt "
                {
                    br.ReadInt16();              // audio format
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    var bytesPerSec = br.ReadInt32();
                    br.ReadInt16();              // block align
                    bitsPerSample = br.ReadInt16();
                    if (bytesPerSec > 0) bytesPerSecondHint = bytesPerSec;
                }
                else if (chunkId == 0x61746164) // "data"
                {
                    dataSize = chunkSize;
                    break;
                }

                if (chunkSize < 0) break;
                stream.Seek(nextChunk, SeekOrigin.Begin);
            }

            if (dataSize <= 0) return 0;

            // Prefer bytesPerSec hint when available — handles MS-ADPCM where the simple
            // sampleRate * channels * bytesPerSample formula is wrong.
            if (bytesPerSecondHint.HasValue && bytesPerSecondHint.Value > 0)
                return (double)dataSize / bytesPerSecondHint.Value;

            if (sampleRate <= 0 || channels <= 0 || bitsPerSample <= 0) return 0;
            var bytesPerSample = bitsPerSample / 8.0;
            return dataSize / (sampleRate * channels * bytesPerSample);
        }
        catch
        {
            return 0;
        }
    }
}
