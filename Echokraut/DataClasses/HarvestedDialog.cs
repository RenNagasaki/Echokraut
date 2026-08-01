using System.Collections.Generic;
using Echokraut.Enums;

namespace Echokraut.DataClasses;

/// <summary>
/// How a dialog entry was matched to an NPC.
/// </summary>
public enum DialogMatchSource
{
    /// <summary>Direct ENpcData reference from ENpcBase.</summary>
    Direct,
    /// <summary>Lua script bytecode analysis (Talk() call → ACTOR mapping).</summary>
    LuaScript,
    /// <summary>Exact NPC name match.</summary>
    NameExact,
    /// <summary>NPC name starts with the quest key (titled NPCs).</summary>
    NameStartsWith,
    /// <summary>Quest key contains an NPC name substring.</summary>
    NameKeyContainsNpc,
    /// <summary>NPC name contains the quest key substring.</summary>
    NameNpcContainsKey,
    /// <summary>Levenshtein fuzzy match (spelling variant).</summary>
    NameFuzzy,
    /// <summary>Matched to a BNpc (battle NPC) — race/gender unavailable from sheet data.</summary>
    BNpc,
    /// <summary>SYSTEM/narrator entry — not a real NPC.</summary>
    Narrator,
    /// <summary>Silent-actor heuristic for paren-prefix dialogs ((-???-) or (-Name-)): the
    /// only ACTOR in the same Lua scene that hadn't spoken before but does speak after.</summary>
    SilentActorHeuristic,
    /// <summary>User-supplied per-quest alias from quest_npc_aliases.json.</summary>
    UserAlias,
}

public class LinkedDialog
{
    public uint NpcId { get; set; }
    public Dictionary<string, string> NpcName { get; set; } = new();
    public string Race { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Sheet { get; set; } = "";
    public uint DialogId { get; set; }
    public string MatchSource { get; set; } = "";
    public QuestType QuestType { get; set; }
    public Dictionary<string, string> Texts { get; set; } = new();
}

public class UnmatchedDialog
{
    public string Sheet { get; set; } = "";
    public uint DialogId { get; set; }
    public Dictionary<string, string> Texts { get; set; } = new();
}

public class LinkedQuestDialog
{
    public uint QuestId { get; set; }
    public string QuestName { get; set; } = "";
    public string NpcNameKey { get; set; } = "";
    public uint NpcId { get; set; }
    public Dictionary<string, string> NpcName { get; set; } = new();
    public string Race { get; set; } = "";
    public string Gender { get; set; } = "";
    public string MatchSource { get; set; } = "";
    public QuestType QuestType { get; set; }
    public Dictionary<string, string> Texts { get; set; } = new();
}

public class UnmatchedQuestDialog
{
    public uint QuestId { get; set; }
    public string QuestName { get; set; } = "";
    public string NpcNameKey { get; set; } = "";
    public Dictionary<string, string> Texts { get; set; } = new();
}

/// <summary>
/// Emitted by the harvester for paren-prefix dialogs (text starts with <c>(-...-)</c>) that
/// stayed unmatched after all match priorities ran. The user resolves these manually by
/// copying the entry into <c>quest_npc_aliases.json</c> with the chosen <c>npcId</c>/<c>npcName</c>.
/// <para>
/// <see cref="Candidates"/> may be empty (heuristic found 0 — user picks freely from
/// <see cref="AllActors"/>) or contain ≥2 entries (heuristic narrowed to multiple). Single-
/// candidate cases are auto-attributed and never reach this list.
/// </para>
/// <para>
/// <see cref="AllActors"/> contains every ACTOR0..N defined in the quest's <c>QuestParams</c>
/// — the full cutscene cast. Always available when the Lua script was loaded; the user can
/// pick any of them when the heuristic gave up.
/// </para>
/// </summary>
public class ParenCandidateEntry
{
    public uint QuestId { get; set; }
    public string QuestName { get; set; } = "";
    public string TextKey { get; set; } = "";
    public string NpcNameKey { get; set; } = "";
    /// <summary>Full multilingual text dict (en/de/fr/ja) — makes manual searching easy.</summary>
    public Dictionary<string, string> Texts { get; set; } = new();
    /// <summary>Heuristic-narrowed candidates (silent-before, speaks-after). Subset of <see cref="AllActors"/>.</summary>
    public List<ParenCandidateOption> Candidates { get; set; } = new();
    /// <summary>Every ACTOR in the quest's cutscene cast. Pick from here when <see cref="Candidates"/> is empty.</summary>
    public List<ParenCandidateOption> AllActors { get; set; } = new();
    /// <summary>Up to one preceding line and three following lines from the same Lua scene,
    /// each with their own resolved speaker (when known). Helps the user identify the speaker
    /// by surrounding context.</summary>
    public List<ParenContextEntry> Context { get; set; } = new();
}

public class ParenCandidateOption
{
    public uint NpcId { get; set; }
    public Dictionary<string, string> Names { get; set; } = new();
}

/// <summary>
/// One surrounding-dialog line emitted with a paren-prefix candidate entry. <see cref="Position"/>
/// is e.g. <c>"before"</c> or <c>"after+2"</c>. <see cref="NpcId"/> is 0 when the surrounding line
/// itself wasn't resolved (typical for unresolved paren-prefix neighbours); otherwise it's the
/// NPC whose voice spoke that line.
/// </summary>
public class ParenContextEntry
{
    public string Position { get; set; } = "";
    public string TextKey { get; set; } = "";
    public Dictionary<string, string> Texts { get; set; } = new();
    public uint NpcId { get; set; }
    public Dictionary<string, string> NpcNames { get; set; } = new();
}

/// <summary>
/// Schema for the user-edited <c>quest_npc_aliases.json</c> file. Each entry maps a
/// (QuestId, NpcNameKey) pair to a specific NPC, either by <c>NpcName</c> (resolved
/// against the harvester's cross-language name lookup with normalization) or by
/// <c>NpcId</c> directly when the name is ambiguous. <c>NpcId</c> wins when both are set.
/// </summary>
public class QuestNpcAliasFile
{
    public int Version { get; set; } = 1;
    public List<QuestNpcAliasEntry> Aliases { get; set; } = new();
}

public class QuestNpcAliasEntry
{
    public uint QuestId { get; set; }
    public string? QuestName { get; set; }
    public string NpcNameKey { get; set; } = "";
    public string? NpcName { get; set; }
    public uint? NpcId { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// One entry in the per-language <c>voice_name_suggestions_&lt;lang&gt;.json</c> harvest
/// output. Same shape as <see cref="VoiceMap"/> in <c>VoiceNames{LANG}.json</c> so users
/// can copy entries directly into the canonical voice-name file.
///
/// Built by the harvester from dialog lines that start with the FFXIV speaker hint
/// <c>(-Fakename-)</c> AND have a resolved NPC. <see cref="speakers"/> collects every
/// distinct fakename observed for the same NPC in that language; <see cref="voiceName"/>
/// is the NPC's name in that language. The intent is to make filling the
/// <c>VoiceNames*.json</c> files much faster — the harvest discovers new aliases
/// automatically, the user reviews and merges.
///
/// Field names use the lowercase form (<c>voiceName</c>/<c>speakers</c>) to match
/// <see cref="VoiceMap"/> exactly so JSON deserialization-on-merge works without
/// renaming. SonarQube will flag the casing — that's acceptable for this DTO.
/// </summary>
public class VoiceNameSuggestion
{
#pragma warning disable IDE1006 // Name matches the JSON field name in VoiceNames*.json
    public string voiceName { get; set; } = "";
    public List<string> speakers { get; set; } = new();
#pragma warning restore IDE1006
}

/// <summary>
/// One entry in the per-language <c>voice_name_collisions_&lt;lang&gt;.json</c> harvest
/// output. Captures fakenames that LOOK suspicious — the fakename text contains another
/// known NPC's name as a substring AND that other NPC isn't who the harvest attributed
/// the line to. Typical case: <c>(-Kriles Stimme-)</c> attributed to Alisaie instead of
/// Krile, because FFXIV references the same DefaultTalk row from multiple ENpcBase rows.
///
/// User reviews this file manually — entries with a single obvious correct NPC
/// (<see cref="LikelyMeantFor"/>) can be re-attributed by adding a quest_npc_alias
/// override; the rest are spurious overlaps and can be ignored.
/// </summary>
public class VoiceNameCollision
{
    public string Fakename { get; set; } = "";
    public string ResolvedAs { get; set; } = "";
    public List<string> LikelyMeantFor { get; set; } = new();
}
