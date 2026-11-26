using System;
using System.Collections.Generic;
using Echokraut.DataClasses;
using EchoScd;

namespace Echokraut.Services;

/// <summary>
/// Central token → NPC mapping and voice-actor boundaries for the game voice (plan
/// game-voice-reference §11.5): <c>GameVoiceAliases.json</c> and <c>GameVoiceActorSplits.json</c>,
/// each layered embedded &lt; remote (<c>main</c>, via <c>RemoteUrls</c>) &lt; local file in the
/// plugin config directory. Maintained in the repo so every user gets the same matching.
/// </summary>
public interface IGameVoiceMappingService
{
    /// <summary>Merged alias entries (one per token, later layer wins).</summary>
    IReadOnlyList<GameVoiceAliasEntry> Aliases { get; }

    /// <summary>Merged voice-actor boundaries.</summary>
    VoiceActorSplits Splits { get; }

    /// <summary>Raised (any thread) when the remote copy arrived or a local entry changed.</summary>
    event Action? Changed;

    /// <summary>Entries of the local override file only (what the maintenance window edits).</summary>
    IReadOnlyList<GameVoiceAliasEntry> LocalAliases { get; }

    /// <summary>Adds/replaces a local alias entry (by token), saves the file, raises <see cref="Changed"/>.</summary>
    void SetLocalAlias(GameVoiceAliasEntry entry);

    /// <summary>Drops the token from the local file (back to remote/embedded), raises <see cref="Changed"/>.</summary>
    void RemoveLocalAlias(string token);

    /// <summary>
    /// Writes the merged aliases in the format of <c>Resources/GameVoiceAliases.json</c> to
    /// <paramref name="path"/> (sorted, one entry per token) for a manual commit. Returns the count.
    /// </summary>
    int ExportAliasesForRepo(string path);
}
