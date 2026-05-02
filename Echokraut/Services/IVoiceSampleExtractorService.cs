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
    Task RunAsync(ClientLanguage language, int samplesPerNpc, CancellationToken ct);
}
