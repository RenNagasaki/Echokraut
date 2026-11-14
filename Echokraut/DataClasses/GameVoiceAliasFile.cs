using System.Collections.Generic;

namespace Echokraut.DataClasses;

/// <summary>
/// <c>GameVoiceAliases.json</c> (plan game-voice-reference F2): maps speaker tokens of voice text
/// keys that match no English NPC name (script names, disguises, "???" scenes) onto the character
/// they really are, so that character may use those clips. Text only. Same format as the former
/// <c>VoiceExtractAliases.json</c> (commit <c>485eb43^</c>). Layered: the embedded file, then
/// <c>&lt;pluginConfigDir&gt;/game_voice_aliases.json</c> (local override, later entries win).
/// Tokens without an entry and without an exact name match are never used — the voice pack speaks.
/// </summary>
public class GameVoiceAliasFile
{
    public int Version { get; set; }
    public List<GameVoiceAliasEntry> Aliases { get; set; } = new();
}

/// <summary>One token → character mapping. <see cref="NpcId"/> (&gt; 0) wins over <see cref="NpcName"/>.</summary>
public class GameVoiceAliasEntry
{
    /// <summary>Trailing token of the voice text key (e.g. <c>"MYSTERYVOICE"</c>), case-insensitive.</summary>
    public string ShortName { get; set; } = string.Empty;

    /// <summary>ENpcResident row id of the character.</summary>
    public uint? NpcId { get; set; }

    /// <summary>English full name of the character (normalized like <c>VoiceExtractKey.Normalize</c>).</summary>
    public string? NpcName { get; set; }

    /// <summary>Free note, ignored.</summary>
    public string? Comment { get; set; }
}
