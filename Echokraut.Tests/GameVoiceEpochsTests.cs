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

/// <summary>Epoch choice by progress (§11.4).</summary>
public sealed class GameVoiceEpochsTests
{
    [Fact]
    public void StartTokens() =>
        Assert.Equal(new string?[] { null, null, "06100", "06100" },
            new[] { "", "Pre06100", "Post06100", "From06100" }.Select(GameVoiceEpochs.StartToken));

    [Fact]
    public void UnknownProgress_TakesTheNewest() =>
        Assert.Equal("From07000", GameVoiceEpochs.Choose(new[] { "From07000", "Pre06100", "From06100" }, null));

    [Theory]
    [InlineData("06006", "Pre06100")]
    [InlineData("06100", "From06100")]
    [InlineData("06500", "From06100")]
    [InlineData("07000", "From07000")]
    public void LatestReachedEpoch(string reached, string expected) =>
        Assert.Equal(expected, GameVoiceEpochs.Choose(new[] { "Pre06100", "From06100", "From07000" },
            t => string.CompareOrdinal(t, reached) <= 0));

    [Fact]
    public void NoSplit_IsTheOnlyEpoch() => Assert.Equal("", GameVoiceEpochs.Choose(new[] { "" }, _ => false));

    [Fact]
    public void OnlyALaterEpochHasClips_StillSpeaks() =>
        Assert.Equal("Post06100", GameVoiceEpochs.Choose(new[] { "Post06100" }, _ => false));
}
