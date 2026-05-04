using System.Collections.Generic;

namespace Echokraut.DataClasses;

/// <summary>
/// Schema for the layered short-name → NPC override files used by the voice sample extractor.
///
/// FFXIV's voice text keys (<c>TEXT_VOICEMAN_06006_000010_YSHTOLA</c>) trail with a speaker
/// "shortname" that the extractor matches against the English ENpcResident name index. Some
/// shortnames don't match any English NPC name (legacy script identifiers, scenario codes,
/// quest-internal placeholders) and end up in <c>FF14-Voices/voice_extract_unmatched.json</c>.
/// This file lets users (and us) explicitly map those shortnames to NPC IDs so the next
/// extraction picks them up. Three layered sources are merged, later wins:
/// <list type="number">
/// <item>Embedded <c>Resources/VoiceExtractAliases.json</c> (always; ships with the plugin)</item>
/// <item>Remote <c>RemoteUrlsData.VoiceExtractAliasesUrl</c> (community-curated; non-fatal)</item>
/// <item>Local <c>&lt;localSaveLocation&gt;/FF14-Voices/voice_extract_aliases.json</c></item>
/// </list>
/// </summary>
public class VoiceExtractAliasFile
{
    public int Version { get; set; }
    public List<VoiceExtractAliasEntry> Aliases { get; set; } = new();
}

/// <summary>
/// One short-name → NPC mapping. Either <see cref="NpcId"/> (wins, must be &gt; 0) or
/// <see cref="NpcName"/> (resolved against the English NPC name index, case-insensitive
/// after stripping spaces/apostrophes/hyphens) is required. Unknown / ambiguous names are
/// logged and skipped, so be explicit when in doubt.
/// </summary>
public class VoiceExtractAliasEntry
{
    /// <summary>The trailing token of the voice text key (e.g. <c>"BUSCARRON"</c>). Case-insensitive.</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>Explicit ENpcResident row ID. Wins over <see cref="NpcName"/> when set and &gt; 0.</summary>
    public uint? NpcId { get; set; }

    /// <summary>English NPC name. Resolved via the canonical name index (same normalization
    /// as <c>VoiceExtractKey.Normalize</c>: lower, no spaces/apostrophes/hyphens).</summary>
    public string? NpcName { get; set; }

    /// <summary>Optional human-readable note. Ignored by the loader.</summary>
    public string? Comment { get; set; }
}
