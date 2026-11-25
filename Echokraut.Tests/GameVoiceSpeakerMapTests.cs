using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Xunit;

namespace Echokraut.Tests;

public class GameVoiceSpeakerMapTests
{
    private static readonly Dictionary<uint, string> Names = new()
    {
        [1] = "Y'shtola",
        [2] = "Y'shtola",       // second ENpcResident row of the same character
        [3] = "Alisaie",
        [4] = "Alisaie Leveilleur", // shares the first name with row 3, different full name
        [5] = "G'raha Tia",
        [6] = "Abelie",
    };

    [Fact]
    public void ExactName_OwnsToken()
    {
        var map = GameVoiceSpeakerMap.Build(new[] { "YSHTOLA" }, Names, null, out _);
        Assert.Equal(new[] { "yshtola" }, map.TokensFor(new[] { "Y'shtola" }));
    }

    [Fact]
    public void SharedFirstName_TheExactFullNameOwnsTheToken()
    {
        // "ALISAIE" resolves to rows 3 and 4; row 3's full name IS the token, row 4 adds a surname →
        // the same NPC written out in full (live: "Estinien" / "Estinien Wyrmblood"): both speak with it.
        var map = GameVoiceSpeakerMap.Build(new[] { "ALISAIE" }, Names, null, out _);
        Assert.Equal("alisaie", map.OwnerOf("ALISAIE"));
        Assert.Equal(new[] { "alisaie" }, map.TokensFor(new[] { "Alisaie" }));
        Assert.Equal(new[] { "alisaie" }, map.TokensFor(new[] { "Alisaie Leveilleur" }));
        Assert.Empty(map.AmbiguousTokens);
    }

    [Fact]
    public void SharedFirstNameWithoutExactFullName_IsOwnedByNobody()
    {
        // "LEVEILLEUR"-style token hitting two different full names, neither equal to the token →
        // ambiguous, so it can never lend one character's voice to another (plan §3: same NPC only).
        var names = new Dictionary<uint, string> { [1] = "Alphinaud Leveilleur", [2] = "Alphinaud Senior" };
        var map = GameVoiceSpeakerMap.Build(new[] { "ALPHINAUD" }, names, null, out _);
        Assert.Null(map.OwnerOf("alphinaud"));
        Assert.Contains("alphinaud", map.AmbiguousTokens);
    }

    [Fact]
    public void NoPrefixMatch()
    {
        var map = GameVoiceSpeakerMap.Build(new[] { "ABEL" }, Names, null, out _);
        Assert.Empty(map.TokensFor(new[] { "Abelie" }));
    }

    [Fact]
    public void Alias_ByName_AssignsTokenAndWinsOverNameIndex()
    {
        var aliases = new[]
        {
            new GameVoiceAliasEntry { ShortName = "mysteryvoice", NpcName = "G'raha Tia" },
            new GameVoiceAliasEntry { ShortName = "ALISAIE", NpcId = 4 },
        };
        var map = GameVoiceSpeakerMap.Build(new[] { "MYSTERYVOICE", "ALISAIE" }, Names, aliases, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new[] { "mysteryvoice" }, map.TokensFor(new[] { "G'raha Tia" }));
        Assert.Equal(new[] { "alisaie" }, map.TokensFor(new[] { "Alisaie Leveilleur" }));
    }

    [Fact]
    public void Alias_UnknownNpcId_IsWarnedAndSkipped()
    {
        var aliases = new[] { new GameVoiceAliasEntry { ShortName = "x", NpcId = 999 } };
        GameVoiceSpeakerMap.Build(new[] { "X" }, Names, aliases, out var warnings);
        Assert.Single(warnings);
    }

    [Fact]
    public void LaterAliasWins()
    {
        var aliases = new[]
        {
            new GameVoiceAliasEntry { ShortName = "mysteryvoice", NpcName = "Abelie" },
            new GameVoiceAliasEntry { ShortName = "mysteryvoice", NpcName = "G'raha Tia" }, // local override
        };
        var map = GameVoiceSpeakerMap.Build(new[] { "MYSTERYVOICE" }, Names, aliases, out _);
        Assert.Empty(map.TokensFor(new[] { "Abelie" }));
        Assert.Single(map.TokensFor(new[] { "G'raha Tia" }));
    }

    // Disabled / ambiguous tokens in the speaker map (§11.5).
    private static readonly Dictionary<uint, string> English = new()
    {
        [1] = "Y'shtola", [2] = "Alphinaud", [3] = "Alphinaud Leveilleur", [4] = "Thancred",
    };

    [Fact]
    public void DisabledAlias_OwnsNothing_LaterEntryWins()
    {
        var map = GameVoiceSpeakerMap.Build(new[] { "yshtola", "thancred" }, English, new[]
        {
            new GameVoiceAliasEntry { ShortName = "YSHTOLA", Disabled = true },
            new GameVoiceAliasEntry { ShortName = "THANCRED", Disabled = true },
            new GameVoiceAliasEntry { ShortName = "THANCRED", NpcName = "Thancred" },
        }, out _);
        Assert.Null(map.OwnerOf("yshtola"));
        Assert.Contains("yshtola", map.DisabledTokens);
        Assert.Equal("thancred", map.OwnerOf("thancred"));
    }

    [Fact]
    public void SharedFirstName_ExactFullNameWins_NotAmbiguous()
    {
        var map = GameVoiceSpeakerMap.Build(new[] { "alphinaud" }, English, null, out _);
        Assert.Equal("alphinaud", map.OwnerOf("alphinaud"));
        Assert.DoesNotContain("alphinaud", map.AmbiguousTokens);
    }
}
