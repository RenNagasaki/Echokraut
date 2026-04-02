using System.Reflection;
using System.Text.Json;
using Echokraut.DataClasses;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class RemoteUrlsJsonTests
{
    private static readonly string RepoJsonPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Echokraut", "Resources", "RemoteUrls.json"));

    private static RemoteUrlsData LoadFromFile()
    {
        Assert.True(File.Exists(RepoJsonPath), $"RemoteUrls.json not found at {RepoJsonPath}");
        var json = File.ReadAllText(RepoJsonPath);
        return JsonSerializer.Deserialize<RemoteUrlsData>(json)!;
    }

    [Fact]
    public void JsonFile_IsValidJson()
    {
        var json = File.ReadAllText(RepoJsonPath);
        var ex = Record.Exception(() => JsonSerializer.Deserialize<RemoteUrlsData>(json));
        Assert.Null(ex);
    }

    [Fact]
    public void JsonFile_HasVersionField()
    {
        var data = LoadFromFile();
        Assert.True(data.Version > 0, "version must be a positive integer");
    }

    [Fact]
    public void JsonFile_AllRequiredFieldsPresent()
    {
        var data = LoadFromFile();

        Assert.False(string.IsNullOrWhiteSpace(data.AlltalkUrl), "alltalkUrl is missing");
        Assert.False(string.IsNullOrWhiteSpace(data.InstallerUrl), "installerUrl is missing");
        Assert.False(string.IsNullOrWhiteSpace(data.VoicesUrl), "voicesUrl is missing");
        Assert.False(string.IsNullOrWhiteSpace(data.Voices2Url), "voices2Url is missing");
        Assert.False(string.IsNullOrWhiteSpace(data.MsBuildToolsUrl), "msBuildToolsUrl is missing");
        Assert.NotNull(data.XttsModelUrls);
        Assert.NotEmpty(data.XttsModelUrls);
        Assert.False(string.IsNullOrWhiteSpace(data.NpcRacesUrl), "npcRacesUrl is missing");
        Assert.False(string.IsNullOrWhiteSpace(data.NpcGendersUrl), "npcGendersUrl is missing");
        Assert.False(string.IsNullOrWhiteSpace(data.EmoticonsUrl), "emoticonsUrl is missing");
        Assert.NotNull(data.VoiceNameUrls);
        Assert.NotEmpty(data.VoiceNameUrls);
    }

    [Fact]
    public void JsonFile_AllUrlsAreWellFormed()
    {
        var data = LoadFromFile();

        var allUrls = new List<string>
        {
            data.AlltalkUrl,
            data.InstallerUrl,
            data.VoicesUrl,
            data.Voices2Url,
            data.MsBuildToolsUrl,
            data.NpcRacesUrl,
            data.NpcGendersUrl,
            data.EmoticonsUrl,
        };
        allUrls.AddRange(data.XttsModelUrls);
        allUrls.AddRange(data.VoiceNameUrls.Values);

        foreach (var url in allUrls)
        {
            Assert.True(
                Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"),
                $"Invalid URL: {url}");
        }
    }

    [Fact]
    public void JsonFile_XttsModelUrlsHasExpectedCount()
    {
        var data = LoadFromFile();
        Assert.Equal(8, data.XttsModelUrls.Length);
    }

    [Fact]
    public void JsonFile_VoiceNameUrlsHasAllLanguages()
    {
        var data = LoadFromFile();
        Assert.True(data.VoiceNameUrls.ContainsKey("German"), "Missing German voice name URL");
        Assert.True(data.VoiceNameUrls.ContainsKey("English"), "Missing English voice name URL");
        Assert.True(data.VoiceNameUrls.ContainsKey("French"), "Missing French voice name URL");
        Assert.True(data.VoiceNameUrls.ContainsKey("Japanese"), "Missing Japanese voice name URL");
    }

    [Fact]
    public void JsonFile_MatchesEmbeddedFallback()
    {
        var fileJson = File.ReadAllText(RepoJsonPath);
        var fileData = JsonSerializer.Deserialize<RemoteUrlsData>(fileJson)!;
        var embedded = RemoteUrlService.LoadEmbeddedFallback();

        Assert.Equal(fileData.Version, embedded.Version);
        Assert.Equal(fileData.AlltalkUrl, embedded.AlltalkUrl);
        Assert.Equal(fileData.InstallerUrl, embedded.InstallerUrl);
        Assert.Equal(fileData.VoicesUrl, embedded.VoicesUrl);
        Assert.Equal(fileData.Voices2Url, embedded.Voices2Url);
        Assert.Equal(fileData.MsBuildToolsUrl, embedded.MsBuildToolsUrl);
        Assert.Equal(fileData.XttsModelUrls, embedded.XttsModelUrls);
        Assert.Equal(fileData.NpcRacesUrl, embedded.NpcRacesUrl);
        Assert.Equal(fileData.NpcGendersUrl, embedded.NpcGendersUrl);
        Assert.Equal(fileData.EmoticonsUrl, embedded.EmoticonsUrl);
        Assert.Equal(fileData.VoiceNameUrls, embedded.VoiceNameUrls);
    }
}
