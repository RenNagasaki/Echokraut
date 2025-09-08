using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Echokraut.Helper.Functional;

    public static class XttsPostFx
{
    // >>> Haupt-Einstieg: aus deinem RawSourceWaveStream einen veredelten Provider bauen
    public static IWaveProvider BuildPipelineFromWaveProvider(IWaveProvider input, bool resampleTo48k = true)
    {
        // 1) float-Samples
        ISampleProvider current = input.ToSampleProvider();

        // 2) sehr sanfter Tilt-EQ:
        //    - LowShelf +1.0 dB @ 120 Hz (Slope 0.7)
        //    - Peaking -1.0 dB @ 450 Hz (Q 0.9)
        //    - HighShelf +1.5 dB @ 7000 Hz (Slope 0.7)  <-- tiefer als 9 kHz, näher an XTTS/22kNyquist
        current = new EqSampleProvider(current,
                                       lowShelfFreq: 120f, lowShelfSlope: 0.7f, lowShelfGainDb: +1.0f,
                                       midFreq: 450f, midQ: 0.9f, midGainDb: -1.0f,
                                       highShelfFreq: 7000f, highShelfSlope: 0.7f, highShelfGainDb: +1.5f);

        // 3) RMS-Kompressor (musikalischer als Peak, moderat)
        current = new RmsCompressorSampleProvider(current,
            thresholdDb: -20f, ratio: 2.0f, attackMs: 10f, releaseMs: 80f, makeupGainDb: 2f);

        // 4) Safety-Limiter (approx. true-peak, ohne Lookahead: -1 dBFS Target)
        current = new SimpleLimiterSampleProvider(current, ceilingDb: -1.0f, releaseMs: 50f);

        // 5) optional: Resampling – nur wenn wirklich gebraucht
        if (resampleTo48k && current.WaveFormat.SampleRate != 48000)
            current = new WdlResamplingSampleProvider(current, 48000);

        return current.ToWaveProvider();
    }
}
