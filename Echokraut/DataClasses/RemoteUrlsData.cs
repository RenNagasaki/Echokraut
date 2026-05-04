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

    /// <summary>
    /// Optional URL for the community-curated quest NPC alias mapping (paren-prefix dialogs).
    /// Loaded by <c>DialogHarvestService</c> with the embedded <c>QuestNpcAliases.json</c> as
    /// fallback. Per-user overrides live in <c>&lt;localSaveLocation&gt;/harvest/quest_npc_aliases.json</c>
    /// and stack on top of remote+embedded.
    /// </summary>
    [JsonPropertyName("questNpcAliasesUrl")]
    public string QuestNpcAliasesUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL for the community-curated voice-extract shortname alias mapping. Loaded
    /// by <c>VoiceSampleExtractorService</c> with the embedded <c>VoiceExtractAliases.json</c>
    /// as fallback. Per-user overrides live in
    /// <c>&lt;localSaveLocation&gt;/FF14-Voices/voice_extract_aliases.json</c> (sibling of
    /// <c>voice_extract_unmatched.json</c> so users can copy entries from one to the other)
    /// and stack on top of remote+embedded.
    /// </summary>
    [JsonPropertyName("voiceExtractAliasesUrl")]
    public string VoiceExtractAliasesUrl { get; set; } = string.Empty;
}
