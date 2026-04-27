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

    /// <summary>
    /// 0 = normal generation tied to a real player (PlayerContentId).
    /// 1 = shareable male alias variant (uses generic name like "Adventurer" / "Abenteurer").
    /// 2 = shareable female alias variant (e.g. "Abenteurerin" / "Aventurière").
    /// Alias variants always have PlayerContentId = 0 — they're not bound to any specific player.
    /// </summary>
    [Column("alias_gender")]
    public int AliasGender { get; set; } = 0;

    // Navigation
    [ForeignKey(nameof(VoiceClipId))]
    public VoiceClipEntity? VoiceClip { get; set; }
}
