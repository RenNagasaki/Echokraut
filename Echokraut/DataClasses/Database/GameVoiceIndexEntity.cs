using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// One ranked game-voice reference clip of a <b>speaker token</b> (plan
/// <c>docs/plans/game-voice-reference.md</c> §11.1, schema v8). Keyed by token, not by character:
/// the index survives a mapping reset and serves NPCs that were never met. Stores the clip's
/// <b>id</b> (its audio base, e.g. <c>vo_voiceman_06100_000010</c>) and how it was ranked — never
/// audio bytes and nothing from which audio could be rebuilt (no speaker vectors either).
/// <para>A row with <see cref="Rank"/> 0 and an empty <see cref="AudioBase"/> means "ranked, nothing
/// usable" — so a resumed batch does not rank that token again.</para>
/// </summary>
[Table("game_voice_index")]
public class GameVoiceIndexEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("language")]
    public int Language { get; set; } // ClientLanguage enum

    /// <summary>Normalized speaker token (<c>VoiceExtractKey.Normalize</c>), e.g. <c>yshtola</c>.</summary>
    [Column("token")]
    public string Token { get; set; } = "";

    /// <summary>Voice-actor epoch (<c>VoiceActorSplits</c> name); empty without a split.</summary>
    [Column("epoch")]
    public string Epoch { get; set; } = "";

    /// <summary>Clip id = audio base of the voice line; empty on the "nothing usable" row.</summary>
    [Column("audio_base")]
    public string AudioBase { get; set; } = "";

    /// <summary>1 = best, at most three per (language, token, epoch); 0 = "nothing usable" marker.</summary>
    [Column("rank")]
    public int Rank { get; set; }

    [Column("score")]
    public double Score { get; set; }

    /// <summary><c>GameVoiceRanking.IndexVersion</c> at ranking time; a mismatch means "rank again".</summary>
    [Column("index_version")]
    public int IndexVersion { get; set; }

    /// <summary>
    /// Hash of the epoch's candidate lines (audio bases + texts) at ranking time. A game patch that
    /// adds, removes or rewords a line of this token changes it → only that token re-ranks (§11.2).
    /// </summary>
    [Column("lines_hash")]
    public string LinesHash { get; set; } = "";

    [Column("created_utc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
