using System.Collections.Generic;

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
    public Dictionary<string, string> Texts { get; set; } = new();
}

public class UnmatchedQuestDialog
{
    public uint QuestId { get; set; }
    public string QuestName { get; set; } = "";
    public string NpcNameKey { get; set; } = "";
    public Dictionary<string, string> Texts { get; set; } = new();
}
