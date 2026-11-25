using System;
using System.Collections.Generic;
using Echokraut.Helper.Functional.GameVoice;

namespace Echokraut.DataClasses;

/// <summary>
/// <c>&lt;save location&gt;/harvest/game_voice_unmatched_&lt;lang&gt;.json</c>, written by the batch index
/// (plan game-voice-reference §11.5): what the central <c>GameVoiceAliases.json</c> does not cover
/// yet. Read by the "Assign game voices" window. Text only — game text samples, no audio.
/// </summary>
public sealed class GameVoiceUnmatchedReport
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "";
    public DateTime CreatedUtc { get; set; }

    /// <summary>Tokens no NPC owns (no exact name, no alias, not disabled).</summary>
    public List<GameVoiceUnmatchedToken> Unmatched { get; set; } = new();

    /// <summary>Tokens whose name match hit several different full names (shared first name).</summary>
    public List<GameVoiceUnmatchedToken> Ambiguous { get; set; } = new();

    /// <summary>NPC names met live in this session that own no token (a missing alias, or no voice).</summary>
    public List<string> SeenWithoutToken { get; set; } = new();
}

/// <summary>One report entry: token, how much audio it has, what it says, who it might be.</summary>
public sealed class GameVoiceUnmatchedToken
{
    public string Token { get; set; } = "";
    public int Clips { get; set; }
    public List<string> Samples { get; set; } = new();
    public List<GameVoiceMatchCandidate> Suggestions { get; set; } = new();
}
