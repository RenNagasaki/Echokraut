using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Voice definition — one row per unique BackendVoice identifier.
/// </summary>
[Table("voices")]
public class VoiceEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("backend_voice")]
    public string BackendVoice { get; set; } = "";

    [Column("voice_name")]
    public string VoiceName { get; set; } = "";

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [Column("use_as_random")]
    public bool UseAsRandom { get; set; }

    [Column("is_adult_voice")]
    public bool IsAdultVoice { get; set; } = true;

    [Column("is_child_voice")]
    public bool IsChildVoice { get; set; }

    [Column("is_elder_voice")]
    public bool IsElderVoice { get; set; }

    [Column("volume")]
    public float Volume { get; set; } = 1.0f;

    [Column("note")]
    public string Note { get; set; } = "";

    // Navigation
    public List<VoiceAllowedGenderEntity> AllowedGenders { get; set; } = new();
    public List<VoiceAllowedRaceEntity> AllowedRaces { get; set; } = new();
}
