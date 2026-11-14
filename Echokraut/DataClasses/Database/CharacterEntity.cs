using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Echokraut.Enums;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Core character identity. One row per unique (Name, Gender, Race).
///
/// <para><b>Gender and Race are what the GAME reports</b>, not what the user thinks. They are part
/// of the lookup key and are re-derived from the game object on every line, so they can never be
/// edited: a row saved under a changed gender is one the lookup can no longer reach, and the next
/// line simply recreates the original row. That is what
/// <see cref="GenderOverride"/>/<see cref="RaceOverride"/> are for — they leave the key alone and
/// only steer voice selection.</para>
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

    [Column("language")]
    public int Language { get; set; } = 1; // ClientLanguage enum (0=JP, 1=EN, 2=DE, 3=FR)

    [Column("object_kind")]
    public int ObjectKind { get; set; } // Dalamud ObjectKind enum

    /// <summary>
    /// User's correction of <see cref="Gender"/> for voice purposes, or <c>null</c> when the game's
    /// value stands. Never part of any lookup key — see the class remarks.
    /// </summary>
    [Column("gender_override")]
    public int? GenderOverride { get; set; }

    /// <summary>User's correction of <see cref="Race"/>. Same rules as <see cref="GenderOverride"/>.</summary>
    [Column("race_override")]
    public int? RaceOverride { get; set; }

    /// <summary>FFXIV world (server) name in English (resolved via World sheet). Empty for non-Player characters.</summary>
    [Column("world")]
    public string World { get; set; } = "";

    // Navigation properties
    public List<CharacterContextEntity> Contexts { get; set; } = new();
    public List<CharacterInstanceEntity> Instances { get; set; } = new();
    public List<VoiceClipEntity> VoiceClips { get; set; } = new();
}
