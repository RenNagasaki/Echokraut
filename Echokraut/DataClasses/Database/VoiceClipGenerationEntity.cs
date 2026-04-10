using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Tracks per-player TTS generation for a voice clip.
/// For clips with player name placeholders, each player character gets a separate generation.
/// For clips without placeholders, PlayerContentId = 0 (player-independent).
/// </summary>
[Table("voice_clip_generations")]
public class VoiceClipGenerationEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("voice_clip_id")]
    public int VoiceClipId { get; set; }

    /// <summary>
    /// IClientState.LocalContentId cast to long (SQLite has no ulong).
    /// 0 = player-independent (clip has no player name placeholders).
    /// </summary>
    [Column("player_content_id")]
    public long PlayerContentId { get; set; }

    /// <summary>
    /// Player name at generation time ("Firstname Lastname").
    /// Stored for display and to detect renames.
    /// </summary>
    [Column("player_name")]
    public string PlayerName { get; set; } = "";

    [Column("save_path")]
    public string SavePath { get; set; } = "";

    [Column("generated_at")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(VoiceClipId))]
    public VoiceClipEntity? VoiceClip { get; set; }
}
