using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Unit tests for the voice-actor split parser + epoch resolver
/// (<see cref="VoiceActorSplits"/>). Pure logic — no game runtime needed.
/// </summary>
public class VoiceActorSplitsTests
{
    private static VoiceActorSplits BuildSplits(params VoiceActorSplitEntry[] entries)
    {
        var file = new VoiceActorSplitsFile { Splits = new List<VoiceActorSplitEntry>(entries) };
        return VoiceActorSplits.Build(file, out _);
    }

    // ── patch-token extraction ───────────────────────────────────────────────

    [Theory]
    [InlineData("vo_voiceman_06006_000010", true, "06006")]
    [InlineData("vo_manfst_00100_000010", true, "00100")]
    [InlineData("vo_voiceman_07410_000010", true, "07410")]
    [InlineData("too_short", false, "")]                 // only 2 segments
    [InlineData("vo_voiceman_0600_000010", false, "")]   // 4-digit token
    [InlineData("vo_voiceman_abcde_000010", false, "")]  // non-numeric token
    [InlineData("", false, "")]
    public void TryExtractPatchToken_HandlesShapes(string audioBase, bool ok, string expected)
    {
        var success = VoiceActorSplits.TryExtractPatchToken(audioBase, out var token);
        Assert.Equal(ok, success);
        if (ok) Assert.Equal(expected, token);
    }

    // ── epoch-name auto-generation ───────────────────────────────────────────

    [Theory]
    // One boundary → Pre / Post.
    [InlineData("06000", new[] { "06010" }, "Pre06010")]
    [InlineData("06009", new[] { "06010" }, "Pre06010")]
    [InlineData("06010", new[] { "06010" }, "Post06010")]   // boundary is inclusive of the new actor
    [InlineData("07410", new[] { "06010" }, "Post06010")]
    public void EpochName_OneBoundary_PreOrPost(string token, string[] boundaries, string expected)
    {
        Assert.Equal(expected, VoiceActorSplits.EpochName(token, boundaries));
    }

    [Theory]
    // Two boundaries → Pre{b1} / From{b1} / From{b2}.
    [InlineData("02000", new[] { "03000", "06010" }, "Pre03000")]
    [InlineData("03000", new[] { "03000", "06010" }, "From03000")]
    [InlineData("05000", new[] { "03000", "06010" }, "From03000")]
    [InlineData("06010", new[] { "03000", "06010" }, "From06010")]
    [InlineData("07000", new[] { "03000", "06010" }, "From06010")]
    public void EpochName_TwoBoundaries_PreFromFrom(string token, string[] boundaries, string expected)
    {
        Assert.Equal(expected, VoiceActorSplits.EpochName(token, boundaries));
    }

    // ── ResolveEpoch / HasSplit ──────────────────────────────────────────────

    [Fact]
    public void ResolveEpoch_NoEntry_ReturnsEmptyEpoch()
    {
        var splits = BuildSplits(); // none configured
        Assert.False(splits.HasAnySplits);
        Assert.False(splits.HasSplit("Female_Hyur_Iceheart", "de"));
        Assert.Equal("", splits.ResolveEpoch("Female_Hyur_Iceheart", "de", "vo_voiceman_06006_000010"));
    }

    [Fact]
    public void ResolveEpoch_MatchingEntry_BucketsByPatch()
    {
        var splits = BuildSplits(new VoiceActorSplitEntry
        {
            VoiceKey = "Female_Hyur_Iceheart",
            Language = "DE",
            BoundaryPatches = new List<string> { "06010" },
        });

        Assert.True(splits.HasSplit("Female_Hyur_Iceheart", "de")); // case-insensitive lang
        Assert.Equal("Pre06010", splits.ResolveEpoch("Female_Hyur_Iceheart", "de", "vo_voiceman_06006_000010"));
        Assert.Equal("Post06010", splits.ResolveEpoch("Female_Hyur_Iceheart", "de", "vo_voiceman_07410_000010"));
    }

    [Fact]
    public void HasSplit_LanguageMismatch_DoesNotMatch()
    {
        var splits = BuildSplits(new VoiceActorSplitEntry
        {
            VoiceKey = "Female_Hyur_Iceheart",
            Language = "DE",
            BoundaryPatches = new List<string> { "06010" },
        });
        // Same voice, different client language → no split applies.
        Assert.False(splits.HasSplit("Female_Hyur_Iceheart", "en"));
        Assert.Equal("", splits.ResolveEpoch("Female_Hyur_Iceheart", "en", "vo_voiceman_06006_000010"));
    }

    [Fact]
    public void ResolveEpoch_UnparseableToken_RoutesToEarliestEpoch()
    {
        var splits = BuildSplits(new VoiceActorSplitEntry
        {
            VoiceKey = "Female_Hyur_Iceheart",
            Language = "DE",
            BoundaryPatches = new List<string> { "06010" },
        });
        // No usable patch token → earliest epoch (never dropped).
        Assert.Equal("Pre06010", splits.ResolveEpoch("Female_Hyur_Iceheart", "de", "garbage"));
    }

    // ── validation ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_DropsEntryWithNonAscendingBoundaries()
    {
        var splits = BuildSplits(new VoiceActorSplitEntry
        {
            VoiceKey = "X_Y_Z",
            Language = "EN",
            BoundaryPatches = new List<string> { "06010", "03000" }, // descending
        });
        Assert.False(splits.HasAnySplits);
    }

    [Fact]
    public void Build_DropsEntryWithNon5DigitBoundary()
    {
        var splits = BuildSplits(new VoiceActorSplitEntry
        {
            VoiceKey = "X_Y_Z",
            Language = "EN",
            BoundaryPatches = new List<string> { "6010" }, // 4 digits
        });
        Assert.False(splits.HasAnySplits);
    }

    [Fact]
    public void Build_DropsEntryWithEmptyBoundaries()
    {
        var splits = BuildSplits(new VoiceActorSplitEntry
        {
            VoiceKey = "X_Y_Z",
            Language = "EN",
            BoundaryPatches = new List<string>(),
        });
        Assert.False(splits.HasAnySplits);
    }

    [Fact]
    public void Build_KeepsValidEntry_AlongsideInvalidOne()
    {
        var splits = BuildSplits(
            new VoiceActorSplitEntry { VoiceKey = "Bad", Language = "EN", BoundaryPatches = new() { "abc" } },
            new VoiceActorSplitEntry { VoiceKey = "Good", Language = "EN", BoundaryPatches = new() { "06010" } });
        Assert.True(splits.HasAnySplits);
        Assert.False(splits.HasSplit("Bad", "en"));
        Assert.True(splits.HasSplit("Good", "en"));
    }

    [Fact]
    public void Parse_EmptySplitsArray_ProducesInertView()
    {
        var json = "{ \"version\": 1, \"splits\": [] }";
        var splits = VoiceActorSplits.Parse(json, out var warnings);
        Assert.False(splits.HasAnySplits);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Parse_GarbageJson_ReturnsEmptyWithWarning()
    {
        var splits = VoiceActorSplits.Parse("{ not valid json ", out var warnings);
        Assert.False(splits.HasAnySplits);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Parse_IgnoresUnknownTopLevelFields()
    {
        // The shipped resource carries _comment / _example documentation fields the parser
        // must ignore without choking.
        var json = "{ \"_comment\": \"docs\", \"_example\": { \"voiceKey\": \"a\" }, \"splits\": " +
                   "[ { \"voiceKey\": \"Female_Hyur_Iceheart\", \"language\": \"DE\", \"boundaryPatches\": [\"06010\"] } ] }";
        var splits = VoiceActorSplits.Parse(json, out _);
        Assert.True(splits.HasSplit("Female_Hyur_Iceheart", "de"));
    }
}
