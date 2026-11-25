using System.Collections.Generic;
using System.Threading;
using Dalamud.Game;
using Echokraut.Helper.Functional.GameVoice;
using EchoScd;

namespace Echokraut.Services;

/// <summary>
/// Thin adapter between <see cref="GameVoiceReferenceService"/> and the user's game files
/// (Lumina via <c>IDataManager</c>). Everything stays in memory: rows and names are text, voice
/// files are decoded with <c>echoscd</c> straight from the byte array Lumina returns — no file is
/// written, Ogg-coded entries are skipped by the reader without touching their payload.
/// Kept separate so the service's logic is testable without game data.
/// </summary>
public interface IGameVoiceDataSource
{
    /// <summary>All rows of the <c>cut_scene/*</c> text sheets in <paramref name="language"/>.</summary>
    IEnumerable<CutsceneTextRow> EnumerateCutsceneRows(ClientLanguage language, CancellationToken ct);

    /// <summary>ENpcResident row id → singular name in <paramref name="language"/> (trimmed; empty names left out).</summary>
    IReadOnlyDictionary<uint, string> GetNpcNames(ClientLanguage language);

    /// <summary>
    /// Decodes the voice clip <paramref name="audioBase"/> in <paramref name="language"/> to mono
    /// 16-bit PCM at 24 kHz. Null when no real clip exists (patched away, stub file) or the first
    /// voice entry is not MS-ADPCM.
    /// </summary>
    PcmBuffer? TryDecodeVoice(string audioBase, ClientLanguage language);
}
