using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Echokraut.DataClasses;

public class RemoteUrlsData
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("alltalkUrl")]
    public string AlltalkUrl { get; set; } = string.Empty;

    [JsonPropertyName("installerUrl")]
    public string InstallerUrl { get; set; } = string.Empty;

    [JsonPropertyName("voicesUrl")]
    public string VoicesUrl { get; set; } = string.Empty;

    [JsonPropertyName("voices2Url")]
    public string Voices2Url { get; set; } = string.Empty;

    [JsonPropertyName("msBuildToolsUrl")]
    public string MsBuildToolsUrl { get; set; } = string.Empty;

    [JsonPropertyName("xttsModelUrls")]
    public string[] XttsModelUrls { get; set; } = [];

    [JsonPropertyName("npcRacesUrl")]
    public string NpcRacesUrl { get; set; } = string.Empty;

    [JsonPropertyName("npcGendersUrl")]
    public string NpcGendersUrl { get; set; } = string.Empty;

    [JsonPropertyName("emoticonsUrl")]
    public string EmoticonsUrl { get; set; } = string.Empty;

    [JsonPropertyName("voiceNameUrls")]
    public Dictionary<string, string> VoiceNameUrls { get; set; } = new();
}
