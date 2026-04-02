using System.Net;
using System.Text;
using System.Text.Json;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class RemoteUrlServiceTests
{
    private readonly Mock<ILogService> _logMock = new();

    private static RemoteUrlsData MakeFallback() => RemoteUrlService.LoadEmbeddedFallback();

    private static HttpClient MakeHttpClient(HttpStatusCode status, string content)
    {
        var handler = new FakeHandler(status, content);
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    private static string MakeValidJson(Action<RemoteUrlsData>? mutate = null)
    {
        var data = new RemoteUrlsData
        {
            Version = 1,
            AlltalkUrl = "https://example.com/alltalk.zip",
            InstallerUrl = "https://example.com/installer.zip",
            VoicesUrl = "https://example.com/voices.zip",
            Voices2Url = "https://example.com/voices2.zip",
            MsBuildToolsUrl = "https://example.com/buildtools.exe",
            XttsModelUrls = new[] { "https://example.com/model1.pth", "https://example.com/model2.pth" },
            NpcRacesUrl = "https://example.com/races.json",
            NpcGendersUrl = "https://example.com/genders.json",
            EmoticonsUrl = "https://example.com/emoticons.json",
            VoiceNameUrls = new Dictionary<string, string>
            {
                ["English"] = "https://example.com/en.json",
                ["German"] = "https://example.com/de.json",
                ["French"] = "https://example.com/fr.json",
                ["Japanese"] = "https://example.com/ja.json"
            }
        };
        mutate?.Invoke(data);
        return JsonSerializer.Serialize(data);
    }

    [Fact]
    public void FetchUrls_Success_ReturnsRemoteValues()
    {
        var json = MakeValidJson();
        using var http = MakeHttpClient(HttpStatusCode.OK, json);

        var service = new RemoteUrlService(_logMock.Object, http);

        Assert.Equal("https://example.com/alltalk.zip", service.Urls.AlltalkUrl);
        Assert.Equal("https://example.com/installer.zip", service.Urls.InstallerUrl);
        Assert.Equal(2, service.Urls.XttsModelUrls.Length);
    }

    [Fact]
    public void FetchUrls_Failure_ReturnsFallbackValues()
    {
        using var http = MakeHttpClient(HttpStatusCode.InternalServerError, "error");

        var service = new RemoteUrlService(_logMock.Object, http);
        var fallback = MakeFallback();

        Assert.Equal(fallback.AlltalkUrl, service.Urls.AlltalkUrl);
        Assert.Equal(fallback.InstallerUrl, service.Urls.InstallerUrl);
    }

    [Fact]
    public void FetchUrls_Timeout_ReturnsFallbackValues()
    {
        var handler = new TimeoutHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };

        var service = new RemoteUrlService(_logMock.Object, http);
        var fallback = MakeFallback();

        Assert.Equal(fallback.AlltalkUrl, service.Urls.AlltalkUrl);
    }

    [Fact]
    public void FetchUrls_InvalidJson_ReturnsFallbackValues()
    {
        using var http = MakeHttpClient(HttpStatusCode.OK, "not valid json {{{}}}");

        var service = new RemoteUrlService(_logMock.Object, http);
        var fallback = MakeFallback();

        Assert.Equal(fallback.AlltalkUrl, service.Urls.AlltalkUrl);
    }

    [Fact]
    public void FetchUrls_MissingField_FallsBackForThatField()
    {
        var json = MakeValidJson(d => d.VoicesUrl = "");
        using var http = MakeHttpClient(HttpStatusCode.OK, json);

        var service = new RemoteUrlService(_logMock.Object, http);
        var fallback = MakeFallback();

        // VoicesUrl was empty in remote → falls back
        Assert.Equal(fallback.VoicesUrl, service.Urls.VoicesUrl);
        // AlltalkUrl was present in remote → uses remote
        Assert.Equal("https://example.com/alltalk.zip", service.Urls.AlltalkUrl);
    }

    [Fact]
    public void FetchUrls_UnexpectedVersion_ReturnsFallbackValues()
    {
        var json = MakeValidJson(d => d.Version = 999);
        using var http = MakeHttpClient(HttpStatusCode.OK, json);

        var service = new RemoteUrlService(_logMock.Object, http);
        var fallback = MakeFallback();

        Assert.Equal(fallback.AlltalkUrl, service.Urls.AlltalkUrl);
    }

    [Fact]
    public void FetchUrls_CachesResult()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, MakeValidJson());
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        var service = new RemoteUrlService(_logMock.Object, http);

        // Access Urls multiple times
        _ = service.Urls.AlltalkUrl;
        _ = service.Urls.VoicesUrl;

        // Constructor fetches once; subsequent access uses cached property
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void EmbeddedFallback_LoadsSuccessfully()
    {
        var fallback = MakeFallback();

        Assert.Equal(1, fallback.Version);
        Assert.False(string.IsNullOrWhiteSpace(fallback.AlltalkUrl));
        Assert.False(string.IsNullOrWhiteSpace(fallback.InstallerUrl));
        Assert.False(string.IsNullOrWhiteSpace(fallback.VoicesUrl));
        Assert.False(string.IsNullOrWhiteSpace(fallback.Voices2Url));
        Assert.False(string.IsNullOrWhiteSpace(fallback.MsBuildToolsUrl));
        Assert.Equal(8, fallback.XttsModelUrls.Length);
        Assert.Equal(4, fallback.VoiceNameUrls.Count);
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    private class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;

        public FakeHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private class TimeoutHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            throw new TaskCanceledException();
        }
    }

    private class CountingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _content;
        public int CallCount { get; private set; }

        public CountingHandler(HttpStatusCode status, string content)
        {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            var response = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
