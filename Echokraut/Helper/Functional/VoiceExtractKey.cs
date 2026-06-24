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
    ///
    /// <para>Indexes BOTH the full normalized name AND the normalized first-name token (the
    /// part before the first space). The first-name key lets a speaker shortname that is just
    /// the character's given name (e.g. <c>"alisaie"</c> for "Alisaie Leveilleur") resolve
    /// precisely via an exact lookup — replacing the old greedy <c>StartsWith</c> prefix match,
    /// which also mapped unrelated names that merely shared a prefix (the speaker token
    /// <c>"ABEL"</c> wrongly resolving to "Abelie").</para>
    /// </summary>
    public static Dictionary<string, List<uint>> BuildNormalizedNameIndex(
        Dictionary<uint, Dictionary<string, string>> npcNames)
    {
        var index = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (npcId, names) in npcNames)
        {
            if (!names.TryGetValue("en", out var enName) || string.IsNullOrEmpty(enName)) continue;

            AddIndexKey(index, Normalize(enName), npcId);

            var firstSpace = enName.IndexOf(' ');
            if (firstSpace > 0)
                AddIndexKey(index, Normalize(enName.Substring(0, firstSpace)), npcId);
        }
        return index;
    }

    private static void AddIndexKey(Dictionary<string, List<uint>> index, string key, uint npcId)
    {
        if (key.Length == 0) return;
        if (!index.TryGetValue(key, out var ids))
            index[key] = ids = new List<uint>();
        if (!ids.Contains(npcId)) ids.Add(npcId);
    }

    /// <summary>
    /// Resolve a speaker shortname against the normalized name index by EXACT match against
    /// either a full name or a first-name token (both are indexed by
    /// <see cref="BuildNormalizedNameIndex"/>). No prefix/substring matching — that previously
    /// caused false positives (speaker token "ABEL" matching "Abelie"). A token that matches
    /// nothing is left for the caller to emit to the unmatched JSON / resolve via the alias map.
    /// On multi-match (e.g. several NPCs sharing a first name), returns the first NpcId; the
    /// caller can log alternatives.
    /// </summary>
    public static List<uint>? Resolve(string shortname, Dictionary<string, List<uint>> index)
    {
        if (string.IsNullOrEmpty(shortname)) return null;
        var norm = Normalize(shortname);
        if (norm.Length == 0) return null;
        return index.TryGetValue(norm, out var ids) && ids.Count > 0 ? ids : null;
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
