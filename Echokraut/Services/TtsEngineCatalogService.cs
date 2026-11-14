using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <summary>
/// Loads <c>engines.json</c> from the EchokrauTTS wrapper repo, at the release tag the plugin
/// currently points at (<c>RemoteUrls.echokrauTtsEnginesUrl</c>, <c>{version}</c> substituted with
/// <c>echokrauTtsVersion</c>). Falls back, in this order, to the same file on the wrapper's default
/// branch and then to the copy embedded in the plugin.
///
/// <para><b>Why the tag and not the branch:</b> the tag is what users can actually install. Reading
/// the branch would advertise engines that no published wrapper ships yet — the branch is only the
/// second chance for the case where a release predates the file (the current one does).</para>
///
/// <para><b>Why the fetch is off the constructor:</b> the catalog is UI-only. Blocking plugin startup
/// (or worse, a UI frame) on two HTTP round trips to show a dropdown is not worth it, so the
/// embedded list is live immediately and the remote one replaces it when it arrives. A window built
/// in the seconds before that shows the shipped list — one plugin restart behind at worst.</para>
/// </summary>
public class TtsEngineCatalogService : ITtsEngineCatalogService, IDisposable
{
    /// <summary>Placeholder in the configured URL, replaced by the wrapper release tag.</summary>
    public const string VersionPlaceholder = "{version}";

    /// <summary>Branch used for the second attempt when the tagged file does not exist.</summary>
    public const string FallbackRef = "main";

    private const int ExpectedVersion = 1;
    private const string EmbeddedResourceName = "Echokraut.Resources.Engines.json";

    private readonly ILogService _log;
    private readonly IRemoteUrlService _remoteUrls;
    private readonly HttpClient _http;

    private volatile IReadOnlyList<TtsEngineInfo> _engines;

    public IReadOnlyList<TtsEngineInfo> Engines => _engines;
    public bool RemoteLoaded { get; private set; }

    public TtsEngineCatalogService(ILogService log, IRemoteUrlService remoteUrls)
        : this(log, remoteUrls, new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
    }

    public TtsEngineCatalogService(ILogService log, IRemoteUrlService remoteUrls, HttpClient http)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        _http = http ?? throw new ArgumentNullException(nameof(http));

        _engines = LoadEmbedded();
        _ = Task.Run(LoadRemote);
    }

    /// <summary>Builds the two candidate URLs (tagged, then default branch) for the template.</summary>
    public static IReadOnlyList<string> BuildUrls(string? template, string? version)
    {
        if (string.IsNullOrWhiteSpace(template)) return [];
        if (!template.Contains(VersionPlaceholder, StringComparison.Ordinal)) return [template];

        var urls = new List<string>();
        if (!string.IsNullOrWhiteSpace(version))
            urls.Add(template.Replace(VersionPlaceholder, version, StringComparison.Ordinal));

        var branchUrl = template.Replace(VersionPlaceholder, FallbackRef, StringComparison.Ordinal);
        if (!urls.Contains(branchUrl)) urls.Add(branchUrl);
        return urls;
    }

    /// <summary>Parses a catalog document; returns null for anything unusable (wrong schema version,
    /// no engines, malformed JSON) so the caller keeps the list it already has.</summary>
    public static IReadOnlyList<TtsEngineInfo>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        TtsEngineCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<TtsEngineCatalog>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (catalog == null || catalog.Version != ExpectedVersion) return null;
        return catalog.Engines.Count > 0 ? catalog.Engines : null;
    }

    private void LoadRemote()
    {
        var eventId = new EKEventId(0, TextSource.None);
        var urls = BuildUrls(_remoteUrls.Urls.EchokrauTtsEnginesUrl, _remoteUrls.Urls.EchokrauTtsVersion);

        foreach (var url in urls)
        {
            try
            {
                var json = _http.GetStringAsync(url).GetAwaiter().GetResult();
                var engines = Parse(json);
                if (engines == null)
                {
                    _log.Warning(nameof(LoadRemote), $"Engine catalog at {url} unusable, trying next source", eventId);
                    continue;
                }

                _engines = engines;
                RemoteLoaded = true;
                _log.Info(nameof(LoadRemote), $"Loaded {engines.Count} TTS engines from {url}", eventId);
                return;
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(LoadRemote), $"Engine catalog fetch failed ({url}): {ex.Message}", eventId);
            }
        }

        _log.Info(nameof(LoadRemote), "Using the embedded TTS engine catalog", eventId);
    }

    private IReadOnlyList<TtsEngineInfo> LoadEmbedded()
    {
        try
        {
            return Parse(ReadEmbedded()) ?? [];
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadEmbedded), $"Embedded engine catalog unreadable: {ex.Message}",
                new EKEventId(0, TextSource.None));
            return [];
        }
    }

    private static string ReadEmbedded()
    {
        using var stream = Assembly.GetAssembly(typeof(TtsEngineCatalogService))!
            .GetManifestResourceStream(EmbeddedResourceName);

        if (stream == null)
            throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' not found");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public void Dispose() => _http.Dispose();
}
