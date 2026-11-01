using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Per-context settings for a character.
/// context_type: "npc" (dialogue/battletalk), "player" (chat/selectstring), "bubble"
/// Allows muting bubbles independently from dialogue, or different volumes per context.
/// </summary>
[Table("character_contexts")]
public class CharacterContextEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("character_id")]
    public int CharacterId { get; set; }

    [Required]
    [Column("context_type")]
    public string ContextType { get; set; } = "npc"; // "npc", "player", "bubble"

    [Column("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [Column("volume")]
    public float Volume { get; set; } = 1.0f;

    // Navigation
    [ForeignKey(nameof(CharacterId))]
    public CharacterEntity? Character { get; set; }
}
