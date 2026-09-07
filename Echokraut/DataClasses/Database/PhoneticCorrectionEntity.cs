using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Echokraut.DataClasses.Database;

/// <summary>
/// Text replacement rule for TTS pronunciation accuracy.
/// </summary>
[Table("phonetic_corrections")]
public class PhoneticCorrectionEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("original_text")]
    public string OriginalText { get; set; } = "";

    [Column("corrected_text")]
    public string CorrectedText { get; set; } = "";
}
