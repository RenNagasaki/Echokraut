using Echotools.Logging.Services;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Echokraut.Services;

/// <summary>
/// Loads NPC metadata and voice-name mappings from the GitHub JSON resources.
/// </summary>
public class JsonDataService : IJsonDataService, IDisposable
{
    private readonly ILogService _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly string RacePath =
        "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcRaces.json";
    private static readonly string GendersPath =
        "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/NpcGenders.json";
    private static readonly string EmoticonPath =
        "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/Emoticons.json";
    private static readonly Dictionary<ClientLanguage, string> VoiceNameUrls = new()
    {
        [ClientLanguage.German]  = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesDE.json",
        [ClientLanguage.English] = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesEN.json",
        [ClientLanguage.French]  = "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesFR.json",
        [ClientLanguage.Japanese]= "https://raw.githubusercontent.com/RenNagasaki/Echokraut/master/Echokraut/Resources/VoiceNamesJA.json",
    };

    public Dictionary<int, NpcRaces> ModelsToRaceMap { get; private set; } = new();
    public List<NpcGenderRaceMap> ModelGenderMap { get; private set; } = new();
    public List<string> Emoticons { get; private set; } = new();
    private List<VoiceMap> _voiceMaps = new();

    public JsonDataService(ILogService log, ClientLanguage language)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
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
            var json = FetchUrl(RacePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Error(nameof(LoadModelsToRaceMap), "Failed to load NPC race maps.", eventId);
                return;
            }
            ModelsToRaceMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, NpcRaces>>(json) ?? new();
            _log.Info(nameof(LoadModelsToRaceMap), $"Loaded NPC race maps: {ModelsToRaceMap.Count} entries", eventId);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(LoadModelsToRaceMap), $"Error loading NPC race maps: {ex}", eventId);
        }
    }

    private void LoadModelsToGenderMap(EKEventId eventId)
    {
        try
        {
            var json = FetchUrl(GendersPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Error(nameof(LoadModelsToGenderMap), "Failed to load NPC gender maps.", eventId);
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
            var json = FetchUrl(EmoticonPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Error(nameof(LoadEmoticons), "Failed to load emoticons.", eventId);
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
        if (!VoiceNameUrls.TryGetValue(language, out var url))
            url = VoiceNameUrls[ClientLanguage.English];

        try
        {
            var json = FetchUrl(url);
            if (string.IsNullOrWhiteSpace(json))
            {
                _log.Error(nameof(LoadVoiceNames), "Failed to load voice name maps.", eventId);
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

    private string FetchUrl(string url) => _http.GetStringAsync(url).GetAwaiter().GetResult();

    public void Dispose() => _http.Dispose();
}
