using System.Collections.Generic;
using Echokraut.DataClasses;

namespace Echokraut.Services;

/// <summary>
/// Supplies the list of sub-engines the EchokrauTTS wrapper knows about, read from the wrapper repo
/// instead of being hard-coded in the plugin. See <see cref="TtsEngineCatalog"/>.
/// </summary>
public interface ITtsEngineCatalogService
{
    /// <summary>
    /// The known engines, in publication order. <b>Never empty</b>: until (or unless) the remote file
    /// arrives, this is the copy embedded in the plugin, so the dropdown always has content.
    /// </summary>
    IReadOnlyList<TtsEngineInfo> Engines { get; }

    /// <summary>True once the remote catalog replaced the embedded fallback (diagnostics only).</summary>
    bool RemoteLoaded { get; }

    /// <summary>
    /// Replaces the list with the catalog in <paramref name="path"/> and keeps it: a remote fetch
    /// still in flight no longer overwrites it. Used for a test install of an untagged wrapper
    /// (<c>TestWrapperInstall.CatalogPath</c>), whose engines are in no tagged <c>engines.json</c>.
    /// Returns false (list unchanged) when the file is missing or unusable.
    /// </summary>
    bool UseLocalCatalog(string path);
}
