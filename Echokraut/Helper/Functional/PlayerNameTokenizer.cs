using System.Collections.Generic;

namespace Echokraut.Helper.Functional;

/// <summary>
/// The write-side counterpart of <see cref="TalkTextHelper.SubstitutePlaceholders"/>: turns a
/// concrete player name back into the <c>-PlayerName-</c> / <c>-PlayerFirstName-</c> /
/// <c>-PlayerLastName-</c> tokens the harvest produces.
///
/// **Everything that lands in <c>voice_clips</c> must go through this.** The live runtime path
/// receives the text from the game addon with the player's name already substituted; persisting
/// it verbatim produced a second, player-specific row next to the harvested one — same dialog
/// line, same on-disk WAV (the file hash collapses both spellings to the same token), but a
/// different <c>has_player_placeholder</c> flag and therefore a different
/// <c>player_content_id</c> bucket for its generation row. Tokenising first makes the live
/// encounter match the harvested row exactly.
/// </summary>
public static class PlayerNameTokenizer
{
    /// <summary>
    /// Token → concrete value map for <see cref="TalkTextHelper.ExtractTokens"/>. Entries whose
    /// value would be empty are omitted, so a name without a surname never maps
    /// <c>-PlayerLastName-</c> onto the empty string (which would token-ise every position in
    /// the text).
    /// </summary>
    public static IReadOnlyDictionary<string, string?> BuildTokenMap(string playerName)
    {
        var map = new Dictionary<string, string?>();
        if (string.IsNullOrWhiteSpace(playerName)) return map;

        var parts = playerName.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

        map[TalkTextHelper.PlaceholderFullName] = playerName;
        if (parts.Length > 0) map[TalkTextHelper.PlaceholderFirstName] = parts[0];
        if (parts.Length > 1) map[TalkTextHelper.PlaceholderLastName] = parts[1];

        return map;
    }

    /// <summary>
    /// Replaces every occurrence of the player's full / first / last name with its token.
    /// Longest value first (handled by <see cref="TalkTextHelper.ExtractTokens"/>), so
    /// "Jon Doe" becomes <c>-PlayerName-</c> rather than
    /// <c>-PlayerFirstName- -PlayerLastName-</c>.
    ///
    /// Returns <paramref name="text"/> unchanged when the name is unknown — that is the cold
    /// <see cref="Services.IGameObjectService.LocalPlayerName"/> cache, and writing a
    /// half-tokenised row would be worse than writing none.
    /// </summary>
    public static string Tokenize(string text, string playerName)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(playerName)) return text;
        return TalkTextHelper.ExtractTokens(text, BuildTokenMap(playerName));
    }
}
