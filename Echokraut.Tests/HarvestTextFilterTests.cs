using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The strings in these tests are verbatim from the live Excel sheets (scanned 2026-08-09 across
/// quest/custom/cut_scene in all four locales) — they are the complete set of distinct texts that
/// contain a placeholder marker, plus the real dialogue that a naive filter would destroy.
/// </summary>
public class HarvestTextFilterTests
{
    [Theory]
    // The dominant form — 13,360 occurrences in the Japanese sheets alone.
    [InlineData("（★未使用／削除予定★）")]
    [InlineData("（★未使用★）")]
    [InlineData("（★未使用／削除★）")]
    [InlineData("★削除予定★")]
    [InlineData("(★未使用／削除予定★)")]   // half-width parens
    [InlineData("●★未使用★●")]
    [InlineData("（★未使用／削除予定★） ")] // trailing space
    public void DecoratedMarkers_ArePlaceholders(string text)
        => Assert.True(HarvestTextFilter.IsDeveloperPlaceholder(text));

    [Fact]
    public void PlaceholderFollowedByMoreText_IsStillAPlaceholder()
        => Assert.True(HarvestTextFilter.IsDeveloperPlaceholder("（★未使用／削除予定★）\n●攻略任務解放しますー"));

    [Fact]
    public void UnusedInRealDialogue_IsKept()
    {
        // Easter event line: "unused" refers to eggs, not to the row. No ★ decoration.
        const string real = "また、未使用のを\n三都市の「エッグドリーマー」に持ち込むと、\n様々なアイテム";
        Assert.False(HarvestTextFilter.IsDeveloperPlaceholder(real));
    }

    [Fact]
    public void StarWithoutMarker_IsKept()
    {
        // Gigi, quest/006/ClsGld500_00658 — stylized speech that legitimately uses ★.
        Assert.False(HarvestTextFilter.IsDeveloperPlaceholder("NE※GIGIGI★※...\nSeCrEtS aNd LiEs"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("Leer")]
    [InlineData("unused")]
    [InlineData("---")]
    [InlineData("消して下さい")]
    public void WholeTextPlaceholders_AreDetected(string text)
        => Assert.True(HarvestTextFilter.IsDeveloperPlaceholder(text));

    [Fact]
    public void ExactListIsNotSubstringMatched()
    {
        // "test" is a whole-text placeholder, but must not kill a sentence containing it.
        Assert.False(HarvestTextFilter.IsDeveloperPlaceholder("We shall test your mettle."));
        // "-" likewise.
        Assert.False(HarvestTextFilter.IsDeveloperPlaceholder("A dash - like this - is fine."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIsNotAPlaceholder(string? text)
    {
        // "no text" is a different condition the harvest handles separately; conflating the two
        // would silently hide missing translations.
        Assert.False(HarvestTextFilter.IsDeveloperPlaceholder(text));
    }

    [Fact]
    public void OrdinaryDialogue_IsKept()
    {
        Assert.False(HarvestTextFilter.IsDeveloperPlaceholder(
            "Well, my friends─now that we have all settled back into our bodies..."));
    }
}
