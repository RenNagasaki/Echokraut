using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Echokraut.Enums;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Core character identity. One row per unique (Name, Gender, Race).
/// Voice is shared across all contexts (dialogue, bubbles, chat).
/// </summary>
[Table("characters")]
public class CharacterEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; } = "";

    [Column("race")]
    public int Race { get; set; } // NpcRaces enum

    [Column("race_str")]
    public string RaceStr { get; set; } = "";

    [Column("gender")]
    public int Gender { get; set; } // Genders enum

    [Column("body_type")]
    public int BodyType { get; set; } = (int)Enums.BodyType.Adult;

    [Column("voice_key")]
    public string VoiceKey { get; set; } = ""; // BackendVoice string

    [Column("do_not_delete")]
    public bool DoNotDelete { get; set; }

    [Column("language")]
    public int Language { get; set; } = 1; // ClientLanguage enum (0=JP, 1=EN, 2=DE, 3=FR)

    [Column("object_kind")]
    public int ObjectKind { get; set; } // Dalamud ObjectKind enum

    // Navigation properties
    public List<CharacterContextEntity> Contexts { get; set; } = new();
    public List<CharacterInstanceEntity> Instances { get; set; } = new();
    public List<VoiceClipEntity> Encounters { get; set; } = new();
}
