using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;

namespace Echokraut.Services;

/// <summary>
/// Extracts FFXIV's built-in voice acting from game `.scd` files and writes a per-NPC
/// sample folder usable by AllTalk (22050 Hz). Output goes to
/// <c>&lt;Configuration.LocalSaveLocation&gt;/FF14-Voices/</c>; nothing is persisted in the
/// SQLite database. See <c>plans/game-data-tools-window.md</c> for the full design.
/// </summary>
public interface IVoiceSampleExtractorService
{
    /// <summary>True while a starter-set build is running.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Fires from any thread when the extractor advances a phase or makes progress within a
    /// phase. Receivers should marshal to the framework thread before touching native UI.
    /// </summary>
    event Action<string, int, int>? ProgressChanged;

    /// <summary>
    /// Build a fresh starter set from game audio. Always overwrites previous output. Cancel
    /// via the supplied token; the run will stop at the next safe boundary.
    /// </summary>
    /// <param name="language">Language used for harvesting text keys (matches the game-side
    /// voice files for that locale).</param>
    /// <param name="samplesPerNpc">Number of samples per NPC (1..5 from the slider).</param>
    /// <param name="outputRootOverride">Optional root folder for the WAV output. When
    /// <c>null</c>, defaults to <c>Configuration.LocalSaveLocation</c>; when set, the
    /// extractor writes to <c>&lt;outputRootOverride&gt;/&lt;outputSubfolder&gt;/</c>.
    /// Used by the First-Time install flow to write voice samples directly into the
    /// AllTalk install folder (replacing the legacy <c>voices.zip</c> download). The
    /// user-local alias override file is always read from <c>LocalSaveLocation</c>
    /// regardless — that's per-user state, not per-output-target. <b>When set, the
    /// target subfolder is wiped before extraction</b> so the output is a known-good
    /// clean state (matches the old voices.zip "extract over fresh dir" semantics).</param>
    /// <param name="outputSubfolder">Subfolder name under <paramref name="outputRootOverride"/>
    /// (or <c>LocalSaveLocation</c>) where WAV files land. Defaults to <c>"FF14-Voices"</c>
    /// for the regular Game-Data-Tools run; the First-Time install flow passes
    /// <c>"voices"</c> so files land at <c>&lt;alltalk_tts&gt;/voices/</c> — AllTalk's
    /// expected voices directory.</param>
    Task RunAsync(ClientLanguage language, int samplesPerNpc, CancellationToken ct,
        string? outputRootOverride = null, string outputSubfolder = "FF14-Voices");
}
