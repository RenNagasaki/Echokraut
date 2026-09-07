using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class NpcNameNormalizerTests
{
    // ── German [a] adjective tags ───────────────────────────────────────────

    [Theory]
    [InlineData("garleisch[a] Centurio", "Garleischer Centurio")]    // male → "er", capitalized
    [InlineData("stille Druidin", "Stille Druidin")]                 // no tag, just capitalize
    public void Resolve_German_Male_AdjectiveTag(string input, string expected)
    {
        Assert.Equal(expected, NpcNameNormalizer.Resolve(input, "de", isFemale: false));
    }

    [Theory]
    [InlineData("garleisch[a] Centurio", "Garleische Centurio")]     // female → "e"
    [InlineData("still[a] Druidin", "Stille Druidin")]               // stem "still" + "e" = "Stille"
    public void Resolve_German_Female_AdjectiveTag(string input, string expected)
    {
        Assert.Equal(expected, NpcNameNormalizer.Resolve(input, "de", isFemale: true));
    }

    // ── German [p] profession tags ──────────────────────────────────────────

    [Fact]
    public void Resolve_German_Male_DropsProfessionTag()
    {
        // "Diener[p] der Fortemps" — male NPC → [p] empty.
        var result = NpcNameNormalizer.Resolve("Diener[p] der Fortemps", "de", isFemale: false);
        Assert.Equal("Diener der Fortemps", result);
    }

    [Fact]
    public void Resolve_German_Female_FeminineNoun_DropsProfessionTag()
    {
        // "Soldatin[p] der Befreiungsarmee" — female NPC, Pronoun=1 (sie/feminine noun)
        // means the stem is already feminine → [p] resolves to empty, NOT "in".
        var result = NpcNameNormalizer.Resolve(
            "Soldatin[p] der Befreiungsarmee", "de", isFemale: true, dePronoun: 1);
        Assert.Equal("Soldatin der Befreiungsarmee", result);
    }

    [Fact]
    public void Resolve_German_Female_MasculineNoun_AddsInSuffix()
    {
        // "Diener[p] der Fortemps" — female NPC, Pronoun=0 (er/masculine noun) → [p] adds "in".
        var result = NpcNameNormalizer.Resolve(
            "Diener[p] der Fortemps", "de", isFemale: true, dePronoun: 0);
        Assert.Equal("Dienerin der Fortemps", result);
    }

    // ── French [a]/[p] tags ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_French_Male_StripsAllTags()
    {
        var result = NpcNameNormalizer.Resolve("garde[a] des Fortemps[p]", "fr", isFemale: false);
        Assert.Equal("Garde des Fortemps", result);
    }

    [Fact]
    public void Resolve_French_Female_AddsE()
    {
        var result = NpcNameNormalizer.Resolve("garde[a] des Fortemps[p]", "fr", isFemale: true);
        Assert.Equal("Gardee des Fortempse", result);
    }

    // ── Pass-through for other languages / no-tag cases ─────────────────────

    [Theory]
    [InlineData("Y'shtola Rhul", "en", "Y'shtola Rhul")]
    [InlineData("ヤ・シュトラ", "ja", "ヤ・シュトラ")]
    [InlineData("Tataru Taru", "en", "Tataru Taru")]
    public void Resolve_NoTags_PassesThroughUnchanged(string input, string lang, string expected)
    {
        Assert.Equal(expected, NpcNameNormalizer.Resolve(input, lang, isFemale: false));
    }

    [Fact]
    public void Resolve_StripsLeftoverUnknownBracketTags()
    {
        // [x] tag with unknown letter — falls through to the regex strip.
        var result = NpcNameNormalizer.Resolve("Wachposten[x] der Wache", "de", isFemale: false);
        Assert.Equal("Wachposten der Wache", result);
    }

    // ── Capitalize ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("stille Druidin", "Stille Druidin")]
    [InlineData("Stille Druidin", "Stille Druidin")]   // already capitalized → no-op
    [InlineData("", "")]
    [InlineData("123 Drei", "123 Drei")]               // non-letter first char → unchanged
    public void Capitalize_TitleCasesFirstLetter(string input, string expected)
    {
        Assert.Equal(expected, NpcNameNormalizer.Capitalize(input));
    }
}
