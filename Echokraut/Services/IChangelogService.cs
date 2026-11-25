using System.Collections.Generic;

namespace Echokraut.Services;

/// <summary>
/// Loads embedded changelog resources and decides which ones a user should still see
/// based on <see cref="Echokraut.DataClasses.Configuration.LastSeenChangelogVersion"/>
/// versus the current plugin version. Used by <c>NativeChangelogWindow</c> to render
/// "what's new since you last looked" content after a plugin update.
/// </summary>
public interface IChangelogService
{
    /// <summary>True iff at least one changelog entry exists with version &gt; LastSeen and ≤ current.</summary>
    bool HasUnseenChangelogs();

    /// <summary>
    /// All unseen changelog entries, ordered ascending by version. Each entry contains the
    /// raw plain-text body for the user's client language (EN/DE/FR/JA → falls back to EN
    /// if the localized variant doesn't exist).
    /// </summary>
    IReadOnlyList<ChangelogEntry> GetUnseenChangelogs();

    /// <summary>
    /// Marks every available changelog as seen by writing the current plugin version to
    /// <see cref="Echokraut.DataClasses.Configuration.LastSeenChangelogVersion"/> and
    /// persisting the config. Idempotent.
    /// </summary>
    void MarkAllSeen();
}

/// <summary>
/// Single changelog entry — one per (target version, language) embedded file.
/// </summary>
public sealed class ChangelogEntry
{
    /// <summary>Version string as it appears in the resource name (e.g. "v0.19.0.0").</summary>
    public string Version { get; }

    /// <summary>Raw file content. Plain text with ASCII section dividers — render as-is in a monospace area.</summary>
    public string Content { get; }

    public ChangelogEntry(string version, string content)
    {
        Version = version;
        Content = content;
    }
}
