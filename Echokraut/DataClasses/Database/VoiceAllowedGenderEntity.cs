using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Junction table: which genders a voice is allowed for.
/// </summary>
[Table("voice_allowed_genders")]
[PrimaryKey(nameof(VoiceId), nameof(Gender))]
public class VoiceAllowedGenderEntity
{
    [Column("voice_id")]
    public int VoiceId { get; set; }

    [Column("gender")]
    public int Gender { get; set; } // Genders enum

    // Navigation
    [ForeignKey(nameof(VoiceId))]
    public VoiceEntity? Voice { get; set; }
}
