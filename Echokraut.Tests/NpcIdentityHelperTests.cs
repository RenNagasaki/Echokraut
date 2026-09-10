using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class NpcIdentityHelperTests
{
    // ── CanonicalRaceName ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Hyuran", "Hyur")]
    [InlineData("Miqo'te", "Miqote")]
    [InlineData("Au Ra", "AuRa")]
    public void CanonicalRaceName_MapsKnownSpellings(string input, string expected)
    {
        Assert.Equal(expected, NpcIdentityHelper.CanonicalRaceName(input));
    }

    [Theory]
    [InlineData("hyuran", "Hyur")]   // map is case-insensitive
    [InlineData("AU RA", "AuRa")]
    public void CanonicalRaceName_IsCaseInsensitive(string input, string expected)
    {
        Assert.Equal(expected, NpcIdentityHelper.CanonicalRaceName(input));
    }

    [Theory]
    [InlineData("Hyur")]             // already canonical → unchanged
    [InlineData("Elezen")]           // unmapped → unchanged
    [InlineData("Roegadyn")]
    public void CanonicalRaceName_PassesUnmappedThrough(string input)
    {
        Assert.Equal(input, NpcIdentityHelper.CanonicalRaceName(input));
    }

    // ── IsWildRace ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(NpcRaces.Hyur)]
    [InlineData(NpcRaces.AuRa)]
    [InlineData(NpcRaces.Miqote)]
    [InlineData(NpcRaces.Roegadyn)]
    [InlineData(NpcRaces.Hrothgar)]
    [InlineData(NpcRaces.Lalafell)]
    [InlineData(NpcRaces.Elezen)]
    [InlineData(NpcRaces.Viera)]
    public void IsWildRace_PlayableRaces_AreNotWild(NpcRaces race)
    {
        Assert.False(NpcIdentityHelper.IsWildRace(race));
    }

    [Fact]
    public void IsWildRace_UnknownAndBeastRaces_AreWild()
    {
        // Anything outside the eight playable races falls through to the wild branch.
        Assert.True(NpcIdentityHelper.IsWildRace(NpcRaces.Unknown));
    }
}
