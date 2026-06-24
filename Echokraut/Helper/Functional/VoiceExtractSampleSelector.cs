using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Sample picking + length-fallback policy for the voice starter set extraction.
/// </summary>
public static class VoiceExtractSampleSelector
{
    /// <summary>
    /// Apply the length filter for one NPC's clip set:
    /// <list type="number">
    /// <item>Primary: keep every clip whose duration falls in <c>[minSec, maxSec]</c>.</item>
    /// <item>Fallback: when the primary set is empty, return ONLY the closest-to-window
    /// clip (short clips → longest available; long clips → shortest available; otherwise
    /// the single closest distance to the interval).</item>
    /// </list>
    /// </summary>
    public static List<T> ApplyLengthFilter<T>(IReadOnlyList<T> clips, System.Func<T, double> lengthSec, double minSec, double maxSec)
    {
        var inWindow = new List<T>(clips.Count);
        foreach (var c in clips)
        {
            var len = lengthSec(c);
            if (len >= minSec && len <= maxSec) inWindow.Add(c);
        }
        if (inWindow.Count > 0) return inWindow;

        // Fallback: closest-to-window single pick.
        T? best = default;
        var bestDistance = double.PositiveInfinity;
        var found = false;
        foreach (var c in clips)
        {
            var len = lengthSec(c);
            double dist;
            if (len < minSec) dist = minSec - len;
            else if (len > maxSec) dist = len - maxSec;
            else dist = 0;
            if (dist < bestDistance)
            {
                bestDistance = dist;
                best = c;
                found = true;
            }
        }
        return found && best != null ? new List<T> { best } : new List<T>();
    }

    /// <summary>
    /// Pick up to <paramref name="n"/> samples from a clip set using a deterministic seed.
    /// <list type="bullet">
    /// <item>If <c>clips.Count &lt;= n</c>: returns a copy of the input (no random work).</item>
    /// <item>Otherwise: <c>System.Random(seed)</c>-based reservoir-style selection so the
    /// same seed + same input → same output.</item>
    /// </list>
    /// </summary>
    public static List<T> PickN<T>(IReadOnlyList<T> clips, int n, int seed)
    {
        if (n <= 0 || clips.Count == 0) return new List<T>();
        if (clips.Count <= n) return new List<T>(clips);

        var rng = new System.Random(seed);
        // Fisher-Yates partial shuffle: only the first n picks need to be valid.
        var pool = new List<T>(clips);
        for (var i = 0; i < n; i++)
        {
            var j = i + rng.Next(pool.Count - i);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.GetRange(0, n);
    }

    /// <summary>
    /// Pick up to <paramref name="n"/> samples spread across the clip-duration range, so a
    /// multi-sample voice set captures varied prosody (short, medium, long lines) instead of
    /// a random cluster that might all sit near one length. Deterministic — no RNG: sorts by
    /// duration ascending and takes <paramref name="n"/> evenly-spaced picks with both
    /// endpoints (shortest + longest) included. For <c>n == 1</c> the median-duration clip is
    /// returned (a representative middle, not an extreme).
    /// <list type="bullet">
    /// <item>If <c>clips.Count &lt;= n</c>: returns a copy of the input (length-sorted).</item>
    /// </list>
    /// </summary>
    public static List<T> PickDiverse<T>(IReadOnlyList<T> clips, int n, System.Func<T, double> lengthSec)
    {
        if (n <= 0 || clips.Count == 0) return new List<T>();

        var sorted = clips.OrderBy(lengthSec).ToList();
        if (sorted.Count <= n) return sorted;
        if (n == 1) return new List<T> { sorted[sorted.Count / 2] };

        var result = new List<T>(n);
        for (var i = 0; i < n; i++)
        {
            // Evenly map i ∈ [0, n-1] onto [0, count-1], inclusive of both endpoints.
            var idx = (int)System.Math.Round(i * (sorted.Count - 1) / (double)(n - 1));
            result.Add(sorted[idx]);
        }
        return result;
    }
}
