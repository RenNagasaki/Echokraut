using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;

namespace Echokraut.Services;

/// <summary>
/// Export and import of Echokraut audio packages (<c>*.ekpack</c>) — a zip with a
/// <c>manifest.json</c> plus the generated WAVs, so a big batch of generated audio can be handed
/// to another user whose install attributes every file to the right character, language and voice
/// without guessing from file names.
///
/// <para>
/// This is deliberately the ONLY place package metadata is produced or consumed. The live
/// generation path and the WAV writer are untouched — no metadata is embedded in the audio files
/// themselves, which is what lets a package be built from audio that was generated long ago.
/// </para>
/// </summary>
public interface IAudioPackageService
{
    /// <summary>True while an export or import is in flight.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Fires from a background thread on phase change and progress. Receivers must marshal to the
    /// framework thread before touching native UI.
    /// </summary>
    event Action<string, int, int>? ProgressChanged;

    /// <summary>
    /// Writes every generated audio file the database knows about into a package.
    /// </summary>
    /// <param name="packagePath">Target file. Overwritten if it exists.</param>
    /// <param name="language">Restrict to one <c>ClientLanguage</c> value, or <c>null</c> for all.</param>
    Task<AudioPackageExportResult> ExportAsync(string packagePath, int? language,
        CancellationToken ct = default);

    /// <summary>
    /// Reads a package: unpacks the audio into <c>Configuration.LocalSaveLocation</c> and creates
    /// the characters / voice clips / generation rows the entries describe. Existing local audio
    /// files are never overwritten — the DB row is still written so the clip counts as generated.
    /// </summary>
    Task<AudioPackageImportResult> ImportAsync(string packagePath, CancellationToken ct = default);
}
