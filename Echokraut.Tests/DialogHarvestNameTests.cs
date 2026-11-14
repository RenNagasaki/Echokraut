using System.Collections.Generic;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class DialogHarvestNameTests
{
    [Theory]
    [InlineData("stille Druidin", "Stille Druidin")]   // German [a]→"e" lowercase stem
    [InlineData("kleine Helferlein", "Kleine Helferlein")]
    [InlineData("Stille Druidin", "Stille Druidin")]   // already title-cased — pass-through
    [InlineData("a", "A")]                              // single char
    [InlineData("", "")]                                // empty stays empty
    [InlineData("(special)", "(special)")]              // non-letter first char untouched
    [InlineData("Über", "Über")]                        // uppercase umlaut already canonical
    public void NormalizeNpcName_TitleCasesFirstLetter(string input, string expected)
    {
        Assert.Equal(expected, DialogHarvestService.NormalizeNpcName(input));
    }

    // ── AccumulateVoiceNameSuggestion ────────────────────────────────────────

    private static readonly string[] AllLangs = { "en", "de", "ja", "fr" };

    private static Dictionary<string, Dictionary<string, HashSet<string>>> NewPerLanguage()
    {
        var d = new Dictionary<string, Dictionary<string, HashSet<string>>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var lc in AllLangs)
            d[lc] = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
        return d;
    }

    [Fact]
    public void AccumulateVoiceNameSuggestion_PicksFakenameOnlyForLangsWithParenPrefix()
    {
        // Input: NPC has multilingual names; only the EN text has a (-Fakename-) prefix.
        // Expected: only the EN bucket gets a suggestion entry.
        var per = NewPerLanguage();
        var npcName = new Dictionary<string, string>
        {
            ["en"] = "Y'shtola Rhul",
            ["de"] = "Y'shtola Rhul",
        };
        var texts = new Dictionary<string, string>
        {
            ["en"] = "(-Mysterious Lady-)Hello, traveller.",
            ["de"] = "Sei gegrüßt, Reisender.",
        };

        DialogHarvestService.AccumulateVoiceNameSuggestion(42u, npcName, texts, AllLangs, per);

        Assert.Single(per["en"]);
        Assert.Contains("Mysterious Lady", per["en"]["Y'shtola Rhul"]);
        Assert.Empty(per["de"]);
        Assert.Empty(per["ja"]);
        Assert.Empty(per["fr"]);
    }

    [Fact]
    public void AccumulateVoiceNameSuggestion_DeduplicatesAcrossCallsForSameNpc()
    {
        // Two dialog lines from the same NPC, same fakename in EN, different fakename in DE.
        var per = NewPerLanguage();
        var npc = new Dictionary<string, string> { ["en"] = "Tataru Taru", ["de"] = "Tataru Taru" };

        DialogHarvestService.AccumulateVoiceNameSuggestion(1u, npc,
            new Dictionary<string, string>
            {
                ["en"] = "(-Energetic Lalafell-)Boss!",
                ["de"] = "(-Aufgeweckte Lala-)Boss!",
            }, AllLangs, per);
        DialogHarvestService.AccumulateVoiceNameSuggestion(1u, npc,
            new Dictionary<string, string>
            {
                ["en"] = "(-Energetic Lalafell-)One moment!",   // duplicate fakename — dedup
                ["de"] = "(-Lebhafte Lala-)Einen Moment!",      // distinct DE variant
            }, AllLangs, per);

        Assert.Single(per["en"]["Tataru Taru"]);                // dedup
        Assert.Equal(2, per["de"]["Tataru Taru"].Count);        // both DE fakenames
    }

    [Fact]
    public void AccumulateVoiceNameSuggestion_SkipsWhenFakenameEqualsNpcName()
    {
        // (-Tataru-) on NPC named "Tataru" carries no aliasing information — drop it
        // so the suggestion file isn't full of trivial no-op entries.
        var per = NewPerLanguage();
        DialogHarvestService.AccumulateVoiceNameSuggestion(1u,
            new Dictionary<string, string> { ["en"] = "Tataru" },
            new Dictionary<string, string> { ["en"] = "(-Tataru-)Hello!" },
            AllLangs, per);

        Assert.Empty(per["en"]);
    }

    [Fact]
    public void AccumulateVoiceNameSuggestion_SkipsWhenNpcIdIsZero()
    {
        // Unresolved NPC (id=0) → no entry. The user wants suggestions ONLY for lines
        // that already have a real NPC attribution.
        var per = NewPerLanguage();
        DialogHarvestService.AccumulateVoiceNameSuggestion(0u,
            new Dictionary<string, string> { ["en"] = "Someone" },
            new Dictionary<string, string> { ["en"] = "(-Stranger-)Hello." },
            AllLangs, per);

        Assert.Empty(per["en"]);
    }

    [Fact]
    public void AccumulateVoiceNameSuggestion_SkipsWhenTextDoesNotStartWithParenPrefix()
    {
        var per = NewPerLanguage();
        DialogHarvestService.AccumulateVoiceNameSuggestion(1u,
            new Dictionary<string, string> { ["en"] = "Y'shtola Rhul" },
            new Dictionary<string, string> { ["en"] = "Greetings, (-Mysterious Lady-) is what they call me." },
            AllLangs, per);

        // Paren-prefix is a START-of-text marker; mid-string occurrences don't count.
        Assert.Empty(per["en"]);
    }

    [Fact]
    public void AccumulateVoiceNameSuggestion_SkipsWhenLanguageNameMissing()
    {
        // Text exists in a language but the NPC name dictionary doesn't have that locale —
        // we can't emit a useful entry without knowing the canonical NPC name in that
        // language. Skip silently.
        var per = NewPerLanguage();
        DialogHarvestService.AccumulateVoiceNameSuggestion(1u,
            new Dictionary<string, string> { ["en"] = "Tataru" },                // de missing
            new Dictionary<string, string> { ["de"] = "(-Lala-)Hi!" },           // de present
            AllLangs, per);

        Assert.Empty(per["de"]);
        Assert.Empty(per["en"]);
    }

    // ── FindCollidingNames ────────────────────────────────────────────────────

    [Fact]
    public void FindCollidingNames_FlagsFakenameContainingOtherNpcName()
    {
        // Real-world bug: harvest mis-attributes "(-Kriles Stimme-)Hello" to Alisaie
        // because both NPCs reference the same DefaultTalk row. The fakename "Kriles
        // Stimme" contains "Krile" — a different NPC in the index — so it must flag.
        var idx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Krile", "Alisaie", "Tataru" };
        var hits = DialogHarvestService.FindCollidingNames("Kriles Stimme", "Alisaie", idx);
        Assert.Contains("Krile", hits);
        Assert.DoesNotContain("Alisaie", hits);
    }

    [Fact]
    public void FindCollidingNames_SkipsSelfNameMatch()
    {
        // The current NPC's own name appearing inside the fakename is NOT a collision.
        var idx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Yshtola", "Krile" };
        var hits = DialogHarvestService.FindCollidingNames("Yshtola Rhul", "Yshtola", idx);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindCollidingNames_IgnoresShortNames()
    {
        // Names < 4 chars (like "Al") would match too many fakenames and produce noise.
        // Filter them out at the source.
        var idx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Al", "Krile" };
        var hits = DialogHarvestService.FindCollidingNames("Albatross", "Alphinaud", idx);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindCollidingNames_ReturnsAllMatches()
    {
        var idx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Krile", "Tataru", "Alisaie" };
        var hits = DialogHarvestService.FindCollidingNames("Krile and Tataru speak", "Alisaie", idx);
        Assert.Contains("Krile", hits);
        Assert.Contains("Tataru", hits);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void FindCollidingNames_EmptyInputs()
    {
        var idx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Krile" };
        Assert.Empty(DialogHarvestService.FindCollidingNames("", "Alisaie", idx));
        Assert.Empty(DialogHarvestService.FindCollidingNames("Stranger", "Alisaie", new HashSet<string>()));
    }

    [Fact]
    public void FindCollidingNames_CaseInsensitive()
    {
        var idx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Krile" };
        Assert.Contains("Krile", DialogHarvestService.FindCollidingNames("KRILE'S VOICE", "Alisaie", idx));
        Assert.Contains("Krile", DialogHarvestService.FindCollidingNames("krile's voice", "Alisaie", idx));
    }
}
