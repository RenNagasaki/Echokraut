using System;
using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// Decides which speaker tokens belong to which character (plan game-voice-reference §2.2, §3,
/// F2): <b>the same NPC only</b>. A token is owned by exactly one English full name, or by nobody:
/// <list type="number">
/// <item>An alias entry (<c>GameVoiceAliases.json</c>) names the owner explicitly and wins.</item>
/// <item>Otherwise <see cref="VoiceExtractKey.Resolve"/> (exact full or first name, no prefix
///   match). If the resolved NPC ids carry more than one distinct full name — a first name shared
///   by different characters — the token is ambiguous and owned by nobody, so it can never lend
///   one character's voice to another.</item>
/// </list>
/// Owners are keyed by the normalized English full name.
/// </summary>
public sealed class GameVoiceSpeakerMap
{
    private readonly Dictionary<string, List<string>> _tokensByOwner;
    private readonly Dictionary<string, string> _ownerByToken;
    // Longer full name -> owner whose token is its first name ("estinienwyrmblood" -> "estinien").
    private readonly Dictionary<string, string> _sameNpcNames;

    private GameVoiceSpeakerMap(Dictionary<string, List<string>> tokensByOwner, HashSet<string> disabled, HashSet<string> ambiguous,
        Dictionary<string, string>? sameNpcNames = null)
    {
        _tokensByOwner = tokensByOwner;
        _sameNpcNames = sameNpcNames ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _ownerByToken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (owner, tokens) in tokensByOwner)
            foreach (var t in tokens) _ownerByToken[t] = owner;
        DisabledTokens = disabled;
        AmbiguousTokens = ambiguous;
    }

    public static readonly GameVoiceSpeakerMap Empty = new(new Dictionary<string, List<string>>(), new HashSet<string>(), new HashSet<string>());

    /// <summary>Tokens an alias entry marked <c>Disabled</c> — owned by nobody on purpose.</summary>
    public IReadOnlySet<string> DisabledTokens { get; }

    /// <summary>Tokens whose name match hit several different full names (shared first name) — owned by nobody.</summary>
    public IReadOnlySet<string> AmbiguousTokens { get; }

    /// <summary>Normalized English full name that owns <paramref name="token"/>, or null.</summary>
    public string? OwnerOf(string token) =>
        _ownerByToken.TryGetValue(VoiceExtractKey.Normalize(token ?? ""), out var owner) ? owner : null;

    /// <param name="tokens">Speaker tokens to assign (from <see cref="GameVoiceTextIndex.Tokens"/>).</param>
    /// <param name="englishNpcNames">ENpcResident row id → English singular name.</param>
    /// <param name="aliases">Alias file entries; null = none.</param>
    /// <param name="warnings">Alias entries that could not be applied (unknown NpcId, empty name).</param>
    public static GameVoiceSpeakerMap Build(
        IEnumerable<string> tokens,
        IReadOnlyDictionary<uint, string> englishNpcNames,
        IEnumerable<GameVoiceAliasEntry>? aliases,
        out List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(englishNpcNames);
        warnings = new List<string>();

        var aliasOwner = new Dictionary<string, string>(StringComparer.Ordinal);
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in aliases ?? Enumerable.Empty<GameVoiceAliasEntry>())
        {
            var token = VoiceExtractKey.Normalize(a?.ShortName ?? "");
            if (token.Length == 0) { warnings.Add("Alias entry without shortName skipped."); continue; }
            if (a!.Disabled)
            {
                // Later entry wins: a local "disabled" overrides an embedded mapping, and vice versa.
                disabled.Add(token);
                aliasOwner.Remove(token);
                continue;
            }
            string? name = null;
            if (a!.NpcId is { } id and > 0)
            {
                if (!englishNpcNames.TryGetValue(id, out name) || string.IsNullOrWhiteSpace(name))
                {
                    warnings.Add($"Alias '{a.ShortName}': NpcId {id} has no English name, skipped.");
                    continue;
                }
            }
            else name = a.NpcName;
            var owner = VoiceExtractKey.Normalize(name ?? "");
            if (owner.Length == 0) { warnings.Add($"Alias '{a.ShortName}': no npcName/npcId, skipped."); continue; }
            aliasOwner[token] = owner; // later entry wins (local override after embedded)
            disabled.Remove(token);
        }

        var nameIndex = BuildNameIndex(englishNpcNames);
        var byOwner = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        var sameNpcNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in tokens)
        {
            var token = VoiceExtractKey.Normalize(raw);
            if (token.Length == 0 || disabled.Contains(token)) continue;
            var isAmbiguous = false;
            List<string>? longer = null;
            var owner = aliasOwner.TryGetValue(token, out var o) ? o : ResolveOwner(token, nameIndex, englishNpcNames, out isAmbiguous, out longer);
            if (isAmbiguous) ambiguous.Add(token);
            if (owner != null && longer != null)
                foreach (var name in longer) sameNpcNames.TryAdd(name, owner);
            if (owner == null) continue;
            if (!byOwner.TryGetValue(owner, out var list))
                byOwner[owner] = list = new List<string>();
            if (!list.Contains(token)) list.Add(token);
        }
        return new GameVoiceSpeakerMap(byOwner, disabled, ambiguous, sameNpcNames);
    }

    /// <summary>Tokens owned by any of <paramref name="englishFullNames"/> (raw names, normalized here).</summary>
    public IReadOnlyList<string> TokensFor(IEnumerable<string> englishFullNames)
    {
        var result = new List<string>();
        foreach (var name in englishFullNames ?? Enumerable.Empty<string>())
        {
            var normalized = VoiceExtractKey.Normalize(name ?? "");
            foreach (var owner in new[] { normalized, _sameNpcNames.GetValueOrDefault(normalized) })
            {
                if (owner == null || !_tokensByOwner.TryGetValue(owner, out var tokens)) continue;
                foreach (var t in tokens)
                    if (!result.Contains(t)) result.Add(t);
            }
        }
        return result;
    }

    private static Dictionary<string, List<uint>> BuildNameIndex(IReadOnlyDictionary<uint, string> englishNpcNames)
    {
        // VoiceExtractKey's index takes the harvest's multi-language shape; feed it English only.
        var shaped = new Dictionary<uint, Dictionary<string, string>>();
        foreach (var (id, name) in englishNpcNames)
            shaped[id] = new Dictionary<string, string> { ["en"] = name ?? "" };
        return VoiceExtractKey.BuildNormalizedNameIndex(shaped);
    }

    private static string? ResolveOwner(string token, Dictionary<string, List<uint>> nameIndex,
        IReadOnlyDictionary<uint, string> englishNpcNames, out bool ambiguous, out List<string>? longerNames)
    {
        ambiguous = false;
        longerNames = null;
        var ids = VoiceExtractKey.Resolve(token, nameIndex);
        if (ids == null) return null;
        var fullNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!englishNpcNames.TryGetValue(id, out var name)) continue;
            var full = VoiceExtractKey.Normalize(name ?? "");
            if (full.Length > 0) fullNames.Add(full);
        }
        if (fullNames.Count == 0) return null;
        // The token IS one of the full names ("ESTINIEN" = "Estinien"): that NPC owns it, and the
        // longer names that only add a surname ("Estinien Wyrmblood") are the same NPC written out
        // in full (user call 2026-11-15) — they speak with it too. Live finding: Estinien had no
        // game voice because both names made the token "ambiguous".
        var normalizedToken = VoiceExtractKey.Normalize(token);
        if (fullNames.Contains(normalizedToken))
        {
            longerNames = fullNames.Where(n => n != normalizedToken).ToList();
            return normalizedToken;
        }
        if (fullNames.Count == 1) return fullNames.First();
        ambiguous = true; // shared first name, no exact full name → nobody owns it
        return null;
    }
}
