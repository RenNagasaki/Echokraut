using System;
using System.Collections.Generic;
using Echotools.Logging.Enums;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Pure rules for what the plugin reports to the Echolines community database, and how a line is
/// keyed. Separate from the service so the decisions are testable without a network or a game.
/// </summary>
public static class EcholinesRules
{
    /// <summary>
    /// The ONLY sources that may be reported — NPC speech, nothing the player wrote or chose.
    ///
    /// <para><b>Why a whitelist and not "everything except chat":</b> the report call sits at a point
    /// every source passes through. A blacklist would silently start sending any source added later,
    /// and the one thing that must never leave the machine is player-authored text. A new source has
    /// to be added here on purpose.</para>
    ///
    /// <para>Deliberately NOT included (user, 2026-09-07): <see cref="TextSource.AddonSelectString"/>
    /// and <see cref="TextSource.AddonCutsceneSelectString"/>. They are candidates, but their
    /// extraction is still buggy — and this is a SHARED database where a wrong line, once three
    /// installations have voted for it, becomes everyone's. Add them once they are trustworthy.</para>
    /// </summary>
    private static readonly HashSet<TextSource> ReportableSources =
    [
        TextSource.AddonTalk,
        TextSource.AddonBattleTalk,
        TextSource.AddonBubble,
    ];

    /// <summary>Whether a line from this source may be reported at all.</summary>
    public static bool IsReportableSource(TextSource source) => ReportableSources.Contains(source);

    /// <summary>
    /// Local "already sent" key. Scoped to language + speaker + line so the same line in two
    /// languages is still worth reporting once each (the server's consensus is per language).
    /// <para>This set means "this installation already sent it", <b>not</b> "we know this line" — it
    /// is unrelated to whether the line exists in the local database or has audio.</para>
    /// </summary>
    public static string SuppressionKey(string lang, string npc, string hash)
        => $"{lang}\u0000{npc}\u0000{hash}";

    /// <summary>
    /// Two-letter language code for the API. Reuses the SCD table on purpose: it already yields
    /// exactly <c>en</c>/<c>de</c>/<c>fr</c>/<c>ja</c>, and a second copy would be free to drift.
    /// </summary>
    public static string LanguageCode(Dalamud.Game.ClientLanguage language)
        => VoiceScdPaths.LanguageCodeForScd(language);

    /// <summary>Number of chunks a language's harvest is split into.</summary>
    public static int ChunkCount(int totalLines, int chunkSize)
    {
        if (totalLines <= 0 || chunkSize <= 0) return 0;
        return (totalLines + chunkSize - 1) / chunkSize;
    }

    /// <summary>
    /// Where a resumed import must start.
    ///
    /// <para>The stored position is only meaningful against the harvest it was taken from. If the
    /// harvest has since grown, shrunk or been wiped, resuming at a stale offset would walk past
    /// lines that were never sent — <b>silently</b>, because nothing downstream can tell a skipped
    /// chunk from a sent one. Starting over costs an hour of paced requests; skipping costs data,
    /// so the stale case restarts.</para>
    /// </summary>
    public static int ResumeChunk(int savedChunk, int chunkCount)
    {
        if (savedChunk <= 0 || chunkCount <= 0) return 0;
        return savedChunk >= chunkCount ? 0 : savedChunk;
    }

    /// <summary>
    /// True when the payload is worth sending at all. Guards the two cases that would poison the
    /// database rather than fill it: an empty line, and a line still carrying a real character name
    /// (the tokeniser returns the text unchanged while the player name is unknown — during loading,
    /// before the first login completes).
    /// </summary>
    public static bool IsSendableText(string? tokenisedText, string? playerName)
    {
        if (string.IsNullOrWhiteSpace(tokenisedText)) return false;
        if (string.IsNullOrWhiteSpace(playerName)) return true;

        // A first name alone is enough to identify a player, so check the parts too.
        foreach (var part in playerName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length > 2 && tokenisedText.Contains(part, StringComparison.Ordinal))
                return false;
        }

        return !tokenisedText.Contains(playerName, StringComparison.Ordinal);
    }
}
