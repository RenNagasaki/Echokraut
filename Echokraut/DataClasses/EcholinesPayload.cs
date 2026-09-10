using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Echokraut.DataClasses;

/// <summary>
/// One reported dialogue line, exactly as the Echolines API expects it
/// (see the service's README → "POST /v1/lines").
///
/// <para><b>What is deliberately absent:</b> no character name, no world, no content id, no play
/// time, no paths, no hardware id. <see cref="Text"/> is tokenised before it is put here — the
/// player's name is already a placeholder.</para>
/// </summary>
public sealed class EcholinesLine
{
    /// <summary>Two-letter language code (<c>en</c>/<c>de</c>/<c>fr</c>/<c>ja</c>).</summary>
    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "";

    /// <summary>
    /// Speaker's display name. <b>Empty is valid and expected</b> for narrator/system lines — the
    /// server folds an empty speaker, <c>SYSTEM</c> and the localised narrator names onto one key.
    /// </summary>
    [JsonPropertyName("npc")]
    public string Npc { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";

    [JsonPropertyName("race")]
    public string Race { get; set; } = "";

    [JsonPropertyName("npcBaseId")]
    public uint NpcBaseId { get; set; }

    /// <summary>The line, with the player's name already replaced by a placeholder.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>
    /// The same slug the plugin uses for the WAV file name. The server recomputes it from
    /// <see cref="Text"/> and uses its own value as the key — ours is only compared, and a mismatch
    /// tells both sides they have drifted. Sending it is therefore useful even though it is not
    /// trusted.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    /// <summary>Which in-game source produced the line (<c>AddonTalk</c>, …). Informational.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// The one way to build a reportable line. Both paths that produce them — the live dialogue
    /// hook and the harvest bulk import — go through here, because both would otherwise compute
    /// <see cref="Hash"/> themselves. That hash IS the line's identity: if the two ever derived it
    /// differently, the same line would arrive under two keys and neither copy would collect the
    /// votes it needs.
    /// </summary>
    /// <param name="tokenisedText">Text with the player's name ALREADY replaced. Hashing an
    /// untokenised line would put a character name into the key.</param>
    public static EcholinesLine Create(string lang, string npc, string gender, string race,
        uint npcBaseId, string tokenisedText, string source) => new()
    {
        Lang = lang,
        Npc = npc,
        Gender = gender,
        Race = race,
        NpcBaseId = npcBaseId,
        Text = tokenisedText,
        Hash = Helper.Functional.TalkTextHelper.VoiceMessageToFileName(tokenisedText),
        Source = source,
    };
}

/// <summary>
/// Request body for both endpoints. <see cref="Chunk"/>/<see cref="ChunkCount"/> are only filled for
/// the harvest import.
/// </summary>
public sealed class EcholinesRequest
{
    [JsonPropertyName("installId")]
    public string InstallId { get; set; } = "";

    [JsonPropertyName("pluginVersion")]
    public string PluginVersion { get; set; } = "";

    /// <summary>
    /// <c>live</c> or <c>harvest</c>. <b>The server ignores this and trusts the endpoint instead</b>
    /// (a client claiming "harvest" on the live path would corrupt the one distinction its insights
    /// rest on). Sent for documentation and debugging.
    /// </summary>
    [JsonPropertyName("origin")]
    public string Origin { get; set; } = "";

    /// <summary>Import only: index of this chunk, for the client's own resumability.</summary>
    [JsonPropertyName("chunk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Chunk { get; set; }

    /// <summary>Import only: total number of chunks in this language's submission.</summary>
    [JsonPropertyName("chunkCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ChunkCount { get; set; }

    [JsonPropertyName("lines")]
    public List<EcholinesLine> Lines { get; set; } = new();
}
