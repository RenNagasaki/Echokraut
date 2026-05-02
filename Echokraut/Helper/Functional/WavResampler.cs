using System;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Sample-rate conversion for 16-bit signed mono PCM via a windowed-sinc kernel
/// (Hann-windowed sinc, default 16 taps each side). Anti-aliased on downsampling by
/// scaling the kernel cutoff to the destination Nyquist. Sufficient for AllTalk
/// voice-cloning input — the network re-extracts spectrograms internally.
/// </summary>
public static class WavResampler
{
    /// <summary>
    /// Resample <paramref name="src"/> from <paramref name="srcRate"/> to <paramref name="dstRate"/>.
    /// Returns a new short[] sized roughly <c>src.Length * dstRate / srcRate</c>.
    /// Returns the input unchanged when rates match.
    /// </summary>
    public static short[] Resample(short[] src, int srcRate, int dstRate, int taps = 16)
    {
        if (src == null) throw new ArgumentNullException(nameof(src));
        if (srcRate <= 0 || dstRate <= 0) throw new ArgumentException("Sample rate must be positive.");
        if (taps < 1) throw new ArgumentException("Tap count must be >= 1.", nameof(taps));
        if (src.Length == 0) return Array.Empty<short>();
        if (srcRate == dstRate) return (short[])src.Clone();

        var dstLen = (int)((long)src.Length * dstRate / srcRate);
        if (dstLen <= 0) return Array.Empty<short>();
        var dst = new short[dstLen];

        // For downsampling we lower the kernel cutoff to the dst Nyquist (relative to srcRate)
        // so we anti-alias before decimation. For upsampling we keep the source Nyquist.
        var ratio = (double)dstRate / srcRate;
        var cutoff = Math.Min(0.5, ratio * 0.5);
        var twoPiCutoff = 2.0 * Math.PI * cutoff;
        var piOverTaps = Math.PI / taps;

        for (var i = 0; i < dstLen; i++)
        {
            var srcPos = (double)i * srcRate / dstRate;
            var center = (int)Math.Floor(srcPos);
            var frac = srcPos - center;

            double sum = 0;
            double normSum = 0;
            for (var j = -taps + 1; j <= taps; j++)
            {
                var idx = center + j;
                if (idx < 0 || idx >= src.Length) continue;
                var x = j - frac;
                double sinc;
                if (Math.Abs(x) < 1e-9)
                    sinc = 2.0 * cutoff;
                else
                    sinc = Math.Sin(twoPiCutoff * x) / (Math.PI * x);
                // Hann window across [-taps, taps].
                var w = 0.5 * (1.0 + Math.Cos(piOverTaps * x));
                if (Math.Abs(x) > taps) w = 0;
                var k = sinc * w;
                sum += src[idx] * k;
                normSum += k;
            }

            var v = normSum > 1e-9 ? sum / normSum : sum;
            var iv = (int)Math.Round(v);
            if (iv > short.MaxValue) iv = short.MaxValue;
            else if (iv < short.MinValue) iv = short.MinValue;
            dst[i] = (short)iv;
        }

        return dst;
    }

    /// <summary>
    /// Downmix interleaved multi-channel int16 PCM to mono by averaging channels.
    /// No-op when already mono.
    /// </summary>
    public static short[] DownmixToMono(short[] interleaved, int channels)
    {
        if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
        if (channels <= 0) throw new ArgumentException("Channels must be positive.", nameof(channels));
        if (channels == 1) return interleaved;
        var frames = interleaved.Length / channels;
        var mono = new short[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0;
            for (var c = 0; c < channels; c++) sum += interleaved[f * channels + c];
            mono[f] = (short)(sum / channels);
        }
        return mono;
    }
}
