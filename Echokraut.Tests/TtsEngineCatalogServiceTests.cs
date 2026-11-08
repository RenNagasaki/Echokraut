using System.IO;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The HTTP-free parts of <see cref="TtsEngineCatalogService"/>: which URLs it tries, and what it
/// accepts as a catalog.
/// </summary>
public class TtsEngineCatalogServiceTests
{
    [Fact]
    public void BuildUrls_TriesTheReleaseTagFirst_ThenTheDefaultBranch()
    {
        var urls = TtsEngineCatalogService.BuildUrls(
            "https://raw.githubusercontent.com/o/r/{version}/engines.json", "0.0.0.2");

        Assert.Equal(
        [
            "https://raw.githubusercontent.com/o/r/0.0.0.2/engines.json",
            "https://raw.githubusercontent.com/o/r/main/engines.json",
        ], urls);
    }

    [Fact]
    public void BuildUrls_WithoutAKnownTag_OnlyTriesTheBranch()
    {
        var urls = TtsEngineCatalogService.BuildUrls(
            "https://raw.githubusercontent.com/o/r/{version}/engines.json", "");

        Assert.Single(urls);
        Assert.EndsWith("/main/engines.json", urls[0]);
    }

    [Fact]
    public void BuildUrls_UrlWithoutAPlaceholder_IsUsedAsIs()
    {
        var urls = TtsEngineCatalogService.BuildUrls("https://example.com/engines.json", "0.0.0.2");

        Assert.Equal(["https://example.com/engines.json"], urls);
    }

    [Fact]
    public void BuildUrls_NoTemplate_MeansNoRemoteLookup()
    {
        Assert.Empty(TtsEngineCatalogService.BuildUrls("", "0.0.0.2"));
    }

    [Fact]
    public void Parse_ReadsTheEngines()
    {
        var engines = TtsEngineCatalogService.Parse(
            """{"version":1,"engines":[{"id":"moss","name":"MOSS","minVersion":"0.0.0.3"}]}""");

        Assert.NotNull(engines);
        Assert.Equal("moss", engines![0].Id);
        Assert.Equal("MOSS", engines[0].Name);
        Assert.Equal("0.0.0.3", engines[0].MinVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"version":2,"engines":[{"id":"moss"}]}""")] // future schema: not half-read
    [InlineData("""{"version":1,"engines":[]}""")]              // empty list: keep what we have
    public void Parse_RejectsAnythingUnusable(string json)
    {
        Assert.Null(TtsEngineCatalogService.Parse(json));
    }

    [Fact]
    public void EmbeddedFallback_IsUsable()
    {
        // The shipped copy is the last line of defence (no internet, GitHub down, URL removed). If it
        // stops parsing, the engine dropdown is empty and the user cannot pick an engine at all.
        var engines = TtsEngineCatalogService.Parse(File.ReadAllText(TestPaths.EnginesJsonPath));

        Assert.NotNull(engines);
        Assert.Contains(engines!, e => e.Id == "xtts");
        Assert.Contains(engines!, e => e.Id == "f5");
        Assert.All(engines!, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.MinVersion));
            Assert.Equal(e.Id.ToLowerInvariant(), e.Id); // the id goes to --tts-backend verbatim
        });
    }
}
