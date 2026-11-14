using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Parses the TEXT keys of FFXIV's <b>scripted dialog sheets</b> — the <c>custom/*</c>,
/// <c>raid/*</c>, <c>content/*</c>, <c>warp/*</c>, <c>transport/*</c>, <c>opening/*</c>,
/// <c>guild_order/*</c> and <c>leve/*</c> families. Pure — no game access, no state.
///
/// <para>These sheets follow one convention: the key is <c>TEXT_</c> + the sheet's own leaf name
/// (upper-cased) + <c>_</c> + a token that names the speaker, then line numbering. For
/// <c>custom/008/FesHlx2024Transform2_00856</c> a key reads
/// <c>TEXT_FESHLX2024TRANSFORM2_00856_TRANSFORMHLX2024_000_000</c> — the speaker token is
/// <c>TRANSFORMHLX2024</c>. That is the same shape the cutscene harvest already exploits, which is
/// why these families need no Lua bytecode analysis to attribute most of their lines.</para>
///
/// <para><b>Not every token is a speaker.</b> Measured over the live sheets (2026-08-09), 9,358 of
/// the 29,106 <c>custom/*</c> lines carry a token that names no one: <c>SYSTEM</c> narration, menu
/// and button captions, and the player's own choice prompts (<c>Q1</c>) and answers (<c>A1</c>) —
/// the same categories that dominate the cutscene <c>_NONE_VOICE</c> keys. Persisting them would
/// make an NPC read out menu labels.</para>
///
/// <para><b>Uncertain tokens are deliberately NOT classified as non-speech.</b> The list below
/// holds only tokens that are unambiguously interface or system text. Anything else — including
/// odd-looking names like <c>PLATOONINSTRUCTOR</c> or <c>MEMBERC01548</c> — is treated as a
/// possible speaker so it surfaces as an alias candidate for review, rather than being silently
/// dropped. Omitting a line is a judgement call and belongs with the user, not with a keyword
/// list.</para>
/// </summary>
public static partial class ScriptedSheetKey
{
    /// <summary>
    /// Tokens that unambiguously mark non-dialogue: narration, UI captions, flow markers.
    /// Compared after trailing digits are stripped, so <c>ENTER2</c>/<c>LEAVE2</c> match too.
    /// </summary>
    private static readonly HashSet<string> NonSpeechTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Narration / system
        "SYSTEM", "NARRATION", "LOG", "NOTICE", "WARNING", "ERROR", "INFO",
        // Menus and buttons
        "MENU", "TITLE", "SELECT", "START", "END", "CANCEL", "YES", "NO", "OK",
        "ABOUT", "EXPLAIN", "EXPLANATION", "HELP", "CONFIRM", "DESC", "DESCRIPTION",
        "EXIT", "RETURN", "NEXT", "BACK", "SHOP", "BUY", "SELL",
        // Scene / flow markers
        "ENTER", "LEAVE", "LCUT", "CUT", "TALK", "POPUP", "BALLOON",
        // Asset channels
        "SE", "BGM", "UI",
        // Dev leftovers
        "TEST", "TODO", "DUMMY",
    };

    /// <summary>Player choice prompt (<c>Q1</c>) or the player's own answer (<c>A1</c>).</summary>
    [GeneratedRegex(@"^[QA]\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex PlayerChoiceToken();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex TrailingDigits();

    /// <summary>
    /// Extracts the speaker token that follows the sheet's own name in <paramref name="textKey"/>.
    /// Returns false when the key does not belong to this sheet (measured: 24 of 29,106 custom
    /// lines) or carries nothing after the prefix.
    /// </summary>
    public static bool TryGetSpeakerToken(string? sheetLeafName, string? textKey, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrEmpty(sheetLeafName) || string.IsNullOrEmpty(textKey)) return false;

        var prefix = $"TEXT_{sheetLeafName}_";
        if (!textKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = textKey[prefix.Length..];
        if (rest.Length == 0) return false;

        var end = rest.IndexOf('_');
        token = end < 0 ? rest : rest[..end];
        return token.Length > 0;
    }

    /// <summary>
    /// True when the token names no speaker and the line must not be persisted as dialogue.
    /// Purely numeric tokens count too — those are line numbers, i.e. the sheet simply has no
    /// speaker segment in its key.
    /// </summary>
    public static bool IsNonSpeechToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;

        var t = token.Trim();
        if (PlayerChoiceToken().IsMatch(t)) return true;
        if (NonSpeechTokens.Contains(t)) return true;

        // "MAINMENU", "ENTERMENU", "CHALLENGEMENU", "TALKMENU", ... — anything that ends in MENU
        // is a caption, never a name.
        if (t.Length > 4 && t.EndsWith("MENU", StringComparison.OrdinalIgnoreCase)) return true;

        // ENTER2 / ENTER3 / LEAVE2 are numbered variants of the flow markers above. Stripping the
        // digits is safe for real names because FFXIV speaker tokens that end in a number keep a
        // distinctive stem (BOY05426 -> BOY, MEMBERC01548 -> MEMBERC), which is not in the list.
        var stem = TrailingDigits().Replace(t, string.Empty);
        if (stem.Length > 0 && stem.Length != t.Length && NonSpeechTokens.Contains(stem)) return true;

        // A token that is only digits is a line number, not a speaker.
        return stem.Length == 0;
    }
}
