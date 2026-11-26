using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// Word error rate between the game text of a clip and what Whisper heard in it (plan
/// game-voice-reference §4, ASR row). A high WER flags effects, reverb or overlapping voices.
/// Case, punctuation and diacritics are ignored; Japanese is compared per character (no spaces).
/// </summary>
public static class WordErrorRate
{
    /// <summary>
    /// Levenshtein distance over units divided by the reference length. 0 = identical; may exceed
    /// 1 when the hypothesis is much longer. An empty reference yields 0 for an empty hypothesis
    /// and 1 otherwise.
    /// </summary>
    public static double Compute(string reference, string hypothesis, string languageCode)
    {
        var r = Units(reference, languageCode);
        var h = Units(hypothesis, languageCode);
        if (r.Count == 0) return h.Count == 0 ? 0 : 1;
        return (double)Distance(r, h) / r.Count;
    }

    /// <summary>Normalized comparison units: words, or characters for Japanese.</summary>
    public static List<string> Units(string text, string languageCode)
    {
        var norm = Normalize(text);
        if (IsJapanese(languageCode))
            return norm.Where(c => !char.IsWhiteSpace(c)).Select(c => c.ToString()).ToList();
        return norm.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static bool IsJapanese(string? languageCode) =>
        string.Equals(languageCode, "ja", StringComparison.OrdinalIgnoreCase)
        || string.Equals(languageCode, "jp", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            // Apostrophes inside words ("don't", "Y'shtola") must not split the word.
            if (c is '\'' or '’') continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int Distance(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var prev = new int[b.Count + 1];
        var cur = new int[b.Count + 1];
        for (var j = 0; j <= b.Count; j++) prev[j] = j;
        for (var i = 1; i <= a.Count; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Count; j++)
            {
                var cost = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal) ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Count];
    }
}
