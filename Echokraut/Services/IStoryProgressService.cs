using System;
using Dalamud.Game;
using Echokraut.Helper.Functional.GameVoice;

namespace Echokraut.Services;

/// <summary>
/// The story patch a line belongs to, for choosing a voice-actor epoch (plan game-voice-reference
/// §11.4). Game state is read on the framework thread and published as snapshots every other
/// thread may read.
/// </summary>
public interface IStoryProgressService : IDisposable
{
    /// <summary>
    /// Highest story patch the player completed (<c>T_VER610</c> …), <c>""</c> = before the first
    /// voiced patch, null = unknown (not logged in, sheet missing).
    /// </summary>
    string? ReachedPatchKey { get; }

    /// <summary>
    /// The story patch of one line: the accepted quest whose text it is, else any quest whose text it
    /// is (talk before accepting), else the accepted
    /// main-scenario quest (New Game+ replays), else <see cref="ReachedPatchKey"/>. Null = unknown
    /// → callers use the newest epoch. Safe on any thread.
    /// </summary>
    StoryContext? ContextFor(string? lineText, ClientLanguage language);

    /// <summary>
    /// Loads the dialogue texts of every quest for <paramref name="language"/> in the
    /// background (once), so a line spoken BEFORE its quest is accepted (sequence 0, New Game+
    /// chapter start) still finds its quest. Until then <see cref="ContextFor"/> skips that step.
    /// </summary>
    void Prepare(ClientLanguage language);

    /// <summary>
    /// "Does story patch <paramref name="patchKey"/> lie at or after boundary token
    /// <paramref name="boundaryToken"/> (<c>06100</c>)?" A token the table cannot place counts as not reached.
    /// </summary>
    bool IsReached(string boundaryToken, string patchKey);

    /// <summary>Raised (any thread) when <see cref="ReachedPatchKey"/> changed.</summary>
    event Action? ProgressChanged;
}
