using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;

namespace Echokraut.Helper.Functional;

/// Sanfter 2x Shelf + Mid-Peak
public sealed class EqSampleProvider : ISampleProvider
{
    private readonly ISampleProvider src;
    private readonly int ch, sr;
    private readonly BiQuadFilter[,] f; // [channel, 3]

    public EqSampleProvider(ISampleProvider source,
                            float lowShelfFreq,  float lowShelfSlope,  float lowShelfGainDb,
                            float midFreq,       float midQ,           float midGainDb,
                            float highShelfFreq, float highShelfSlope, float highShelfGainDb)
    {
        src = source; ch = src.WaveFormat.Channels; sr = src.WaveFormat.SampleRate;
        f = new BiQuadFilter[ch, 3];
        for (int c = 0; c < ch; c++)
        {
            f[c,0] = BiQuadFilter.LowShelf (sr, lowShelfFreq,  lowShelfSlope,  lowShelfGainDb);
            f[c,1] = BiQuadFilter.PeakingEQ(sr, midFreq,       midQ,           midGainDb);
            f[c,2] = BiQuadFilter.HighShelf(sr, highShelfFreq, highShelfSlope, highShelfGainDb);
        }
    }

    public WaveFormat WaveFormat => src.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = src.Read(buffer, offset, count);
        for (int n = 0; n < read; n += ch)
            for (int c = 0; c < ch; c++)
            {
                int i = offset + n + c;
                float s = buffer[i];
                s = f[c,0].Transform(s);
                s = f[c,1].Transform(s);
                s = f[c,2].Transform(s);
                buffer[i] = s;
            }
        return read;
    }
}

/// RMS-basierter Kompressor (musikalischer, weniger hart)
public sealed class RmsCompressorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider src;
    private readonly int ch; private readonly float sr;
    private readonly float thresholdDb, ratio, attackCoeff, releaseCoeff, makeup;
    private readonly float[] env; // pro Kanal

    public RmsCompressorSampleProvider(ISampleProvider source,
        float thresholdDb = -20f, float ratio = 2.0f,
        float attackMs = 10f, float releaseMs = 80f,
        float makeupGainDb = 2f)
    {
        src = source; ch = src.WaveFormat.Channels; sr = src.WaveFormat.SampleRate;
        this.thresholdDb = thresholdDb; this.ratio = ratio;
        attackCoeff = (float)Math.Exp(-1.0 / (sr * (attackMs/1000f)));
        releaseCoeff = (float)Math.Exp(-1.0 / (sr * (releaseMs/1000f)));
        makeup = DbToLin(makeupGainDb);
        env = new float[ch];
    }

    public WaveFormat WaveFormat => src.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = src.Read(buffer, offset, count);

        // RMS-Detektor (einfach: gleitender quadratischer Mittelwert)
        for (int n = 0; n < read; n += ch)
        {
            for (int c = 0; c < ch; c++)
            {
                int i = offset + n + c;
                float x = buffer[i];

                float x2 = x * x;
                float e = env[c];
                e = (x2 > e) ? (attackCoeff * e + (1 - attackCoeff) * x2)
                             : (releaseCoeff * e + (1 - releaseCoeff) * x2);
                env[c] = e;

                float envDb = 10f * (float)Math.Log10(Math.Max(e, 1e-12));
                float gainDb = 0f;
                if (envDb > thresholdDb)
                {
                    float over = envDb - thresholdDb;
                    gainDb = -(over - over / ratio);
                }
                float g = DbToLin(gainDb) * makeup;
                buffer[i] = x * g;
            }
        }
        return read;
    }

    private static float DbToLin(float db) => (float)Math.Pow(10.0, db / 20.0);
}

/// Simpler Safety-Limiter (kein Lookahead) – hält Peaks < ceiling
public sealed class SimpleLimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider src;
    private readonly int ch;
    private readonly float ceilingLin;
    private readonly float releaseCoeff;
    private readonly float[] gain; // pro Kanal

    public SimpleLimiterSampleProvider(ISampleProvider source, float ceilingDb = -1.0f, float releaseMs = 50f)
    {
        src = source; ch = src.WaveFormat.Channels;
        ceilingLin = (float)Math.Pow(10.0, ceilingDb / 20.0);
        float sr = src.WaveFormat.SampleRate;
        releaseCoeff = (float)Math.Exp(-1.0 / (sr * (releaseMs/1000f)));
        gain = new float[ch];
        for (int c = 0; c < ch; c++) gain[c] = 1.0f;
    }

    public WaveFormat WaveFormat => src.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = src.Read(buffer, offset, count);
        for (int n = 0; n < read; n += ch)
        {
            for (int c = 0; c < ch; c++)
            {
                int i = offset + n + c;
                float x = buffer[i];
                float a = Math.Abs(x);
                float desired = (a > ceilingLin && a > 1e-9f) ? (ceilingLin / a) : 1.0f;

                // nur nach oben langsam freigeben
                gain[c] = (desired < gain[c]) ? desired : (releaseCoeff * gain[c] + (1 - releaseCoeff) * desired);

                buffer[i] = x * gain[c];
            }
        }
        return read;
    }
}
