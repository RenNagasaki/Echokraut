using System.Collections.Generic;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>
/// The per-NPC game-voice override of one user (plan §11.5, <c>Configuration.GameVoiceNpcOverrides</c>)
/// as one text field: empty = automatic (the central mapping decides), <c>-</c> = no game voice,
/// anything else = that speaker token. Pure, for the NPC edit window.
/// </summary>
public static class GameVoiceNpcOverride
{
    public const string NoneMarker = "-";

    /// <summary>What the text field shows for <paramref name="key"/>.</summary>
    public static string Display(IReadOnlyDictionary<string, string> overrides, string key) =>
        !overrides.TryGetValue(key, out var value) ? "" : value.Length == 0 ? NoneMarker : value.ToUpperInvariant();

    /// <summary>Applies the text field; returns true when the stored override changed.</summary>
    public static bool Apply(IDictionary<string, string> overrides, string key, string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return overrides.Remove(key);
        var value = text == NoneMarker ? "" : VoiceExtractKey.Normalize(text);
        if (text != NoneMarker && value.Length == 0) return overrides.Remove(key);
        if (overrides.TryGetValue(key, out var old) && old == value) return false;
        overrides[key] = value;
        return true;
    }
}
