using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// Detects the language of chat text via the DetectLanguage.com API.
/// Falls back to the client's configured game language when the API key is absent or a request fails.
/// </summary>
public class LanguageDetectionService : ILanguageDetectionService, IDisposable
{
    private readonly Configuration _config;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly HttpClient _httpClient;

    public LanguageDetectionService(Configuration config, IClientState clientState, ILogService log)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<ClientLanguage> GetTextLanguage(string text, EKEventId eventId)
    {
        if (_config.VoiceChatLanguageAPIKey.Length != 32)
        {
            _log.Info(nameof(GetTextLanguage), "Skipping language detection for chat. Using client language.", eventId);
            return _clientState.ClientLanguage;
        }

        try
        {
            var uriBuilder = new UriBuilder(@"https://ws.detectlanguage.com/0.2/") { Path = "/0.2/detect" };
            var detectData = new Dictionary<string, string> { { "q", text } };
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = uriBuilder.Uri,
                Headers =
                {
                    { HttpRequestHeader.Authorization.ToString(), $"Bearer {_config.VoiceChatLanguageAPIKey}" },
                    { HttpRequestHeader.Accept.ToString(), "application/json" }
                },
                Content = new FormUrlEncodedContent(detectData)
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            var jsonResult = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            dynamic resultObj = JObject.Parse(jsonResult);

            string languageString = resultObj.data.detections.Count > 0
                ? (string)resultObj.data.detections[0].language
                : "en";

            var language = languageString switch
            {
                "de" => ClientLanguage.German,
                "ja" => ClientLanguage.Japanese,
                "fr" => ClientLanguage.French,
                _ => ClientLanguage.English
            };

            _log.Debug(nameof(GetTextLanguage), $"Found language for chat: {languageString}/{language}", eventId);
            return language;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(GetTextLanguage), $"Error while detecting language. Using client language. Exception: {ex}", eventId);
            return _clientState.ClientLanguage;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
