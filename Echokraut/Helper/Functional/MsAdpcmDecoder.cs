using System;
using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Decode an MS-ADPCM WAV (RIFF/WAVE container with a 4-bit ADPCM data chunk) to 16-bit
/// signed PCM samples. Implements the standard Microsoft ADPCM algorithm with the seven
/// default coefficient pairs — FFXIV's SCD ADPCM entries ship a minimal 16-byte fmt chunk
/// without the extended ADPCMWAVEFORMAT coefficient table, relying on these defaults
/// (which is also what ffmpeg's <c>adpcm_ms</c> codec assumes).
/// </summary>
public static class MsAdpcmDecoder
{
    /// <summary>The 7 standard MS-ADPCM coefficient pairs (Q8 fixed-point).</summary>
    private static readonly int[] Coef1 = { 256, 512, 0, 192, 240, 460, 392 };
    private static readonly int[] Coef2 = { 0, -256, 0, 64, 0, -208, -232 };

    /// <summary>Per-nibble delta scaling table.</summary>
    private static readonly int[] AdaptationTable =
    {
        230, 230, 230, 230, 307, 409, 512, 614,
        768, 614, 512, 409, 307, 230, 230, 230,
    };

    public sealed class DecodedPcm
    {
        public short[] Samples { get; init; } = Array.Empty<short>();
        public int SampleRate { get; init; }
        public int Channels { get; init; }
        /// <summary>Number of frames (one frame = N samples for N channels).</summary>
        public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;
        public double Seconds => SampleRate > 0 ? (double)FrameCount / SampleRate : 0;
    }

    /// <summary>
    /// Decode the given MS-ADPCM RIFF/WAVE byte buffer to interleaved int16 PCM.
    /// Returns null if the buffer can't be parsed as MS-ADPCM.
    /// </summary>
    public static DecodedPcm? Decode(byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length < 44) return null;
        try
        {
            using var ms = new MemoryStream(wavBytes);
            using var br = new BinaryReader(ms);

            if (br.ReadInt32() != 0x46464952) return null; // "RIFF"
            br.ReadInt32();                                 // file size
            if (br.ReadInt32() != 0x45564157) return null; // "WAVE"

            int sampleRate = 0;
            int channels = 0;
            int blockAlign = 0;
            int bitsPerSample = 0;
            int dataOffset = -1;
            int dataSize = 0;

            while (ms.Position < ms.Length - 8)
            {
                var chunkId = br.ReadInt32();
                var chunkSize = br.ReadInt32();
                var nextChunk = ms.Position + chunkSize;

                if (chunkId == 0x20746D66) // "fmt "
                {
                    br.ReadInt16();                       // wFormatTag (often 0x0002 = WAVE_FORMAT_ADPCM, or 0xFFFE for SCD)
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32();                       // nAvgBytesPerSec
                    blockAlign = br.ReadInt16();
                    bitsPerSample = br.ReadInt16();
                    // SCD ships a 16-byte fmt chunk — no cbSize / wSamplesPerBlock / coef table.
                    // Standard ADPCMWAVEFORMAT would have those; we don't need them since we
                    // use the 7 default coef pairs (consistent with ffmpeg's adpcm_ms codec).
                }
                else if (chunkId == 0x61746164) // "data"
                {
                    dataOffset = (int)ms.Position;
                    dataSize = chunkSize;
                    break;
                }

                if (chunkSize < 0 || nextChunk > ms.Length) break;
                ms.Seek(nextChunk, SeekOrigin.Begin);
            }

            if (dataOffset < 0 || dataSize <= 0) return null;
            if (channels <= 0 || channels > 2) return null;
            if (blockAlign <= 0) return null;
            if (bitsPerSample != 4) return null;

            var samples = DecodeBlocks(wavBytes, dataOffset, dataSize, channels, blockAlign);
            return new DecodedPcm
            {
                Samples = samples,
                SampleRate = sampleRate,
                Channels = channels,
            };
        }
        catch
        {
            return null;
        }
    }

    private static short[] DecodeBlocks(byte[] data, int dataOffset, int dataSize, int channels, int blockAlign)
    {
        // Each block holds (blockAlign - 7*channels)*2/channels + 2 frames.
        var samplesPerBlock = (blockAlign - 7 * channels) * 2 / channels + 2;
        var blockCount = dataSize / blockAlign;
        if (blockCount <= 0 || samplesPerBlock <= 0) return Array.Empty<short>();

        var totalFrames = blockCount * samplesPerBlock;
        var output = new short[totalFrames * channels];
        var outIdx = 0;

        // Per-channel decoder state, reused across blocks (reinitialized per block from preamble).
        Span<int> coef1 = stackalloc int[2];
        Span<int> coef2 = stackalloc int[2];
        Span<int> delta = stackalloc int[2];
        Span<int> sample1 = stackalloc int[2];
        Span<int> sample2 = stackalloc int[2];
        Span<int> predictorIdx = stackalloc int[2];

        for (var b = 0; b < blockCount; b++)
        {
            var blockStart = dataOffset + b * blockAlign;
            var p = blockStart;

            // Per-channel: predictor index (1 byte each)
            for (var c = 0; c < channels; c++)
            {
                var idx = data[p++];
                if (idx > 6) idx = 6; // clamp to valid coef-table index
                predictorIdx[c] = idx;
                coef1[c] = Coef1[idx];
                coef2[c] = Coef2[idx];
            }
            // Per-channel: delta (int16)
            for (var c = 0; c < channels; c++)
            {
                delta[c] = ReadInt16Le(data, p);
                p += 2;
            }
            // Per-channel: sample1 (most recent)
            for (var c = 0; c < channels; c++)
            {
                sample1[c] = ReadInt16Le(data, p);
                p += 2;
            }
            // Per-channel: sample2 (older)
            for (var c = 0; c < channels; c++)
            {
                sample2[c] = ReadInt16Le(data, p);
                p += 2;
            }

            // Emit the two preamble samples per channel (in playback order: sample2 first, then sample1).
            for (var c = 0; c < channels; c++) output[outIdx + c] = (short)sample2[c];
            outIdx += channels;
            for (var c = 0; c < channels; c++) output[outIdx + c] = (short)sample1[c];
            outIdx += channels;

            // Remaining frames: each byte carries two 4-bit nibbles. For mono: high then low for the same channel.
            // For stereo: high nibble = left channel, low nibble = right channel of the SAME frame.
            var nibbleBytesPerBlock = blockAlign - 7 * channels;
            var remainingFrames = samplesPerBlock - 2;

            if (channels == 1)
            {
                for (var i = 0; i < nibbleBytesPerBlock && remainingFrames > 0; i++)
                {
                    var bByte = data[p++];
                    var hi = (bByte >> 4) & 0x0F;
                    output[outIdx++] = DecodeNibble(hi, coef1, coef2, delta, sample1, sample2, 0);
                    remainingFrames--;
                    if (remainingFrames <= 0) break;
                    var lo = bByte & 0x0F;
                    output[outIdx++] = DecodeNibble(lo, coef1, coef2, delta, sample1, sample2, 0);
                    remainingFrames--;
                }
            }
            else // stereo
            {
                for (var i = 0; i < nibbleBytesPerBlock && remainingFrames > 0; i++)
                {
                    var bByte = data[p++];
                    var hi = (bByte >> 4) & 0x0F;
                    var lo = bByte & 0x0F;
                    output[outIdx++] = DecodeNibble(hi, coef1, coef2, delta, sample1, sample2, 0);
                    output[outIdx++] = DecodeNibble(lo, coef1, coef2, delta, sample1, sample2, 1);
                    remainingFrames--;
                }
            }
        }

        // Trim any unwritten tail (defensive — shouldn't normally happen).
        if (outIdx < output.Length)
        {
            var trimmed = new short[outIdx];
            Array.Copy(output, trimmed, outIdx);
            return trimmed;
        }
        return output;
    }

    private static short DecodeNibble(int nibble, Span<int> coef1, Span<int> coef2,
        Span<int> delta, Span<int> sample1, Span<int> sample2, int c)
    {
        // Predictor: linear combination of last two samples, scaled down by 256.
        var predictor = (sample1[c] * coef1[c] + sample2[c] * coef2[c]) >> 8;
        // 4-bit two's-complement nibble.
        var signed = (nibble & 0x08) != 0 ? nibble - 16 : nibble;
        var newSample = predictor + signed * delta[c];
        if (newSample > short.MaxValue) newSample = short.MaxValue;
        else if (newSample < short.MinValue) newSample = short.MinValue;

        // Adaptive delta scaling.
        delta[c] = (delta[c] * AdaptationTable[nibble]) >> 8;
        if (delta[c] < 16) delta[c] = 16;

        sample2[c] = sample1[c];
        sample1[c] = newSample;
        return (short)newSample;
    }

    private static int ReadInt16Le(byte[] buf, int offset)
    {
        return (short)(buf[offset] | (buf[offset + 1] << 8));
    }
}
