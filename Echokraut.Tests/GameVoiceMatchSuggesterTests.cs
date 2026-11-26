using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Suggestions for unmatched tokens (§11.5 report).</summary>
public sealed class GameVoiceMatchSuggesterTests
{
    private static readonly string[] Names = { "Sphene", "Vrtra", "Y'shtola", "Thancred", "G'raha Tia", "Count Edmont de Fortemps" };

    [Theory]
    [InlineData("NEWSPHENE", "Sphene")]
    [InlineData("VRITRA", "Vrtra")]
    [InlineData("EDMONT", "Count Edmont de Fortemps")]
    [InlineData("YSHTOLA", "Y'shtola")]
    public void BestSuggestion(string token, string expected) =>
        Assert.Equal(expected, GameVoiceMatchSuggester.Suggest(token, Names).First().NpcName);

    [Fact]
    public void Unrelated_NoSuggestion() => Assert.Empty(GameVoiceMatchSuggester.Suggest("MYSTERYVOICE", Names));

    [Fact]
    public void Similarity_IsLevenshteinBased()
    {
        Assert.Equal(1.0, GameVoiceMatchSuggester.Similarity("abc", "abc"));
        Assert.Equal(0.6, GameVoiceMatchSuggester.Similarity("vritr", "vrtra"), 3); // 2 edits / 5
    }
}
