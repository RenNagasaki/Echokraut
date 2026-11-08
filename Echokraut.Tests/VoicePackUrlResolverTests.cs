using System.Collections.Generic;
using Dalamud.Game;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Which voice pack a client gets: its own language, else English, else the single pack from before
/// per-language packs existed.
/// </summary>
public class VoicePackUrlResolverTests
{
    private static Dictionary<string, string> Packs() => new()
    {
        ["English"] = "https://example.invalid/en.zip",
        ["German"] = "https://example.invalid/de.zip",
    };

    [Fact]
    public void LanguageWithItsOwnPack_GetsIt()
        => Assert.Equal("https://example.invalid/de.zip",
            VoicePackUrlResolver.Resolve(Packs(), "German", "https://example.invalid/legacy.zip"));

    [Fact]
    public void LanguageWithoutAPack_GetsEnglish()
        => Assert.Equal("https://example.invalid/en.zip",
            VoicePackUrlResolver.Resolve(Packs(), "Japanese", "https://example.invalid/legacy.zip"));

    [Fact]
    public void EmptyEntry_CountsAsNotPublished()
    {
        // A key that is present but blank is how a pack gets pulled back without removing the line.
        var packs = new Dictionary<string, string> { ["English"] = "https://example.invalid/en.zip", ["German"] = "" };

        Assert.Equal("https://example.invalid/en.zip", VoicePackUrlResolver.Resolve(packs, "German", ""));
        Assert.False(VoicePackUrlResolver.IsLanguageSpecific(packs, "German"));
    }

    [Fact]
    public void NoMapAtAll_UsesTheLegacySinglePack()
    {
        // Older RemoteUrls.json without voicePackUrls must keep working unchanged.
        Assert.Equal("https://example.invalid/legacy.zip",
            VoicePackUrlResolver.Resolve(null, "German", "https://example.invalid/legacy.zip"));
        Assert.Equal("https://example.invalid/legacy.zip",
            VoicePackUrlResolver.Resolve(new Dictionary<string, string>(), "German",
                "https://example.invalid/legacy.zip"));
    }

    [Fact]
    public void NothingPublishedAnywhere_IsEmpty_SoTheCallerCanSkip()
        => Assert.Equal(string.Empty,
            VoicePackUrlResolver.Resolve(new Dictionary<string, string>(), "German", ""));

    [Fact]
    public void IsLanguageSpecific_SaysWhetherTheUserGetsTheirOwnLanguage()
    {
        Assert.True(VoicePackUrlResolver.IsLanguageSpecific(Packs(), "German"));
        Assert.False(VoicePackUrlResolver.IsLanguageSpecific(Packs(), "French"));
        Assert.False(VoicePackUrlResolver.IsLanguageSpecific(null, "German"));
    }

    [Theory]
    [InlineData(ClientLanguage.German, "German")]
    [InlineData(ClientLanguage.English, "English")]
    [InlineData(ClientLanguage.French, "French")]
    [InlineData(ClientLanguage.Japanese, "Japanese")]
    public void LanguageKeys_MatchTheRemoteJsonSpelling(ClientLanguage language, string expected)
        => Assert.Equal(expected, ClientLanguageKeys.KeyFor(language));
}
