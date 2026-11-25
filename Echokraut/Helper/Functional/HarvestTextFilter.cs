using System;
using System.Collections.Generic;
using System.Linq;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Decides whether a harvested line is real dialogue or one of Square Enix's own development
/// placeholders. Pure — no game access, no state.
///
/// <para>Why this exists: the localized Excel sheets keep rows for lines that were cut or never
/// translated, and fill them with a Japanese marker instead of removing the row. Measured on game
/// data (2026-08-09), a harvest that does not filter them stores this much junk as NPC dialogue:
/// <c>de</c> 12,673 lines (10,660 of them from <c>quest/*</c>), <c>fr</c> 11,191, <c>en</c> 970.
/// Every one of those would later be read out loud by TTS.</para>
///
/// <para><b>The marker alone is not sufficient evidence.</b> A substring test for <c>未使用</c>
/// ("unused") also matches a real Easter-event line ("また、未使用のを…" — unused *eggs*). Square
/// Enix always decorates the actual placeholders with <c>★</c>, so we require the decoration AND a
/// marker. Measured cost of that extra condition: one line across the whole game. The inverse
/// mistake is just as easy to make — <c>★</c> on its own appears in legitimate stylized dialogue
/// (Gigi in <c>quest/006/ClsGld500_00658</c>: "NE※GIGIGI★※..."), so it must never be a marker by
/// itself.</para>
/// </summary>
public static class HarvestTextFilter
{
    /// <summary>U+2605 BLACK STAR — the decoration Square Enix wraps its placeholders in.</summary>
    private const char PlaceholderDecoration = '★';

    /// <summary>
    /// Japanese placeholder phrases. Only ever consulted together with
    /// <see cref="PlaceholderDecoration"/> — see the class remarks for why.
    /// </summary>
    private static readonly string[] DecoratedMarkers =
    {
        "未使用",    // "unused"
        "削除予定",  // "scheduled for deletion"
        "削除",      // "delete" — covers （★未使用／削除★）-style variants
    };

    /// <summary>
    /// Texts that are a placeholder in their entirety. Exact match (trimmed, case-insensitive) so
    /// short real lines like "-" inside a longer sentence are not affected.
    /// </summary>
    private static readonly HashSet<string> ExactPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "0", "leer", "未使用", "inutilisé", "unused", "dummy", "test",
        "none", "null", "n/a", "-", "---", "...", "placeholder",
        "消して下さい", // "please delete" — story/StoryMain ships one of these undecorated
    };

    /// <summary>
    /// True when <paramref name="text"/> is a development placeholder rather than dialogue.
    /// Null/blank is NOT a placeholder — "no text" is a separate condition the callers already
    /// handle, and conflating the two would hide missing translations.
    /// </summary>
    public static bool IsDeveloperPlaceholder(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();
        if (ExactPlaceholders.Contains(trimmed)) return true;

        // Decoration first: it is the cheap test and the one that keeps real dialogue safe.
        if (trimmed.IndexOf(PlaceholderDecoration) < 0) return false;

        return DecoratedMarkers.Any(m => trimmed.Contains(m, StringComparison.Ordinal));
    }
}
