using System.Net;
using System.Text.Json;
using Echokraut.DataClasses;
using Xunit;

namespace Echokraut.Tests;

public class RemoteUrlsReachabilityTests : IDisposable
{
    private static readonly string RepoJsonPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Echokraut", "Resources", "RemoteUrls.json"));

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly RemoteUrlsData _data;

    public RemoteUrlsReachabilityTests()
    {
        var json = File.ReadAllText(RepoJsonPath);
        _data = JsonSerializer.Deserialize<RemoteUrlsData>(json)!;
    }

    public void Dispose() => _http.Dispose();

    private async Task<HttpResponseMessage> SendWithRetry(HttpRequestMessage request)
    {
        try
        {
            return await _http.SendAsync(request);
        }
        catch
        {
            // Single retry after 5s
            await Task.Delay(TimeSpan.FromSeconds(5));
            var retry = new HttpRequestMessage(request.Method, request.RequestUri);
            return await _http.SendAsync(retry);
        }
    }

    [Fact]
    public async Task AllUrls_ReturnSuccessStatusCode()
    {
        var allUrls = new List<string>
        {
            _data.AlltalkUrl,
            _data.InstallerUrl,
            _data.MsBuildToolsUrl,
            _data.NpcRacesUrl,
            _data.NpcGendersUrl,
            _data.EmoticonsUrl,
        };
        allUrls.AddRange(_data.XttsModelUrls);
        allUrls.AddRange(_data.VoiceNameUrls.Values);

        foreach (var url in allUrls)
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await SendWithRetry(request);

            Assert.True(
                response.IsSuccessStatusCode,
                $"URL returned {response.StatusCode}: {url}");
        }
    }

    [Fact]
    public async Task XttsModelUrls_AreDownloadable()
    {
        foreach (var url in _data.XttsModelUrls)
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await SendWithRetry(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(
                response.Content.Headers.ContentLength > 0,
                $"Content-Length is 0 for {url}");
        }
    }

    [Fact]
    public async Task VoiceNameUrls_ReturnValidJson()
    {
        foreach (var (lang, url) in _data.VoiceNameUrls)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await SendWithRetry(request);

            Assert.True(response.IsSuccessStatusCode, $"Voice name URL ({lang}) returned {response.StatusCode}: {url}");

            var body = await response.Content.ReadAsStringAsync();
            var ex = Record.Exception(() => JsonDocument.Parse(body));
            Assert.True(ex == null, $"Voice name URL ({lang}) returned invalid JSON: {url}");
        }
    }

    [Fact]
    public async Task NpcDataUrls_ReturnValidJson()
    {
        var urls = new[] { _data.NpcRacesUrl, _data.NpcGendersUrl, _data.EmoticonsUrl };

        foreach (var url in urls)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await SendWithRetry(request);

            Assert.True(response.IsSuccessStatusCode, $"NPC data URL returned {response.StatusCode}: {url}");

            var body = await response.Content.ReadAsStringAsync();
            var ex = Record.Exception(() => JsonDocument.Parse(body));
            Assert.True(ex == null, $"NPC data URL returned invalid JSON: {url}");
        }
    }

    [Fact]
    public async Task GoogleDriveUrls_AreReachable()
    {
        var driveUrls = new[] { _data.VoicesUrl, _data.Voices2Url };

        foreach (var url in driveUrls)
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await SendWithRetry(request);

            // Google Drive may return 200 or 303 (redirect)
            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                response.StatusCode == HttpStatusCode.SeeOther ||
                response.StatusCode == HttpStatusCode.Found ||
                response.StatusCode == HttpStatusCode.TemporaryRedirect,
                $"Google Drive URL returned unexpected {response.StatusCode}: {url}");
        }
    }
}
