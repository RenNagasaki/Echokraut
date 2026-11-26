using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Junction table: which races a voice is allowed for.
/// </summary>
[Table("voice_allowed_races")]
[PrimaryKey(nameof(VoiceId), nameof(Race))]
public class VoiceAllowedRaceEntity
{
    [Column("voice_id")]
    public int VoiceId { get; set; }

    [Column("race")]
    public int Race { get; set; } // NpcRaces enum

    // Navigation
    [ForeignKey(nameof(VoiceId))]
    public VoiceEntity? Voice { get; set; }
}
