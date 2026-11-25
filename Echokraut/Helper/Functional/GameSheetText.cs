using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Plain text of a game-sheet SeString as the dialogue pipeline stores it: player-name macros become
/// <c>-PlayerName-</c>/<c>-PlayerFirstName-</c>/<c>-PlayerLastName-</c>, gender branches become
/// <c>-IfGender:male|female-</c> (resolved later by <see cref="TalkTextHelper.SubstitutePlaceholders"/>).
/// Shared by the dialog harvest and the sentence-continuation index — both must see the same text.
/// </summary>
public static class GameSheetText
{
    /// <summary>
    /// Known formatting-only macros that don't produce visible text content.
    /// </summary>
    // GameVoiceDataSource uses the same set to tell formatting from content macros.
    public static readonly HashSet<MacroCode> FormattingOnlyMacros = new()
    {
        MacroCode.Color, MacroCode.EdgeColor, MacroCode.ShadowColor,
        MacroCode.ColorType, MacroCode.EdgeColorType,
        MacroCode.Bold, MacroCode.Italic, MacroCode.Edge, MacroCode.Shadow,
        MacroCode.SoftHyphen, MacroCode.Icon, MacroCode.Icon2,
        MacroCode.Scale, MacroCode.Ruby, MacroCode.Sound,
        MacroCode.SetResetTime, MacroCode.SetTime, MacroCode.Wait,
        MacroCode.Key,
    };

    /// <summary>
    /// Regex to detect Split index in a macro ToString() representation. Matches both
    /// <c>Split(PcName(...), …, N)</c> AND <c>Split(string(gstrM), …, N)</c> — the latter
    /// is FFXIV's convention for player-name references in cutscene quest text (slot
    /// <c>gstr1</c> overwhelmingly carries the player's name; we detect any digit suffix).
    /// </summary>
    private static readonly Regex SplitIndexRegex = new(
        @"Split\(.*?(?:PcName|gstr\d+).*?,\s*.+?,\s*(\d+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Regex matching ANY player-name macro reference in a payload's repr. Used both for
    /// the first-pass scan (decides whether to take the slow rebuild path) and the
    /// second-pass append (decides whether to emit a placeholder for unhandled macros).
    /// Covers <c>PcName</c> calls (the standard payload) and <c>gstr&lt;N&gt;</c> global
    /// string references (used by cutscene quest text — see <see cref="SplitIndexRegex"/>).
    /// </summary>
    private static readonly Regex PlayerNameMacroRegex = new(
        @"PcName|gstr\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extract text from a SeString, replacing player-name macros with
    /// -PlayerFirstName-, -PlayerLastName-, or -PlayerName- placeholders.
    /// </summary>
    /// <summary>
    /// Macros that produce conditional/computed text (need special extraction).
    /// </summary>
    private static readonly HashSet<MacroCode> ConditionalTextMacros = new()
    {
        MacroCode.IfPcGender, MacroCode.IfPcName, MacroCode.IfSelf,
        MacroCode.If, MacroCode.Switch,
    };


    public static string ExtractTextWithPlayerName(ReadOnlySeString seString)
    {
        // Quick scan: check if any payload needs special handling
        var needsCustomExtraction = false;
        foreach (var payload in seString)
        {
            if (payload.Type != ReadOnlySePayloadType.Macro) continue;
            if (payload.MacroCode == MacroCode.PcName)
            {
                needsCustomExtraction = true;
                break;
            }
            if (ConditionalTextMacros.Contains(payload.MacroCode))
            {
                needsCustomExtraction = true;
                break;
            }
            if (!FormattingOnlyMacros.Contains(payload.MacroCode)
                && payload.MacroCode != MacroCode.NewLine
                && payload.MacroCode != MacroCode.Hyphen
                && payload.MacroCode != MacroCode.NonBreakingSpace)
            {
                var repr = payload.ToString();
                if (PlayerNameMacroRegex.IsMatch(repr))
                {
                    needsCustomExtraction = true;
                    break;
                }
            }
        }

        // Fast path: no macros that need special handling
        if (!needsCustomExtraction)
        {
            var fastResult = seString.ExtractText();
            return fastResult;
        }

        // Rebuild text, handling player name and conditional macros
        var sb = new StringBuilder();
        foreach (var payload in seString)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                sb.Append(Encoding.UTF8.GetString(payload.Body.Span));
            }
            else if (payload.Type == ReadOnlySePayloadType.Macro)
            {
                if (payload.MacroCode == MacroCode.PcName)
                {
                    sb.Append(TalkTextHelper.PlaceholderFullName);
                }
                else if (payload.MacroCode == MacroCode.NewLine)
                {
                    sb.Append('\n');
                }
                else if (payload.MacroCode == MacroCode.Hyphen)
                {
                    sb.Append('-');
                }
                else if (payload.MacroCode == MacroCode.NonBreakingSpace)
                {
                    sb.Append(' ');
                }
                else if (ConditionalTextMacros.Contains(payload.MacroCode))
                {
                    // Extract first text branch from conditional macros (e.g., IfPcGender → male form)
                    var conditionalText = ExtractConditionalText(payload);
                    if (string.IsNullOrEmpty(conditionalText))
                    {
                        // ExtractConditionalText returned empty because every branch was a nested
                        // expression instead of a literal string — typical when FFXIV wraps the
                        // player address as <If(...)<PcName>...<PcName></If>, uns erwartet ein Kampf...>.
                        // Without this fallback the macro silently disappears and OriginalText starts
                        // with orphan punctuation like ", uns erwartet…" while HasPlayerPlaceholder
                        // stays false (a real DB-side bug we hit in the wild on Gaius's Praetorium
                        // line). When the macro tree references PcName, emit a player placeholder so
                        // (a) the visible text reads correctly, and (b) the live path substitutes
                        // the actual player name at speak-time.
                        var repr = payload.ToString();
                        if (PlayerNameMacroRegex.IsMatch(repr))
                            sb.Append(GetPlayerPlaceholderFromRepr(repr));
                    }
                    else
                    {
                        sb.Append(conditionalText);
                    }
                }
                else if (!FormattingOnlyMacros.Contains(payload.MacroCode))
                {
                    var repr = payload.ToString();
                    if (PlayerNameMacroRegex.IsMatch(repr))
                        sb.Append(GetPlayerPlaceholderFromRepr(repr));
                }
            }
        }
        var result = sb.ToString();
        return result;
    }

    /// <summary>
    /// Extract text from conditional macros like IfPcGender.
    /// For IfPcGender: stores both forms as -IfGender:male|female- placeholder.
    /// For other conditionals: extracts first available text branch.
    /// </summary>
    private static string ExtractConditionalText(ReadOnlySePayload payload)
    {
        // IfPcGender: 3 expressions (condition, male_text, female_text)
        if (payload.MacroCode == MacroCode.IfPcGender
            && payload.TryGetExpression(out _, out var maleExpr, out var femaleExpr))
        {
            var maleText = maleExpr.TryGetString(out var ms) ? ms.ExtractText() : "";
            var femaleText = femaleExpr.TryGetString(out var fs) ? fs.ExtractText() : "";
            if (!string.IsNullOrEmpty(maleText) || !string.IsNullOrEmpty(femaleText))
                return TalkTextHelper.MakeGenderPlaceholder(maleText, femaleText);
        }

        // Other conditionals: try to extract first text branch
        if (payload.TryGetExpression(out _, out var expr2, out var expr3))
        {
            if (expr2.TryGetString(out var text2)) return text2.ExtractText();
            if (expr3.TryGetString(out var text3)) return text3.ExtractText();
        }
        if (payload.TryGetExpression(out _, out var e2))
        {
            if (e2.TryGetString(out var text)) return text.ExtractText();
        }
        if (payload.TryGetExpression(out var e1))
        {
            if (e1.TryGetString(out var text)) return text.ExtractText();
        }

        return "";
    }

    /// <summary>
    /// Determine the correct placeholder from a macro's string representation.
    /// Split(..., " ", 1) → first name, Split(..., " ", 2) → last name, else → full name.
    /// </summary>
    private static string GetPlayerPlaceholderFromRepr(string repr)
    {
        var match = SplitIndexRegex.Match(repr);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
        {
            return index switch
            {
                1 => TalkTextHelper.PlaceholderFirstName,
                2 => TalkTextHelper.PlaceholderLastName,
                _ => TalkTextHelper.PlaceholderFullName,
            };
        }
        return TalkTextHelper.PlaceholderFullName;
    }
}
