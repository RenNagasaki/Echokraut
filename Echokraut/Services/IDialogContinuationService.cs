using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;

namespace Echokraut.Services;

/// <summary>What <see cref="IDialogContinuationService.Apply"/> did to one dialogue box.</summary>
/// <param name="Text">Text to speak (may be empty when the whole box was the previous box's sentence end).</param>
/// <param name="AppendedHead">Rest of the sentence taken from the next box, null = nothing appended.</param>
/// <param name="StrippedHead">The box started with the sentence end the previous box already spoke.</param>
/// <param name="PagedRow">The whole sheet row (player name as placeholder) this box was found to be a
/// page of — the game split that row over several boxes. Null when the box is not a page of a longer
/// row or could not be matched. Reported to Echolines with the "paged" flag.</param>
public readonly record struct ContinuationResult(string Text, string? AppendedHead, bool StrippedHead, string? PagedRow = null)
{
    public bool Changed => AppendedHead != null || StrippedHead;
}

/// <summary>
/// Option <see cref="DataClasses.Configuration.JoinSplitSentences"/>: a Talk box that ends in the
/// middle of a sentence gets the rest of that sentence from the game sheet's next row, and the next
/// box is spoken without it. Only <see cref="TextSource.AddonTalk"/>; every other source passes through.
/// </summary>
public interface IDialogContinuationService
{
    /// <summary>Builds the lookup for <paramref name="language"/> in the background (once). Until it is done boxes pass through.</summary>
    Task Prepare(ClientLanguage language);

    /// <summary>
    /// Must be called once per box in display order (it remembers what it appended to strip it from the
    /// next box). Synchronous and cheap; the caller passes the raw box text before any cleaning.
    /// </summary>
    ContinuationResult Apply(string text, TextSource source, ClientLanguage language);

    /// <summary>Boxes of <paramref name="language"/> that end mid-sentence and have a known end; null = index not built yet (self-test).</summary>
    int? JoinableBoxCount(ClientLanguage language);

    /// <summary>Two consecutive joinable boxes without placeholders (E2E continuation scenario); null = index not built or none.</summary>
    ContinuationPair? SamplePair(ClientLanguage language);
}
