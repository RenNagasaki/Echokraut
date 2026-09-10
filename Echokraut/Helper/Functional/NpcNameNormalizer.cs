using System.Text.RegularExpressions;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Resolves FFXIV grammatical gender tags in NPC names.
/// <list type="bullet">
/// <item>German: <c>[a]</c> = adjective ending (er/e), <c>[p]</c> = profession suffix
///   (""/in). Pronoun=1 (sie, feminine noun) means <c>[p]</c> is empty even for female
///   NPCs because the stem is already feminine; Pronoun=0 (er, masculine) adds "in".</item>
/// <item>French: <c>[a]</c> = adjective ending (""/"e"), <c>[p]</c> = profession suffix
///   (""/e).</item>
/// <item>Languages without known tags pass through unchanged. Any leftover <c>[x]</c>
///   tags are stripped as a fallback.</item>
/// </list>
/// </summary>
public static class NpcNameNormalizer
{
    private static readonly Regex BracketTagRegex = new(@"\[[a-z]\]", RegexOptions.Compiled);

    /// <summary>
    /// Resolve gender tags + capitalize the first letter so harvest/voice-extractor stems
    /// like "stille Druidin" (after [a]→"e") match the runtime title-cased "Stille Druidin".
    /// </summary>
    /// <param name="name">Raw NPC name from <c>ENpcResident.Singular</c>.</param>
    /// <param name="langCode">Two-letter language code: "de" / "fr" / "en" / "ja".</param>
    /// <param name="isFemale">NPC's grammatical gender — drives [a]/[p] substitution.</param>
    /// <param name="dePronoun">German <c>ENpcResident.Pronoun</c> (0=er, 1=sie). Only consulted
    /// for German feminine NPCs to decide whether [p] adds "in" or stays empty.</param>
    public static string Resolve(string name, string langCode, bool isFemale, int dePronoun = 0)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var n = name;
        if (n.Contains('['))
        {
            switch (langCode)
            {
                case "de":
                    if (isFemale)
                    {
                        n = n.Replace("[a]", "e");
                        n = dePronoun == 1
                            ? n.Replace("[p]", "")
                            : n.Replace("[p]", "in");
                    }
                    else
                    {
                        n = n.Replace("[a]", "er").Replace("[p]", "");
                    }
                    break;
                case "fr":
                    n = isFemale
                        ? n.Replace("[a]", "e").Replace("[p]", "e")
                        : n.Replace("[a]", "").Replace("[p]", "");
                    break;
            }
            n = BracketTagRegex.Replace(n, string.Empty);
        }
        return Capitalize(n);
    }

    /// <summary>
    /// Title-case the first character. German adjective declensions land lowercase after
    /// <c>[a]→"e"</c> substitution (e.g. "stille Druidin"); without this fix-up they would
    /// not match the runtime display name and would create duplicate character rows.
    /// </summary>
    public static string Capitalize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var first = name[0];
        if (!char.IsLetter(first) || char.IsUpper(first)) return name;
        return char.ToUpperInvariant(first) + name.Substring(1);
    }
}
