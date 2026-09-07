using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// One per <c>(Character, Language, Alias)</c> row — captures the FFXIV speaker hint
/// <c>(-Fakename-)</c> the harvest discovered for each character. At runtime, the live
/// dialog path consults this table when the displayed speaker name doesn't match an
/// existing character row directly: e.g. dialog box shows "Mysterious Lady" → alias
/// row resolves it to character "Y'shtola Rhul".
///
/// Complement to the static <c>VoiceNames{LANG}.json</c> voice-family mappings — that
/// file stays community-curated for "voice X is shared across Y, Z, W". This table is
/// per-installation, harvest-populated, deterministic from game data.
///
/// Unique on <c>(character_id, language, alias)</c>. <see cref="Language"/> mirrors the
/// owning character's language so we can index alias→character cheaply per locale (the
/// dialog-box speaker name is always in the client language).
/// </summary>
[Table("character_speaker_aliases")]
public class CharacterSpeakerAliasEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("character_id")]
    public int CharacterId { get; set; }

    /// <summary>Same as <see cref="CharacterEntity.Language"/>. Stored explicitly so a
    /// single index <c>(language, alias)</c> serves the runtime lookup.</summary>
    [Column("language")]
    public int Language { get; set; }

    /// <summary>The fakename captured from <c>(-Fakename-)</c> in the harvested dialog
    /// text. Stored as-is (capitalization preserved); compared case-insensitively at
    /// lookup time via <c>EF.Functions.Collate(alias, "NOCASE")</c>.</summary>
    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [ForeignKey(nameof(CharacterId))]
    public CharacterEntity? Character { get; set; }
}
