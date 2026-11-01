using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Reading tag + zip URL out of a GitHub <c>releases/latest</c> payload. Both must come from the
/// same release — see <see cref="GitHubReleaseParser"/>.
/// </summary>
public class GitHubReleaseParserTests
{
    private static string Payload(string tag, params (string name, string url)[] assets)
    {
        var assetJson = string.Join(",", assets.Select(a =>
            $$"""{"name":"{{a.name}}","browser_download_url":"{{a.url}}"}"""));
        return $$"""{"tag_name":"{{tag}}","assets":[{{assetJson}}]}""";
    }

    [Fact]
    public void ParsesTagAndZipUrl()
    {
        var json = Payload("0.0.0.3", ("EchokrauTTS.zip", "https://example.com/EchokrauTTS.zip"));

        Assert.True(GitHubReleaseParser.TryParseLatestRelease(json, out var release, out var reason));
        Assert.Equal("0.0.0.3", release!.Tag);
        Assert.Equal("https://example.com/EchokrauTTS.zip", release.DownloadUrl);
        Assert.Empty(reason);
    }

    [Fact]
    public void PrefersTheWrapperZipOverOtherZipAssets()
    {
        // A release may ship more than one zip (debug symbols, tooling). The wrapper is the one we
        // install, and it must not depend on GitHub's asset ordering.
        var json = Payload("0.0.0.3",
            ("symbols.zip", "https://example.com/symbols.zip"),
            ("EchokrauTTS.zip", "https://example.com/EchokrauTTS.zip"));

        Assert.True(GitHubReleaseParser.TryParseLatestRelease(json, out var release, out _));
        Assert.Equal("https://example.com/EchokrauTTS.zip", release!.DownloadUrl);
    }

    [Fact]
    public void FallsBackToTheFirstZipWhenNoneIsNamedAfterTheWrapper()
    {
        var json = Payload("0.0.0.3",
            ("notes.txt", "https://example.com/notes.txt"),
            ("bundle.zip", "https://example.com/bundle.zip"));

        Assert.True(GitHubReleaseParser.TryParseLatestRelease(json, out var release, out _));
        Assert.Equal("https://example.com/bundle.zip", release!.DownloadUrl);
    }

    [Fact]
    public void ReleaseWithoutAZipIsAFailureNotAnUpdate()
    {
        // Reporting "no update" here would hide a broken release: we could not install it anyway.
        var json = Payload("0.0.0.3", ("checksums.txt", "https://example.com/checksums.txt"));

        Assert.False(GitHubReleaseParser.TryParseLatestRelease(json, out var release, out var reason));
        Assert.Null(release);
        Assert.Contains("no downloadable zip", reason);
    }

    [Fact]
    public void ReleaseWithoutAssetsArrayFails()
    {
        Assert.False(GitHubReleaseParser.TryParseLatestRelease("""{"tag_name":"0.0.0.3"}""", out _, out var reason));
        Assert.Contains("no downloadable zip", reason);
    }

    [Fact]
    public void MissingTagFails()
    {
        var json = """{"assets":[{"name":"EchokrauTTS.zip","browser_download_url":"https://example.com/a.zip"}]}""";

        Assert.False(GitHubReleaseParser.TryParseLatestRelease(json, out _, out var reason));
        Assert.Contains("no tag", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void EmptyResponseFails(string? json)
    {
        Assert.False(GitHubReleaseParser.TryParseLatestRelease(json, out _, out var reason));
        Assert.Contains("empty", reason);
    }

    [Fact]
    public void MalformedJsonFailsInsteadOfThrowing()
    {
        // A rate-limit or proxy page arrives as non-JSON; the check must report it, not crash the UI.
        Assert.False(GitHubReleaseParser.TryParseLatestRelease("<html>429</html>", out _, out var reason));
        Assert.Contains("malformed", reason);
    }

    [Fact]
    public void NonObjectPayloadFails()
    {
        Assert.False(GitHubReleaseParser.TryParseLatestRelease("[]", out _, out var reason));
        Assert.Contains("unexpected", reason);
    }

    [Fact]
    public void TagIsTrimmed()
    {
        var json = Payload(" 0.0.0.3 ", ("EchokrauTTS.zip", "https://example.com/a.zip"));

        Assert.True(GitHubReleaseParser.TryParseLatestRelease(json, out var release, out _));
        Assert.Equal("0.0.0.3", release!.Tag);
    }
}
