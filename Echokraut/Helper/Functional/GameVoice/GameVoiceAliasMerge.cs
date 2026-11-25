using System;
using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// Layering of <c>GameVoiceAliases.json</c> files (plan game-voice-reference §11.5): embedded &lt;
/// remote &lt; local, the later layer wins per token. Also builds the "for the repo" export: the
/// merged result, one entry per token, sorted, in the format of the embedded resource — so the
/// maintainer can replace <c>Resources/GameVoiceAliases.json</c> with it as a whole.
/// </summary>
public static class GameVoiceAliasMerge
{
    /// <summary>One entry per normalized token, the last layer's entry winning; sorted by token.</summary>
    public static List<GameVoiceAliasEntry> Merge(params IEnumerable<GameVoiceAliasEntry>?[] layers)
    {
        var byToken = new Dictionary<string, GameVoiceAliasEntry>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            if (layer == null) continue;
            foreach (var entry in layer)
            {
                var token = VoiceExtractKey.Normalize(entry?.ShortName ?? "");
                if (token.Length == 0) continue;
                byToken[token] = Clean(entry!, token);
            }
        }
        return byToken.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value).ToList();
    }

    /// <summary>Upserts <paramref name="entry"/> into <paramref name="local"/> by token (local file edit).</summary>
    public static List<GameVoiceAliasEntry> Upsert(IEnumerable<GameVoiceAliasEntry>? local, GameVoiceAliasEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Merge(local, new[] { entry });
    }

    /// <summary>Removes the token from <paramref name="local"/> (falls back to remote/embedded again).</summary>
    public static List<GameVoiceAliasEntry> Remove(IEnumerable<GameVoiceAliasEntry>? local, string token)
    {
        var key = VoiceExtractKey.Normalize(token ?? "");
        return Merge(local).Where(e => VoiceExtractKey.Normalize(e.ShortName) != key).ToList();
    }

    private static GameVoiceAliasEntry Clean(GameVoiceAliasEntry e, string token) => new()
    {
        ShortName = token,
        NpcId = e.NpcId is > 0 ? e.NpcId : null,
        NpcName = e.NpcName?.Trim(),
        Comment = e.Comment ?? "",
        Disabled = e.Disabled,
    };
}
