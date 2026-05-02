using System;
using System.Collections.Generic;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Stateless parsing + name-mapping helpers for voice-sample extraction.
/// </summary>
public static class VoiceExtractKey
{
    /// <summary>
    /// Parses a TextKey of the form
    /// <c>TEXT_&lt;PREFIX&gt;_&lt;CUTSCENE&gt;_&lt;LINE&gt;_&lt;CHARACTER&gt;</c> — five
    /// underscore-separated tokens total (e.g. <c>TEXT_VOICEMAN_06006_000010_YSHTOLA</c> or
    /// <c>TEXT_MANFST_..._..._..._CHARACTER</c>). Mirrors Tools' <c>audioFileSplit.Length == 5</c>
    /// gate. Keys with any other shape (system markers like
    /// <c>TEXT_VOICEMAN_..._SYSTEM_NONE_VOICE</c>, older 6-segment quest-name keys whose final
    /// token is a line number not a speaker, etc.) are intentionally rejected — Tools skips
    /// them too.
    /// </summary>
    public static bool TryParse(string textKey, out string speakerShortName, out string audioFileBase)
    {
        speakerShortName = string.Empty;
        audioFileBase = string.Empty;
        if (string.IsNullOrEmpty(textKey)) return false;

        var parts = textKey.Split('_');
        if (parts.Length != 5) return false;
        if (!parts[0].Equals("TEXT", StringComparison.OrdinalIgnoreCase)) return false;

        speakerShortName = parts[4].ToLowerInvariant();
        // audioFileBase = same key with TEXT→vo and minus the speaker-name suffix, lowercased.
        // Matches Tools: audioFile.Substring(0, audioFile.Length - character.Length - 1)
        //                .Replace("TEXT", "vo").ToLower()
        var withoutSpeaker = textKey.Substring(0, textKey.Length - parts[4].Length - 1);
        audioFileBase = withoutSpeaker.Replace("TEXT", "vo").ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Build a normalized name index: lowercase, strip spaces / apostrophes / hyphens.
    /// Mirrors the convention used elsewhere in <c>DialogHarvestService</c>.
    /// </summary>
    public static Dictionary<string, List<uint>> BuildNormalizedNameIndex(
        Dictionary<uint, Dictionary<string, string>> npcNames)
    {
        var index = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (npcId, names) in npcNames)
        {
            if (!names.TryGetValue("en", out var enName) || string.IsNullOrEmpty(enName)) continue;
            var norm = Normalize(enName);
            if (norm.Length == 0) continue;
            if (!index.TryGetValue(norm, out var ids))
                index[norm] = ids = new List<uint>();
            if (!ids.Contains(npcId)) ids.Add(npcId);
        }
        return index;
    }

    /// <summary>
    /// Resolve a speaker shortname against the normalized name index.
    /// <list type="number">
    /// <item>Direct match — shortname == normalized name.</item>
    /// <item>Substring — normalized name <c>StartsWith(shortname)</c> (e.g. "alisaie"
    /// matches "alisaieleveilleur").</item>
    /// <item>No match — caller emits to unmatched JSON.</item>
    /// </list>
    /// On multi-match (e.g. several "Aymeric" spawns), returns the first NpcId; the caller
    /// can log alternatives.
    /// </summary>
    public static List<uint>? Resolve(string shortname, Dictionary<string, List<uint>> index)
    {
        if (string.IsNullOrEmpty(shortname)) return null;
        var norm = Normalize(shortname);
        if (index.TryGetValue(norm, out var direct) && direct.Count > 0)
            return direct;

        // Substring fallback: any indexed name whose normalized form starts with shortname.
        // Cap to avoid pathological matches for very short names ("a" matching everything).
        if (norm.Length < 4) return null;

        List<uint>? collected = null;
        foreach (var (key, ids) in index)
        {
            if (key.StartsWith(norm, StringComparison.OrdinalIgnoreCase))
            {
                collected ??= new List<uint>();
                foreach (var id in ids)
                    if (!collected.Contains(id)) collected.Add(id);
            }
        }
        return collected;
    }

    /// <summary>Lowercase, strip ASCII spaces / apostrophes / hyphens. Pure transform.</summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var span = s.AsSpan();
        Span<char> buf = stackalloc char[span.Length];
        var i = 0;
        foreach (var ch in span)
        {
            if (ch == ' ' || ch == '\'' || ch == '-') continue;
            buf[i++] = char.ToLowerInvariant(ch);
        }
        return new string(buf[..i]);
    }
}
