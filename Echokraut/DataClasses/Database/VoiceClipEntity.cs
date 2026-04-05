using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Voice clip record — one per unique NPC dialog line encountered during gameplay.
/// </summary>
[Table("voice_clips")]
public class VoiceClipEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("character_id")]
    public int CharacterId { get; set; }

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

    [Column("save_path")]
    public string SavePath { get; set; } = "";

    [Column("zone_name")]
    public string ZoneName { get; set; } = "";

    [Column("map_x")]
    public float MapX { get; set; }

    [Column("map_y")]
    public float MapY { get; set; }

    // Navigation
    [ForeignKey(nameof(CharacterId))]
    public CharacterEntity? Character { get; set; }
}
