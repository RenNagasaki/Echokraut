using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
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

    private const string LocalCatalog =
        """{"version":1,"engines":[{"id":"qwen-1.7b","name":"Qwen","minVersion":"0.0.2.0"}]}""";

    [Fact]
    public void UseLocalCatalog_ReplacesTheList_AndALateRemoteDoesNotOverwriteIt()
    {
        var dir = Directory.CreateTempSubdirectory("ek-catalog-");
        try
        {
            var file = Path.Combine(dir.FullName, "engines.json");
            File.WriteAllText(file, LocalCatalog);
            var handler = new GatedHandler(
                """{"version":1,"engines":[{"id":"xtts","name":"XTTS"}]}""");
            using var sut = new TtsEngineCatalogService(new Mock<ILogService>().Object, Urls(), new HttpClient(handler));

            Assert.True(sut.UseLocalCatalog(file));
            Assert.Equal("qwen-1.7b", Assert.Single(sut.Engines).Id);

            handler.Release.Set();                       // the remote answer arrives after the pin
            Assert.True(handler.Answered.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(100);                           // let LoadRemote finish its swap attempt
            Assert.Equal("qwen-1.7b", Assert.Single(sut.Engines).Id);
            Assert.False(sut.RemoteLoaded);
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData(null)]          // file missing
    [InlineData("not json")]    // unusable
    public void UseLocalCatalog_BadFile_KeepsTheListAndReturnsFalse(string? content)
    {
        var dir = Directory.CreateTempSubdirectory("ek-catalog-");
        try
        {
            var file = Path.Combine(dir.FullName, "engines.json");
            if (content != null) File.WriteAllText(file, content);
            var handler = new GatedHandler("");
            using var sut = new TtsEngineCatalogService(new Mock<ILogService>().Object, Urls(), new HttpClient(handler));
            var before = sut.Engines;

            Assert.False(sut.UseLocalCatalog(file));
            Assert.Same(before, sut.Engines);
            handler.Release.Set();
        }
        finally { dir.Delete(true); }
    }

    private static IRemoteUrlService Urls()
    {
        var mock = new Mock<IRemoteUrlService>();
        mock.SetupGet(u => u.Urls).Returns(new RemoteUrlsData
        {
            EchokrauTtsEnginesUrl = "https://example.invalid/engines.json",
        });
        return mock.Object;
    }

    /// <summary>Answers every request with <c>body</c>, but only after <see cref="Release"/> is set.</summary>
    private sealed class GatedHandler(string body) : HttpMessageHandler
    {
        public ManualResetEventSlim Release { get; } = new(false);
        public ManualResetEventSlim Answered { get; } = new(false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Release.Wait(TimeSpan.FromSeconds(10), ct);
            Answered.Set();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
