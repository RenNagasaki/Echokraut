using Echotools.Logging.Services;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Echokraut.Services;

/// <summary>
/// Loads NPC metadata and voice-name mappings from the GitHub JSON resources.
/// </summary>
public class JsonDataService : IJsonDataService, IDisposable
{
    private readonly ILogService _log;
    private readonly IRemoteUrlService _remoteUrls;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly Dictionary<ClientLanguage, string> LanguageKeys = new()
    {
        [ClientLanguage.German]   = "German",
        [ClientLanguage.English]  = "English",
        [ClientLanguage.French]   = "French",
        [ClientLanguage.Japanese] = "Japanese",
    };

    public Dictionary<int, NpcRaces> ModelsToRaceMap { get; private set; } = new();
    public List<NpcGenderRaceMap> ModelGenderMap { get; private set; } = new();
    public List<string> Emoticons { get; private set; } = new();
    private List<VoiceMap> _voiceMaps = new();

    public IReadOnlyList<VoiceMap> VoiceMaps => _voiceMaps;

    public JsonDataService(ILogService log, IRemoteUrlService remoteUrls, ClientLanguage language)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        Reload(language);
    }

    public void Reload(ClientLanguage language)
    {
        var eventId = new EKEventId(0, TextSource.None);
        LoadModelsToRaceMap(eventId);
        LoadModelsToGenderMap(eventId);
        LoadEmoticons(eventId);
        LoadVoiceNames(language, eventId);

    }

    public string GetNpcName(string npcName)
    {
        var voiceMap = _voiceMaps.Find(p => p.speakers.Any(s => s.Equals(npcName, StringComparison.OrdinalIgnoreCase)));
        return voiceMap != null ? voiceMap.voiceName : npcName;
    }

    private void LoadModelsToRaceMap(EKEventId eventId)
    {
        try
        {
            var json = FetchUrl(_remoteUrls.Urls.NpcRacesUrl, eventId);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadModelsToRaceMap), "Remote NPC race map empty, using embedded fallback.", eventId);
                json = LoadEmbeddedResource("Echokraut.Resources.NpcRaces.json");
            }

            var remote = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, NpcRaces>>(json ?? "") ?? new();

            // Merge with embedded resource so local additions are always included
            var embedded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, NpcRaces>>(
                LoadEmbeddedResource("Echokraut.Resources.NpcRaces.json") ?? "{}") ?? new();
            foreach (var (key, value) in embedded)
                remote.TryAdd(key, value);

            ModelsToRaceMap = remote;
            _log.Info(nameof(LoadModelsToRaceMap), $"Loaded NPC race maps: {ModelsToRaceMap.Count} entries", eventId);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(LoadModelsToRaceMap), $"Error loading NPC race maps: {ex}", eventId);
        }
    }

    private static string? LoadEmbeddedResource(string resourceName)
    {
        using var stream = typeof(JsonDataService).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void LoadModelsToGenderMap(EKEventId eventId)
    {
        try
        {
            var json = FetchUrl(_remoteUrls.Urls.NpcGendersUrl, eventId);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadModelsToGenderMap), "Remote NPC gender map unavailable, using embedded fallback.", eventId);
                json = LoadEmbeddedResource("Echokraut.Resources.NpcGenders.json");
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadModelsToGenderMap), "Failed to load NPC gender maps.", eventId);
                return;
            }
            ModelGenderMap = System.Text.Json.JsonSerializer.Deserialize<List<NpcGenderRaceMap>>(json) ?? new();
            _log.Info(nameof(LoadModelsToGenderMap), $"Loaded NPC gender maps: {ModelGenderMap.Count} entries", eventId);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(LoadModelsToGenderMap), $"Error loading NPC gender maps: {ex}", eventId);
        }
    }

    private void LoadEmoticons(EKEventId eventId)
    {
        try
        {
            var json = FetchUrl(_remoteUrls.Urls.EmoticonsUrl, eventId);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadEmoticons), "Remote emoticons unavailable, using embedded fallback.", eventId);
                json = LoadEmbeddedResource("Echokraut.Resources.Emoticons.json");
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadEmoticons), "Failed to load emoticons.", eventId);
                return;
            }
            Emoticons = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new();
            _log.Info(nameof(LoadEmoticons), $"Loaded {Emoticons.Count} emoticons", eventId);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(LoadEmoticons), $"Error loading emoticons: {ex}", eventId);
        }
    }

    private void LoadVoiceNames(ClientLanguage language, EKEventId eventId)
    {
        var langKey = LanguageKeys.GetValueOrDefault(language, "English");
        if (!_remoteUrls.Urls.VoiceNameUrls.TryGetValue(langKey, out var url))
            url = _remoteUrls.Urls.VoiceNameUrls["English"];

        // Embedded fallback resource for the same language (e.g. Echokraut.Resources.VoiceNamesEN.json).
        var embeddedName = $"Echokraut.Resources.VoiceNames{langKey switch
        {
            "German" => "DE",
            "French" => "FR",
            "Japanese" => "JA",
            _ => "EN",
        }}.json";

        try
        {
            var json = FetchUrl(url, eventId);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadVoiceNames), "Remote voice name map unavailable, using embedded fallback.", eventId);
                json = LoadEmbeddedResource(embeddedName);
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Warning(nameof(LoadVoiceNames), "Failed to load voice name maps.", eventId);
                return;
            }
            _voiceMaps = System.Text.Json.JsonSerializer.Deserialize<List<VoiceMap>>(json) ?? new();
            _log.Info(nameof(LoadVoiceNames), $"Loaded voice name maps: {_voiceMaps.Count} entries", eventId);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(LoadVoiceNames), $"Error loading voice name maps: {ex}", eventId);
        }
    }

    // Backoff schedule for transient fetch failures (GitHub raw rate-limits the burst of
    // startup requests with 429). Each entry is the delay before the next retry attempt.
    private static readonly int[] RetryDelaysMs = { 500, 1500, 3000 };

    /// <summary>
    /// Fetches a URL, retrying with backoff on transient failures (HTTP 429/5xx, timeouts,
    /// network errors). Returns null on final failure so callers fall back to embedded data
    /// instead of throwing — an unhandled fetch failure at startup left the plugin with no
    /// voice-name mappings and surfaced a user-facing error popup.
    /// </summary>
    private string? FetchUrl(string url, EKEventId eventId)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return _http.GetStringAsync(url).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                var status = (ex as HttpRequestException)?.StatusCode
                             ?? (ex.InnerException as HttpRequestException)?.StatusCode;

                if (attempt < RetryDelaysMs.Length && IsTransient(status))
                {
                    _log.Warning(nameof(FetchUrl),
                        $"Transient fetch failure ({status?.ToString() ?? ex.GetType().Name}) for {url}, retrying in {RetryDelaysMs[attempt]}ms (attempt {attempt + 1}/{RetryDelaysMs.Length})",
                        eventId);
                    Thread.Sleep(RetryDelaysMs[attempt]);
                    continue;
                }

                _log.Warning(nameof(FetchUrl), $"Failed to fetch {url}: {ex.Message}", eventId);
                return null;
            }
        }
    }

    /// <summary>
    /// A fetch failure is transient (worth retrying) when the server rate-limits us (429),
    /// is temporarily unavailable (5xx / 408), or the request failed at the network level
    /// (null status = DNS/connection/timeout).
    /// </summary>
    internal static bool IsTransient(HttpStatusCode? status)
    {
        if (status is null) return true;
        return status is HttpStatusCode.RequestTimeout          // 408
            or HttpStatusCode.TooManyRequests                   // 429
            or HttpStatusCode.InternalServerError               // 500
            or HttpStatusCode.BadGateway                        // 502
            or HttpStatusCode.ServiceUnavailable                // 503
            or HttpStatusCode.GatewayTimeout;                   // 504
    }

    public void Dispose() => _http.Dispose();
}
