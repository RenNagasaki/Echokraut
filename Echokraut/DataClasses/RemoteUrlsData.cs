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

    /// <summary>Expected EchokrautLocalInstaller release tag. When it differs from
    /// <c>Configuration.InstalledInstallerVersion</c>, the cached installer is re-downloaded even if
    /// the exe already exists — so users get a newer installer that understands new arg modes
    /// (BLK-5). Empty disables the version check (download only when the exe is missing).</summary>
    [JsonPropertyName("installerVersion")]
    public string InstallerVersion { get; set; } = string.Empty;

    /// <summary>Download URL for the EchokrauTTS wrapper zip (extracted to {root}\echokrautts).</summary>
    [JsonPropertyName("echokrauTtsUrl")]
    public string EchokrauTtsUrl { get; set; } = string.Empty;

    /// <summary>
    /// GitHub API endpoint for the wrapper's latest release. Queried ONLY when the user presses
    /// "Check for updates" — unauthenticated GitHub API calls are capped at 60/h per user IP, so it
    /// must never run on a timer or at startup. The answer supplies both the tag and the zip URL, so
    /// publishing a release is enough; <see cref="EchokrauTtsUrl"/> / <see cref="EchokrauTtsVersion"/>
    /// stay as the shipped baseline for fresh installs. Empty disables the check.
    /// </summary>
    [JsonPropertyName("echokrauTtsReleasesUrl")]
    public string EchokrauTtsReleasesUrl { get; set; } = string.Empty;

    /// <summary>Git release tag of the wrapper zip behind <see cref="EchokrauTtsUrl"/>. Compared
    /// against <c>EchokrauTtsData.InstalledWrapperVersion</c> to offer the in-UI Update button.
    /// Bump this together with the URL on every wrapper release — empty disables the update offer
    /// entirely (users then only get a new wrapper via Reinstall).</summary>
    [JsonPropertyName("echokrauTtsVersion")]
    public string EchokrauTtsVersion { get; set; } = string.Empty;

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
    /// Download URL for the curated voice pack zip (freely-licensed reference voices, see
    /// <c>IVoicePackService</c>). Unpacked into the active engine's voice folder on a fresh
    /// install and on demand from the Game Data Tools window. Empty = no pack published yet;
    /// the download is then skipped with a warning instead of failing the install.
    /// </summary>
    [JsonPropertyName("voicePackUrl")]
    public string VoicePackUrl { get; set; } = string.Empty;
}
