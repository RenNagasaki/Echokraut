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

    private GameVoiceSpeakerMap(Dictionary<string, List<string>> tokensByOwner) => _tokensByOwner = tokensByOwner;

    public static readonly GameVoiceSpeakerMap Empty = new(new Dictionary<string, List<string>>());

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
        foreach (var a in aliases ?? Enumerable.Empty<GameVoiceAliasEntry>())
        {
            var token = VoiceExtractKey.Normalize(a?.ShortName ?? "");
            if (token.Length == 0) { warnings.Add("Alias entry without shortName skipped."); continue; }
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
        }

        var nameIndex = BuildNameIndex(englishNpcNames);
        var byOwner = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var raw in tokens)
        {
            var token = VoiceExtractKey.Normalize(raw);
            if (token.Length == 0) continue;
            var owner = aliasOwner.TryGetValue(token, out var o) ? o : ResolveOwner(token, nameIndex, englishNpcNames);
            if (owner == null) continue;
            if (!byOwner.TryGetValue(owner, out var list))
                byOwner[owner] = list = new List<string>();
            if (!list.Contains(token)) list.Add(token);
        }
        return new GameVoiceSpeakerMap(byOwner);
    }

    /// <summary>Tokens owned by any of <paramref name="englishFullNames"/> (raw names, normalized here).</summary>
    public IReadOnlyList<string> TokensFor(IEnumerable<string> englishFullNames)
    {
        var result = new List<string>();
        foreach (var name in englishFullNames ?? Enumerable.Empty<string>())
        {
            if (!_tokensByOwner.TryGetValue(VoiceExtractKey.Normalize(name ?? ""), out var tokens)) continue;
            foreach (var t in tokens)
                if (!result.Contains(t)) result.Add(t);
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
        IReadOnlyDictionary<uint, string> englishNpcNames)
    {
        var ids = VoiceExtractKey.Resolve(token, nameIndex);
        if (ids == null) return null;
        string? owner = null;
        foreach (var id in ids)
        {
            if (!englishNpcNames.TryGetValue(id, out var name)) continue;
            var full = VoiceExtractKey.Normalize(name ?? "");
            if (full.Length == 0) continue;
            if (owner == null) owner = full;
            else if (owner != full) return null; // shared first name → ambiguous, nobody owns it
        }
        return owner;
    }
}
