using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Validated, queryable view over <see cref="VoiceActorSplitsFile"/>. Pure (no DI) — the
/// service loads the JSON (embedded + optional local override) and hands it here.
///
/// <para>Maps a <c>(voiceKey, language)</c> pair to its actor-change boundary patches, and
/// resolves a single clip's <c>audioFileBase</c> to the epoch name it belongs to. Voices
/// without a configured split resolve to the empty epoch (no filename suffix, current
/// behavior).</para>
/// </summary>
public sealed class VoiceActorSplits
{
    // key = "<voiceKey-lower>|<LANG-upper>" → strictly-ascending 5-digit boundary tokens.
    private readonly Dictionary<string, string[]> _byKey;

    private VoiceActorSplits(Dictionary<string, string[]> byKey) => _byKey = byKey;

    /// <summary>A splits view with no entries — every voice collapses to the empty epoch.</summary>
    public static readonly VoiceActorSplits Empty = new(new Dictionary<string, string[]>());

    /// <summary>True when at least one valid split entry is configured.</summary>
    public bool HasAnySplits => _byKey.Count > 0;

    /// <summary>
    /// Parse raw JSON into a validated splits view. Invalid entries (empty key/language,
    /// empty/non-5-digit/non-ascending boundary lists) are dropped and reported via
    /// <paramref name="warnings"/>; the rest are kept. Unparseable JSON yields
    /// <see cref="Empty"/> with a single warning.
    /// </summary>
    public static VoiceActorSplits Parse(string? json, out List<string> warnings)
    {
        warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
            return Empty;
        VoiceActorSplitsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<VoiceActorSplitsFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            warnings.Add($"VoiceActorSplits JSON could not be parsed: {ex.Message}");
            return Empty;
        }
        return Build(file, out warnings);
    }

    /// <summary>Build + validate from an already-deserialized file.</summary>
    public static VoiceActorSplits Build(VoiceActorSplitsFile? file, out List<string> warnings)
    {
        warnings = new List<string>();
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (file?.Splits == null) return Empty;

        foreach (var entry in file.Splits)
        {
            if (string.IsNullOrWhiteSpace(entry.VoiceKey) || string.IsNullOrWhiteSpace(entry.Language))
            {
                warnings.Add($"Split entry skipped: missing voiceKey or language ({entry.VoiceKey}/{entry.Language}).");
                continue;
            }
            if (entry.BoundaryPatches == null || entry.BoundaryPatches.Count == 0)
            {
                warnings.Add($"Split entry '{entry.VoiceKey}' skipped: no boundaryPatches.");
                continue;
            }
            if (!ValidateBoundaries(entry.BoundaryPatches, out var reason))
            {
                warnings.Add($"Split entry '{entry.VoiceKey}' skipped: {reason}");
                continue;
            }
            var key = MakeKey(entry.VoiceKey, entry.Language);
            if (map.ContainsKey(key))
                warnings.Add($"Split entry '{entry.VoiceKey}/{entry.Language}' overrides an earlier duplicate.");
            map[key] = entry.BoundaryPatches.ToArray();
        }
        return new VoiceActorSplits(map);
    }

    /// <summary>True when a split is configured for this voice key + language.</summary>
    public bool HasSplit(string voiceKey, string languageCode) =>
        _byKey.ContainsKey(MakeKey(voiceKey, languageCode));

    /// <summary>
    /// The epoch name a clip belongs to. Empty string when no split applies to this
    /// voice+language (the default, single-epoch case → no filename suffix). When a split
    /// applies but the clip's <c>audioFileBase</c> has no usable patch token, the clip is
    /// routed to the earliest epoch (treated as oldest content) — never silently dropped.
    /// </summary>
    public string ResolveEpoch(string voiceKey, string languageCode, string audioFileBase)
    {
        if (!_byKey.TryGetValue(MakeKey(voiceKey, languageCode), out var boundaries))
            return string.Empty;
        var token = TryExtractPatchToken(audioFileBase, out var t) ? t : "00000";
        return EpochName(token, boundaries);
    }

    // ── pure helpers (internal for testing) ──────────────────────────────────

    /// <summary>
    /// Auto-generate the epoch name for a patch token given strictly-ascending boundaries.
    /// One boundary → <c>Pre{b}</c> / <c>Post{b}</c>. N&gt;1 boundaries → <c>Pre{b1}</c> for the
    /// oldest epoch and <c>From{bk}</c> for every epoch at-or-after a boundary. Each name
    /// declares its inclusive lower bound so the filename is self-documenting.
    /// </summary>
    internal static string EpochName(string token, string[] boundaries)
    {
        if (boundaries.Length == 0) return string.Empty;
        if (string.CompareOrdinal(token, boundaries[0]) < 0)
            return "Pre" + boundaries[0];

        var idx = 0;
        for (var i = 0; i < boundaries.Length; i++)
            if (string.CompareOrdinal(token, boundaries[i]) >= 0) idx = i;

        return boundaries.Length == 1 ? "Post" + boundaries[0] : "From" + boundaries[idx];
    }

    /// <summary>Extract the 5-digit patch token (third underscore segment) from an audio file
    /// base such as <c>vo_voiceman_06006_000010</c> → <c>"06006"</c>. False if the shape is
    /// unexpected (fewer than 3 segments, or the token isn't exactly 5 digits).</summary>
    internal static bool TryExtractPatchToken(string audioFileBase, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrEmpty(audioFileBase)) return false;
        var parts = audioFileBase.Split('_');
        if (parts.Length < 3) return false;
        var t = parts[2];
        if (t.Length != 5 || !t.All(char.IsDigit)) return false;
        token = t;
        return true;
    }

    private static bool ValidateBoundaries(IReadOnlyList<string> boundaries, out string reason)
    {
        reason = string.Empty;
        string? prev = null;
        foreach (var b in boundaries)
        {
            if (b == null || b.Length != 5 || !b.All(char.IsDigit))
            {
                reason = $"boundary '{b}' is not a 5-digit token";
                return false;
            }
            if (prev != null && string.CompareOrdinal(b, prev) <= 0)
            {
                reason = $"boundaries not strictly ascending ('{prev}' then '{b}')";
                return false;
            }
            prev = b;
        }
        return true;
    }

    private static string MakeKey(string voiceKey, string languageCode) =>
        voiceKey.Trim().ToLowerInvariant() + "|" + languageCode.Trim().ToUpperInvariant();
}
