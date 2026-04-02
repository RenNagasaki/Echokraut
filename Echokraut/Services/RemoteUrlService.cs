using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Echokraut.Services;

public class RemoteUrlService : IRemoteUrlService, IDisposable
{
    private const string RemoteJsonUrl =
        "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/RemoteUrls.json";
    private const int ExpectedVersion = 1;
    private const string EmbeddedResourceName = "Echokraut.Resources.RemoteUrls.json";

    private readonly ILogService _log;
    private readonly HttpClient _http;

    public RemoteUrlsData Urls { get; }

    public RemoteUrlService(ILogService log) : this(log, new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
    }

    public RemoteUrlService(ILogService log, HttpClient http)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _http = http ?? throw new ArgumentNullException(nameof(http));

        var fallback = LoadEmbeddedFallback();
        Urls = LoadRemoteWithFallback(fallback);
    }

    private RemoteUrlsData LoadRemoteWithFallback(RemoteUrlsData fallback)
    {
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            _log.Info(nameof(LoadRemoteWithFallback), "Fetching remote URL config", eventId);
            var json = _http.GetStringAsync(RemoteJsonUrl).GetAwaiter().GetResult();
            var remote = JsonSerializer.Deserialize<RemoteUrlsData>(json);

            if (remote == null)
            {
                _log.Warning(nameof(LoadRemoteWithFallback), "Remote JSON deserialized to null, using fallback", eventId);
                return fallback;
            }

            if (remote.Version != ExpectedVersion)
            {
                _log.Warning(nameof(LoadRemoteWithFallback),
                    $"Remote JSON version {remote.Version} != expected {ExpectedVersion}, using fallback", eventId);
                return fallback;
            }

            var merged = MergeWithFallback(remote, fallback);
            _log.Info(nameof(LoadRemoteWithFallback), "Remote URL config loaded successfully", eventId);
            return merged;
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadRemoteWithFallback), $"Failed to fetch remote URLs, using fallback: {ex.Message}", eventId);
            return fallback;
        }
    }

    private static RemoteUrlsData MergeWithFallback(RemoteUrlsData remote, RemoteUrlsData fallback)
    {
        return new RemoteUrlsData
        {
            Version = remote.Version,
            AlltalkUrl = StringOrFallback(remote.AlltalkUrl, fallback.AlltalkUrl),
            InstallerUrl = StringOrFallback(remote.InstallerUrl, fallback.InstallerUrl),
            VoicesUrl = StringOrFallback(remote.VoicesUrl, fallback.VoicesUrl),
            Voices2Url = StringOrFallback(remote.Voices2Url, fallback.Voices2Url),
            MsBuildToolsUrl = StringOrFallback(remote.MsBuildToolsUrl, fallback.MsBuildToolsUrl),
            XttsModelUrls = remote.XttsModelUrls.Length > 0 ? remote.XttsModelUrls : fallback.XttsModelUrls,
            NpcRacesUrl = StringOrFallback(remote.NpcRacesUrl, fallback.NpcRacesUrl),
            NpcGendersUrl = StringOrFallback(remote.NpcGendersUrl, fallback.NpcGendersUrl),
            EmoticonsUrl = StringOrFallback(remote.EmoticonsUrl, fallback.EmoticonsUrl),
            VoiceNameUrls = remote.VoiceNameUrls.Count > 0 ? remote.VoiceNameUrls : fallback.VoiceNameUrls,
        };
    }

    private static string StringOrFallback(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    public static RemoteUrlsData LoadEmbeddedFallback()
    {
        using var stream = Assembly.GetAssembly(typeof(RemoteUrlService))!
            .GetManifestResourceStream(EmbeddedResourceName);

        if (stream == null)
            throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' not found");

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return JsonSerializer.Deserialize<RemoteUrlsData>(json)
               ?? throw new InvalidOperationException("Embedded RemoteUrls.json deserialized to null");
    }

    public void Dispose() => _http.Dispose();
}
