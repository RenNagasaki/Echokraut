using System;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// Downloads a curated pack of freely-licensed reference voices from the internet and unpacks
/// it into the TTS engine's voice folder. This is the legal replacement for the removed
/// in-game voice extraction: no game asset is ever read, decoded or redistributed.
/// <para>
/// The pack contains ready-to-use 16-bit PCM mono WAV samples (22050 Hz) plus a same-named
/// <c>.txt</c> transcript per sample — the exact on-disk contract AllTalk / EchokrauTTS expect
/// for voice cloning — and a <c>LICENSE.txt</c> / <c>ATTRIBUTION.csv</c> pair naming every
/// speaker and their upstream license. Those files must never be stripped from the output.
/// </para>
/// <para>
/// <b>Filename grammar is load-bearing.</b> The plugin derives a voice's allowed genders,
/// allowed races and body type from the FILENAME alone — see
/// <c>NpcDataService.ReSetVoiceGenders</c> / <c>ReSetVoiceRaces</c> (split on <c>_</c>, then on
/// <c>-</c>) and the <c>EchokrautVoice.VoiceName</c> display-name tokenizer. Every sample in the
/// pack must therefore be named:
/// <code>
/// Gender_RacePool[-BodyType]_NPCnnn.wav
/// </code>
/// <list type="bullet">
/// <item><c>Gender</c> — <c>Male</c> / <c>Female</c>, segment[0] ONLY (must parse as
///   <see cref="Enums.Genders"/>). Nothing else is scanned for gender.</item>
/// <item><c>RacePool</c> — segment[1] ONLY. <c>All</c> for the generic pack voices (expands to
///   every race in <c>Constants.RACELIST</c>), or an exact <see cref="Enums.NpcRaces"/> token
///   (<c>Miqote</c>, <c>AuRa</c>, <c>Hyur</c> — no apostrophes, spaces or "Hyuran").</item>
/// <item><c>BodyType</c> — optional <c>-Child</c> / <c>-Elder</c> appended to the race segment;
///   omitted means Adult.</item>
/// <item><c>NPCnnn</c> — the display name. It <b>must contain the literal, case-sensitive
///   <c>NPC</c></b>: <c>BackendService.MapVoices</c> sets <c>UseAsRandom = name.Contains("NPC")</c>
///   and <c>EchokrautVoice.FitsNpcData</c> requires it, so a name without <c>NPC</c> is only ever
///   selectable by hand. Keep the rest neutral (a running number), never the upstream dataset's
///   speaker id — that mapping belongs in <c>ATTRIBUTION.csv</c> only.</item>
/// </list>
/// Good: <c>Male_All_NPC001.wav</c>, <c>Female_All-Child_NPC091.wav</c>,
/// <c>Female_All-Elder_NPC120.wav</c>.
/// Bad: <c>voice_m_07.wav</c> (no gender/race → empty filter, inert), <c>Male_01.wav</c>
/// (<c>Enum.TryParse</c> reads <c>01</c> as the enum value 1 → silently filed as race Hyur),
/// <c>Male_All_M01.wav</c> (parses fine but has no <c>NPC</c> → never auto-assigned).
/// <c>WarnAboutUnparsableNames</c> logs a warning after unpacking for both failure modes.
/// </para>
/// <para>
/// The engine lists — and the plugin stores as a voice's identity key — the filename
/// <b>including the extension</b>. Renaming a sample later therefore drops the old voice and
/// creates a new one, migrating any NPC that used it. Pick the names once, before publishing.
/// A pack may also ship <c>Narrator.wav</c> (exact spelling, top level) to replace the default
/// narrator fallback.
/// </para>
/// </summary>
public interface IVoicePackService
{
    /// <summary>True while a download/unpack run is in flight.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Fires from any thread when the run advances a phase or makes progress within a phase.
    /// Receivers must marshal to the framework thread before touching native UI.
    /// </summary>
    event Action<string, int, int>? ProgressChanged;

    /// <summary>
    /// Download the voice pack and unpack it. Cancel via the supplied token; the run stops at
    /// the next safe boundary.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="outputRootOverride">Optional root folder for the output. When <c>null</c>,
    /// defaults to <c>Configuration.LocalSaveLocation</c>. <b>When set, the target subfolder is
    /// wiped before unpacking</b> so a fresh install gets a known-good clean voice set — this is
    /// the path the First-Time install flow uses.</param>
    /// <param name="outputSubfolder">Subfolder under the root where the samples land. Defaults to
    /// <c>"Voices"</c> for a manual run; the install flow passes <c>"voices"</c> (AllTalk) or
    /// <c>"samples"</c> (EchokrauTTS).</param>
    Task DownloadAsync(CancellationToken ct, string? outputRootOverride = null,
        string outputSubfolder = "Voices");
}
