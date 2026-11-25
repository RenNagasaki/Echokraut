using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>A candidate NPC for an unmatched speaker token, best first.</summary>
public sealed record GameVoiceMatchCandidate(string NpcName, double Score);

/// <summary>
/// Suggestions for speaker tokens no NPC owns (plan game-voice-reference §11.5 report). Name
/// heuristics only — a suggestion is for a human to confirm, it is never applied by itself.
/// <list type="bullet">
/// <item>Containment: the token contains a name part of ≥ 4 letters (<c>NEWSPHENE</c> → Sphene),
/// or a name contains the token.</item>
/// <item>Otherwise Levenshtein similarity of token vs. full name and vs. each name part
/// (<c>VRITRA</c> → Vrtra).</item>
/// </list>
/// </summary>
public static class GameVoiceMatchSuggester
{
    public const double MinScore = 0.6;

    public static List<GameVoiceMatchCandidate> Suggest(string token, IEnumerable<string> npcNames, int max = 5)
    {
        var t = VoiceExtractKey.Normalize(token ?? "");
        if (t.Length < 3) return new List<GameVoiceMatchCandidate>();
        var best = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in npcNames ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var score = ScoreOf(t, name);
            if (score < MinScore) continue;
            if (!best.TryGetValue(name, out var old) || score > old) best[name] = score;
        }
        return best.Select(pair => new GameVoiceMatchCandidate(pair.Key, Math.Round(pair.Value, 3)))
            .OrderByDescending(c => c.Score).ThenBy(c => c.NpcName, StringComparer.OrdinalIgnoreCase)
            .Take(max).ToList();
    }

    internal static double ScoreOf(string normalizedToken, string npcName)
    {
        var full = VoiceExtractKey.Normalize(npcName);
        if (full.Length == 0) return 0;
        if (full == normalizedToken) return 1.0;
        var parts = npcName.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(VoiceExtractKey.Normalize).Where(p => p.Length > 0).ToList();
        double score = 0;
        if (normalizedToken.Length >= 4 && full.Contains(normalizedToken, StringComparison.Ordinal)) score = 0.9;
        foreach (var p in parts)
        {
            if (p.Length >= 4 && normalizedToken.Contains(p, StringComparison.Ordinal))
                score = Math.Max(score, 0.75 + 0.2 * p.Length / normalizedToken.Length);
            score = Math.Max(score, Similarity(normalizedToken, p));
        }
        return Math.Min(1.0, Math.Max(score, Similarity(normalizedToken, full)));
    }

    /// <summary>1 − Levenshtein / longer length.</summary>
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var tmp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = tmp;
            }
        }
        return 1.0 - (double)d[b.Length] / Math.Max(a.Length, b.Length);
    }
}
