using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Log of every dialog encountered during gameplay.
/// References character for NPC identity, stores per-encounter specifics.
/// </summary>
[Table("dialog_encounters")]
public class DialogEncounterEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("character_id")]
    public int? CharacterId { get; set; }

    [Column("npc_base_id")]
    public long NpcBaseId { get; set; } // ENpcBase row ID

    [Column("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column("text_source")]
    public int TextSource { get; set; } // TextSource enum

    [Column("language")]
    public int Language { get; set; } // ClientLanguage enum

    [Column("voice_key")]
    public string VoiceKey { get; set; } = "";

    [Column("original_text")]
    public string OriginalText { get; set; } = "";

    [Column("cleaned_text")]
    public string CleanedText { get; set; } = "";

    [Column("saved_to_disk")]
    public bool SavedToDisk { get; set; }

    [Column("body_type")]
    public int BodyType { get; set; } // BodyType enum

    // Navigation
    [ForeignKey(nameof(CharacterId))]
    public CharacterEntity? Character { get; set; }
}
