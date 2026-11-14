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

    /// <summary>
    /// On-disk file name (without extension) the audio file lives under, e.g. the result of
    /// <c>AudioFileService.VoiceMessageToFileName(RemovePlayerNameInText(originalText))</c>.
    /// Empty for clips first encountered through the live runtime path — that path never
    /// needs the column because it already has the original text. Set by the legacy audio
    /// backfill in <c>MigrateFromConfig</c> for orphan files we discover on disk without a
    /// matching <c>voice_clips</c> row, so the live runtime can later upgrade the orphan to
    /// a fully-textualised row when the same dialog re-occurs in-game (lookup
    /// <c>(character_id, wav_file_name)</c> as the fallback after the text-based match misses).
    /// Indexed for that fallback path.
    /// </summary>
    [Column("wav_file_name")]
    public string WavFileName { get; set; } = "";

    [Column("has_player_placeholder")]
    public bool HasPlayerPlaceholder { get; set; }

    [Column("quest_type")]
    public int QuestType { get; set; } // QuestType enum

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
