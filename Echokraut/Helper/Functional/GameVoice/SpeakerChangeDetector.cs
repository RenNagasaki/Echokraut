using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>Speaker vector of one clip of one token (from <c>POST /speaker-embedding</c>).</summary>
public sealed record SpeakerSample(string PatchToken, float[] Vector);

/// <summary>A suggested voice-actor boundary: the new voice starts at <see cref="BoundaryToken"/>.</summary>
/// <param name="Gap">Mean similarity within both sides minus across them (patch centroids).</param>
public sealed record SpeakerChangeSuggestion(string BoundaryToken, double Gap, int PatchesBefore, int PatchesAfter);

/// <summary>
/// Finds voice-actor changes in the clips of one speaker token (plan game-voice-reference §11.3).
/// <para><b>Method, measured 2026-11-15 on 978 German clips of 11 speakers:</b> vectors are centred
/// on the mean of ALL clips of the run (raw x-vectors all sit near 0.98 cosine), averaged per patch,
/// and a boundary between patches is scored by <c>gap = (within A + within B) / 2 − across</c> over
/// the patch centroids, the own patch left out of "within". Leaving it out matters: clips of one
/// recording session resemble each other, so the newest patch alone always looked "different".</para>
/// <para>Results (XTTS encoder): Y'shtola 6.1 gap 0.65, Hydaelyn 6.0 0.36, the worst false
/// candidate 0.28 (Alphinaud, last patch only). Qwen's x-vector did NOT separate (Y'shtola 0.41 vs
/// false 0.39) — hence a threshold per encoder and only suggestions, never applied automatically:
/// the hand-checked <c>GameVoiceActorSplits.json</c> is what the live path uses.</para>
/// </summary>
public static class SpeakerChangeDetector
{
    /// <summary>Patches each side of a boundary needs (one patch is one recording session).</summary>
    public const int MinPatchesPerSide = 2;

    /// <summary>Gap a calibrated encoder must reach. Unknown encoders get <see cref="UncalibratedThreshold"/>.</summary>
    internal static readonly IReadOnlyDictionary<string, double> Thresholds = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["xtts-v2-speaker"] = 0.32,
    };

    /// <summary>High on purpose: an uncalibrated encoder may only report the very obvious.</summary>
    public const double UncalibratedThreshold = 0.6;

    public static double ThresholdFor(string? encoder) =>
        encoder != null && Thresholds.TryGetValue(encoder, out var t) ? t : UncalibratedThreshold;

    public static bool IsCalibrated(string? encoder) => encoder != null && Thresholds.ContainsKey(encoder);

    /// <summary>Element-wise mean of L2-normalised vectors (the centring point of a run).</summary>
    public static float[] Mean(IEnumerable<float[]> vectors)
    {
        float[]? sum = null;
        var n = 0;
        foreach (var v in vectors)
        {
            var u = Normalize(v);
            sum ??= new float[u.Length];
            if (u.Length != sum.Length) continue;
            for (var i = 0; i < u.Length; i++) sum[i] += u[i];
            n++;
        }
        if (sum == null || n == 0) return Array.Empty<float>();
        for (var i = 0; i < sum.Length; i++) sum[i] /= n;
        return sum;
    }

    /// <summary>
    /// Best boundary of one token, recursing once into both sides (a voice can change twice).
    /// Empty when fewer than 2 × <see cref="MinPatchesPerSide"/> patches or no gap reaches
    /// <paramref name="threshold"/>. Boundaries are returned in ascending token order.
    /// </summary>
    public static List<SpeakerChangeSuggestion> Detect(IEnumerable<SpeakerSample> samples, float[] runMean, double threshold, int depth = 2)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(runMean);
        var centroids = samples
            .GroupBy(s => s.PatchToken, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Patch: g.Key, Vector: Centroid(g.Select(s => Center(s.Vector, runMean)))))
            .Where(c => c.Vector.Length > 0)
            .ToList();
        var result = new List<SpeakerChangeSuggestion>();
        Split(centroids, threshold, depth, result);
        return result.OrderBy(r => r.BoundaryToken, StringComparer.Ordinal).ToList();
    }

    private static void Split(List<(string Patch, float[] Vector)> c, double threshold, int depth, List<SpeakerChangeSuggestion> into)
    {
        if (depth <= 0 || c.Count < 2 * MinPatchesPerSide) return;
        var n = c.Count;
        var sim = new double[n, n];
        for (var a = 0; a < n; a++)
            for (var b = 0; b < n; b++)
                sim[a, b] = Dot(c[a].Vector, c[b].Vector);

        var bestGap = double.NegativeInfinity;
        var bestAt = -1;
        for (var i = MinPatchesPerSide; i <= n - MinPatchesPerSide; i++)
        {
            var gap = (MeanWithin(sim, 0, i) + MeanWithin(sim, i, n)) / 2 - MeanAcross(sim, i, n);
            if (gap > bestGap) { bestGap = gap; bestAt = i; }
        }
        if (bestAt < 0 || bestGap < threshold) return;
        into.Add(new SpeakerChangeSuggestion(c[bestAt].Patch, Math.Round(bestGap, 3), bestAt, n - bestAt));
        Split(c.GetRange(0, bestAt), threshold, depth - 1, into);
        Split(c.GetRange(bestAt, n - bestAt), threshold, depth - 1, into);
    }

    private static double MeanWithin(double[,] sim, int from, int to)
    {
        double sum = 0; var k = 0;
        for (var a = from; a < to; a++)
            for (var b = from; b < to; b++)
                if (a != b) { sum += sim[a, b]; k++; }
        return k == 0 ? 0 : sum / k;
    }

    private static double MeanAcross(double[,] sim, int split, int n)
    {
        double sum = 0; var k = 0;
        for (var a = 0; a < split; a++)
            for (var b = split; b < n; b++) { sum += sim[a, b]; k++; }
        return k == 0 ? 0 : sum / k;
    }

    private static float[] Center(float[] v, float[] mean)
    {
        var u = Normalize(v);
        if (mean.Length != u.Length) return u;
        var r = new float[u.Length];
        for (var i = 0; i < u.Length; i++) r[i] = u[i] - mean[i];
        return Normalize(r);
    }

    private static float[] Centroid(IEnumerable<float[]> vectors)
    {
        float[]? sum = null;
        foreach (var v in vectors)
        {
            sum ??= new float[v.Length];
            if (v.Length != sum.Length) continue;
            for (var i = 0; i < v.Length; i++) sum[i] += v[i];
        }
        return sum == null ? Array.Empty<float>() : Normalize(sum);
    }

    private static float[] Normalize(float[] v)
    {
        double s = 0;
        foreach (var x in v) s += x * x;
        var norm = Math.Sqrt(s);
        if (norm == 0 || double.IsNaN(norm)) return v.ToArray();
        var r = new float[v.Length];
        for (var i = 0; i < v.Length; i++) r[i] = (float)(v[i] / norm);
        return r;
    }

    private static double Dot(float[] a, float[] b)
    {
        double s = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) s += a[i] * b[i];
        return s;
    }
}
