using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Cache of Lodestone (Name + World) lookups so we don't re-fetch on every plugin load.
/// </summary>
[Table("lodestone_lookups")]
[PrimaryKey(nameof(Name), nameof(World))]
public class LodestoneLookupEntity
{
    [Column("name")]
    public string Name { get; set; } = "";

    /// <summary>English world name (resolved via World sheet).</summary>
    [Column("world")]
    public string World { get; set; } = "";

    [Column("race")]
    public int Race { get; set; } // NpcRaces enum

    [Column("gender")]
    public int Gender { get; set; } // Genders enum

    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }

    [Column("found")]
    public bool Found { get; set; }
}
