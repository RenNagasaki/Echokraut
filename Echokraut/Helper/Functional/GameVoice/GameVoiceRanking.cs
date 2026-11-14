using System;
using EchoScd;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// Scores candidate reference clips (plan game-voice-reference §4, Teil C): hard limits only for
/// the obvious, everything else feeds a score in [0, 1]; the best 1–3 clips win as long as they
/// clear <see cref="MinScore"/>, below that the voice pack speaks. All thresholds are start values
/// to be tuned against real clips (phase 8). Changing any of them, or a filter, means bumping
/// <see cref="IndexVersion"/> so stored indexes get re-ranked.
/// </summary>
public static class GameVoiceRanking
{
    /// <summary>Version of ranking + filters; stored per index row (§2.6).</summary>
    public const int IndexVersion = 1;

    public const double MinSeconds = 3.0;
    public const double MaxSeconds = 15.0;
    public const double OptimumMinSeconds = 6.0;
    public const double OptimumMaxSeconds = 10.0;

    /// <summary>Final score below which a clip is never used.</summary>
    public const double MinScore = 0.35;

    /// <summary>ASR is expensive: only this many best pre-scored clips per NPC are sent to <c>/asr-check</c>.</summary>
    public const int AsrTopN = 5;

    /// <summary>At most this many clips are stored per NPC (§2.1: top 1–3).</summary>
    public const int MaxReferences = 3;

    /// <summary>
    /// Upper bound of clips decoded per ranking run. A main character has hundreds of lines; the
    /// newest ones (highest audio base, i.e. latest patch — F4 prefers the current actor anyway)
    /// are taken first.
    /// </summary>
    public const int MaxDecodedCandidates = 40;

    // Weights of the pre-score; sum = 1.
    private const double DurationWeight = 0.35;
    private const double TempoWeight = 0.25;
    private const double SignalWeight = 0.40;

    // Share of the ASR term in the final score of the top-N clips.
    private const double AsrWeight = 0.40;

    /// <summary>Hard filter on length (§4, "Dauer": hard outside 3–15 s).</summary>
    public static bool IsAcceptableDuration(double seconds) => seconds >= MinSeconds && seconds <= MaxSeconds;

    /// <summary>1 inside the 6–10 s optimum, falling linearly to 0 at the hard limits.</summary>
    public static double DurationScore(double seconds)
    {
        if (!IsAcceptableDuration(seconds)) return 0;
        if (seconds < OptimumMinSeconds) return (seconds - MinSeconds) / (OptimumMinSeconds - MinSeconds);
        if (seconds > OptimumMaxSeconds) return (MaxSeconds - seconds) / (MaxSeconds - OptimumMaxSeconds);
        return 1;
    }

    /// <summary>
    /// Typical speaking rate per language: words per second, characters per second for Japanese.
    /// Start values from common speech-rate figures; the plan asks for the "language median".
    /// </summary>
    public static double MedianRate(string languageCode) => languageCode?.ToUpperInvariant() switch
    {
        "DE" => 2.3,
        "FR" => 2.8,
        "JA" or "JP" => 7.5,
        _ => 2.6,
    };

    /// <summary>
    /// 1 at the language median, decaying with the log ratio: half the score at ~1.3x or ~0.77x,
    /// a clearly drawn-out or rushed line (screaming, effects, long pauses) scores low.
    /// </summary>
    public static double TempoScore(string text, double seconds, string languageCode)
    {
        if (seconds <= 0) return 0;
        var units = WordErrorRate.Units(text, languageCode).Count;
        if (units == 0) return 0;
        var ratio = units / seconds / MedianRate(languageCode);
        return Math.Exp(-Math.Abs(Math.Log(ratio)) / 0.38);
    }

    /// <summary>
    /// Signal quality: starts at 1, the old WavEffectGate violations cost 0.25 each; clipping,
    /// silence and loudness far from a normal dialogue level are scored on top (they had no
    /// thresholds in the gate).
    /// </summary>
    public static double SignalScore(SignalMetrics metrics, SignalGateResult gate)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(gate);
        var score = 1.0 - 0.25 * gate.Violations.Count;
        // Clipping: anything above 0.1 % of samples at full scale is audible distortion.
        score -= Math.Min(0.4, metrics.ClippingRatio * 100);
        // Silence: a reference that is a third pause teaches the model pauses.
        if (metrics.SilenceRatio > 0.3) score -= Math.Min(0.3, (metrics.SilenceRatio - 0.3));
        // Loudness: dialogue sits around -20 dBFS RMS; whispers and shouts are far from it.
        var loudnessOff = Math.Abs(metrics.RmsDbfs - (-20.0));
        if (loudnessOff > 8) score -= Math.Min(0.3, (loudnessOff - 8) * 0.03);
        return Clamp01(score);
    }

    /// <summary>Weighted pre-score (no ASR) of one decoded clip.</summary>
    public static double PreScore(string text, double seconds, string languageCode, SignalMetrics metrics, SignalGateResult gate) =>
        DurationWeight * DurationScore(seconds)
        + TempoWeight * TempoScore(text, seconds, languageCode)
        + SignalWeight * SignalScore(metrics, gate);

    /// <summary>
    /// Final score. Without an ASR result (endpoint missing or failed) the pre-score stands alone;
    /// with one, WER 0 → full ASR term, WER ≥ 1 → none.
    /// </summary>
    public static double FinalScore(double preScore, double? wordErrorRate)
    {
        if (wordErrorRate is not { } wer) return preScore;
        return (1 - AsrWeight) * preScore + AsrWeight * Clamp01(1 - wer);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
