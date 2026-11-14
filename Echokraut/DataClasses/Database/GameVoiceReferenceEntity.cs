using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// One ranked game-voice reference clip of a character (plan <c>docs/plans/game-voice-reference.md</c>
/// §2.6). Stores the clip's <b>id</b> (its audio base, e.g. <c>vo_voiceman_06006_000010</c>) and
/// how it was ranked — never audio bytes and nothing from which audio could be rebuilt. The PCM is
/// decoded from the user's own game files in memory every time it is needed.
/// </summary>
[Table("game_voice_references")]
public class GameVoiceReferenceEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("character_id")]
    public int CharacterId { get; set; }

    [Column("language")]
    public int Language { get; set; } // ClientLanguage enum

    /// <summary>Voice-actor epoch the clip belongs to (<c>VoiceActorSplits</c>); empty without a split.</summary>
    [Column("epoch")]
    public string Epoch { get; set; } = "";

    /// <summary>Clip id = audio base of the voice line (lowercase, no language/gender suffix).</summary>
    [Column("audio_base")]
    public string AudioBase { get; set; } = "";

    /// <summary>1 = best. At most three rows per (character, language).</summary>
    [Column("rank")]
    public int Rank { get; set; }

    [Column("score")]
    public double Score { get; set; }

    /// <summary><c>GameVoiceRanking.IndexVersion</c> at ranking time; a mismatch means "rank again".</summary>
    [Column("index_version")]
    public int IndexVersion { get; set; }

    [Column("created_utc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CharacterId))]
    public CharacterEntity? Character { get; set; }
}
