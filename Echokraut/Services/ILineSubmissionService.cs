using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services;

/// <summary>
/// Reports dialogue lines to the Echolines community database so lines missing from the plugin's own
/// data can be found and added.
///
/// <para><b>Everything about this is quiet.</b> No UI while it runs, no progress, no error in game:
/// a failed report is worth nothing to the user and interrupting them for it would be a bug, not a
/// feature. Failures are logged at Warning and retried with the next batch.</para>
///
/// <para><b>Never blocks the caller.</b> <see cref="Report"/> is called from the dialogue pipeline on
/// the frame thread and must stay a cheap enqueue; all I/O happens on a timer thread.</para>
/// </summary>
public interface ILineSubmissionService
{
    /// <summary>
    /// Offer one line for reporting. Cheap and safe to call for every line: it applies the opt-out,
    /// the source whitelist and the local "already sent" set, and drops anything that does not
    /// qualify. Never throws.
    /// </summary>
    /// <param name="npcBaseId">The speaker's game-data id, or 0 when there is no game object (a
    /// narrator line, or a speaker that could not be resolved). The server stores it as an extra
    /// handle on the line; it says nothing about the player.</param>
    void Report(NpcMapData speaker, uint npcBaseId, string originalText, TextSource source,
        ClientLanguage language);

    /// <summary>
    /// Send whatever is buffered now instead of waiting for the timer. Used on shutdown so a session's
    /// last lines are not lost. Returns once the attempt is over; failures are swallowed.
    /// </summary>
    void FlushNow();

    /// <summary>
    /// Submit a completed harvest's entire result for one language to the bulk endpoint.
    ///
    /// <para>Chunked, paced and <b>resumable</b>: a language is ~288 000 lines, so this runs for
    /// roughly an hour and must survive being interrupted. Progress is kept per language in
    /// <c>Configuration.EcholinesImportProgress</c> — a player who plays in two languages triggers
    /// two independent submissions, and the server counts consensus per language anyway.</para>
    ///
    /// <para>Deliberately slow. The server's import budget is a few requests per minute: a harvest
    /// is meant to take hours, not minutes, and hammering it only earns 429s.</para>
    ///
    /// <para>Silent like the live path: no UI, no error in game. Returns when done, cancelled, or
    /// when a send failed — in the last two cases the progress is kept so the next run continues.</para>
    /// </summary>
    Task SubmitHarvestAsync(ClientLanguage language, CancellationToken ct);
}
