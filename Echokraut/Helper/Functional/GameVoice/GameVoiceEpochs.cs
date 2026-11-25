using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// Voice-actor epoch choice by story progress (plan game-voice-reference §11.4, F4 new).
/// Epoch names come from <c>EchoScd.VoiceActorSplits</c>: <c>""</c> (no split), <c>Pre{b1}</c>
/// (before the first boundary), <c>Post{b}</c> / <c>From{bk}</c> (from boundary token <c>b</c>).
/// </summary>
public static class GameVoiceEpochs
{
    /// <summary>Boundary token an epoch starts at; null for the first epoch (<c>Pre…</c> or <c>""</c>).</summary>
    public static string? StartToken(string epoch)
    {
        if (string.IsNullOrEmpty(epoch) || epoch.StartsWith("Pre", StringComparison.Ordinal)) return null;
        if (epoch.StartsWith("Post", StringComparison.Ordinal)) return epoch.Substring(4);
        if (epoch.StartsWith("From", StringComparison.Ordinal)) return epoch.Substring(4);
        return null;
    }

    /// <summary>Epochs in story order: the first epoch, then by start token.</summary>
    public static List<string> Ordered(IEnumerable<string> epochs) =>
        epochs.Distinct(StringComparer.Ordinal)
            .OrderBy(e => StartToken(e) ?? "", StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The epoch to speak with. <paramref name="reached"/> answers "has the player reached this
    /// boundary token?" — null delegate = progress unknown → newest epoch (the old behaviour).
    /// Otherwise the latest epoch whose start is reached; none reached → the first epoch.
    /// </summary>
    public static string Choose(IEnumerable<string> epochs, Func<string, bool>? reached)
    {
        var ordered = Ordered(epochs);
        if (ordered.Count == 0) return string.Empty;
        if (reached == null) return ordered[^1];
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var start = StartToken(ordered[i]);
            if (start == null || reached(start)) return ordered[i];
        }
        return ordered[0];
    }
}
