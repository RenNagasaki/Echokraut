namespace Echokraut.DataClasses;

/// <summary>
/// Outcome of <c>IDatabaseService.RetokenizePlayerNames</c> — the one-shot cleanup that converts
/// legacy <c>voice_clips</c> rows carrying the literal player name back into
/// <c>-PlayerName-</c> / <c>-PlayerFirstName-</c> / <c>-PlayerLastName-</c> tokens.
/// Used for both the dry run and the applied run, so the preview and the result read alike.
/// </summary>
public sealed class PlayerNameRetokenizeResult
{
    /// <summary>Rows examined (those whose text mentions the player's first or last name).</summary>
    public int Scanned { get; set; }

    /// <summary>Rows whose text changed and that had no harvested twin — rewritten in place.</summary>
    public int Rewritten { get; set; }

    /// <summary>
    /// Rows that, once tokenised, turned out to be duplicates of an existing (harvested) row for
    /// the same character + base id. Their generation rows are moved onto the surviving row and
    /// the duplicate is deleted.
    /// </summary>
    public int MergedAndDeleted { get; set; }

    /// <summary>Generation rows re-pointed from a merged duplicate to the surviving row.</summary>
    public int GenerationsMoved { get; set; }

    /// <summary>
    /// Generation rows dropped because the surviving row already had one for the same
    /// (player_content_id, alias_gender). Both described the same audio file — the filename hash
    /// is identical for the tokenised and the substituted spelling — so nothing is lost.
    /// </summary>
    public int GenerationsDropped { get; set; }

    /// <summary>Up to a handful of before → after samples, for the report text.</summary>
    public System.Collections.Generic.List<string> Samples { get; } = new();

    public bool HasWork => Rewritten > 0 || MergedAndDeleted > 0;
}
