using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Links characters to Dalamud ENpcBase IDs encountered in-game.
/// Also tracks session-scoped muting of specific NPC instances.
/// Composite PK: (CharacterId, NpcBaseId).
/// </summary>
[Table("character_instances")]
[PrimaryKey(nameof(CharacterId), nameof(NpcBaseId))]
public class CharacterInstanceEntity
{
    [Column("character_id")]
    public int CharacterId { get; set; }

    [Column("npc_base_id")]
    public long NpcBaseId { get; set; } // uint stored as long for SQLite compat

    [Column("first_seen")]
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

    [Column("is_muted")]
    public bool IsMuted { get; set; }

    // Navigation
    [ForeignKey(nameof(CharacterId))]
    public CharacterEntity? Character { get; set; }
}
