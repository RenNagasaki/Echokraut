using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;

namespace Echokraut.Services;

/// <summary>Why the game-voice reference is (not) used right now (plan game-voice-reference §5).</summary>
public enum GameVoiceAvailability
{
    /// <summary>Feature switched off (default, §5.1).</summary>
    Disabled,
    /// <summary>AllTalk is active: its API only takes file names, so it is never supported (§2.4).</summary>
    WrongBackend,
    /// <summary>EchokrauTTS active but <c>/health</c> not asked yet (or asked again after a rejection).</summary>
    Unknown,
    /// <summary><c>/health</c> without <c>inline_reference: true</c> (older wrapper, MOSS on PyTorch) or unreachable.</summary>
    ServerUnsupported,
    /// <summary>Everything in place: lines of indexed NPCs get the game reference.</summary>
    Available,
}

/// <summary>The speaker a reference is asked for: a DB character in one client language.</summary>
/// <param name="CharacterId"><c>characters.id</c>; 0 = no DB row → never a reference.</param>
/// <param name="Name">Name as shown in game (client language).</param>
/// <param name="NpcBaseId">ENpcBase id of the live speaker, 0 when unknown.</param>
/// <param name="Language">Client language — the reference must speak the output language (§2.2).</param>
/// <param name="LineText">The line being spoken: its quest decides the voice-actor epoch (§11.4).
/// Null (batch, tools) → the accepted main-scenario quest or the completed progress decides.</param>
public sealed record GameVoiceSpeaker(int CharacterId, string Name, uint NpcBaseId, ClientLanguage Language, string? LineText = null);

/// <summary>
/// The ONLY place game audio may be used (root <c>CLAUDE.md</c> › hard constraint; plan
/// <c>docs/plans/game-voice-reference.md</c> §2, §4, §5): finds voiced cutscene lines of the
/// <b>same</b> NPC (exact name or <c>GameVoiceAliases.json</c>), ranks them, stores only their ids
/// (<c>game_voice_references</c>) and decodes the winners in memory into an
/// <see cref="InlineVoiceReference"/> for EchokrauTTS. Nothing is written to disk; without an index
/// (or with the feature off / an unsuitable server) the voice pack speaks.
/// </summary>
/// <summary>Options of the batch index (plan §11.2).</summary>
/// <param name="WithAsr">Whisper-check the top clips (slow, better); false = "fast without Whisper".</param>
/// <param name="DetectVoiceChanges">Also ask <c>/speaker-embedding</c> per patch and write boundary
/// suggestions (§11.3). Only meaningful with a calibrated encoder (XTTS).</param>
public sealed record GameVoiceBatchOptions(bool WithAsr = true, bool DetectVoiceChanges = false);

/// <summary>Batch progress: tokens done / total, the token being ranked, elapsed time.</summary>
public sealed record GameVoiceBatchProgress(int Done, int Total, string Token, TimeSpan Elapsed);

/// <summary>Outcome of <see cref="IGameVoiceReferenceService.BuildIndexAsync"/>.</summary>
public sealed record GameVoiceBatchResult(
    int Tokens, int Ranked, int UpToDate, int WithReference, int Unmatched, int Suggestions,
    string? ReportPath, string? SuggestionsPath, TimeSpan Elapsed, bool Cancelled, string? NotRunReason = null);

/// <summary>Outcome of <see cref="IGameVoiceReferenceService.SelfCheckAsync"/>.</summary>
/// <param name="Tokens">Speaker tokens in the text index.</param>
/// <param name="Lines">Voiced lines in the text index.</param>
/// <param name="DecodedClip">Clip id that decoded, null = none of the tried clips did.</param>
/// <param name="DecodedSeconds">Length of that clip.</param>
public sealed record GameVoiceSelfCheck(int Tokens, int Lines, string? DecodedClip, double DecodedSeconds);

public interface IGameVoiceReferenceService : IDisposable
{
    /// <summary>Current state; never blocks (a stale or missing probe is started in the background).</summary>
    GameVoiceAvailability Availability { get; }

    /// <summary>Raised when <see cref="Availability"/> changes (UI hint, phase 5).</summary>
    event Action<GameVoiceAvailability>? AvailabilityChanged;

    /// <summary>True while <see cref="BuildIndexAsync"/> runs (batch gating, <see cref="IBatchModeService"/>).</summary>
    bool IsIndexBuildRunning { get; }

    /// <summary>Asks <c>/health</c> now and updates <see cref="Availability"/>.</summary>
    Task<GameVoiceAvailability> RefreshAvailabilityAsync(EKEventId eventId, CancellationToken ct = default);

    /// <summary>
    /// Reference for one line, or null → use the voice pack. An NPC without an index is queued for
    /// ranking (when <c>GameVoiceRankOnConversationStart</c> is on) and this line waits — at most
    /// <c>FirstLineWait</c> — for the ranking's quick stage (no Whisper), so even the first line of
    /// a new NPC speaks with its game voice; the Whisper refinement follows in the background.
    /// </summary>
    Task<InlineVoiceReference?> GetReferenceAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct = default);

    /// <summary>The server refused or failed a request with a reference: forget the probe so the next line asks again.</summary>
    void ReportReferenceRejected(EKEventId eventId);

    /// <summary>
    /// Ranks every speaker token of the NPC for the epoch its story progress selects and stores the
    /// index. Returns the number of stored clips (0 = voice pack).
    /// </summary>
    Task<int> RankAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct = default);

    /// <summary>
    /// Batch "game-voice index for all" (plan §11.2): ranks every speaker token of the text index of
    /// <paramref name="language"/> — voiced NPCs only, met or not — per voice-actor epoch. Resumable:
    /// tokens with a current index (version + unchanged lines) are skipped. Writes the matching
    /// report (§11.5) and, with <see cref="GameVoiceBatchOptions.DetectVoiceChanges"/>, boundary
    /// suggestions (§11.3) into <c>&lt;save location&gt;/harvest/</c>.
    /// </summary>
    Task<GameVoiceBatchResult> BuildIndexAsync(ClientLanguage language, GameVoiceBatchOptions options,
        IProgress<GameVoiceBatchProgress>? progress, CancellationToken ct = default);

    /// <summary>
    /// Speaker tokens whose index is missing or outdated (new game patch, new ranking version) —
    /// the number the Backend tab shows next to the batch button. Builds the text index if needed.
    /// </summary>
    Task<int> CountOutdatedTokensAsync(ClientLanguage language, CancellationToken ct = default);

    /// <summary>
    /// In-game self-test: builds the text index and decodes the newest voiced clip that exists, in
    /// memory only, and throws the PCM away again — nothing is stored, sent or written. Does
    /// nothing (all zero) while the feature is off: game audio is only ever read after the opt-in.
    /// </summary>
    Task<GameVoiceSelfCheck> SelfCheckAsync(ClientLanguage language, CancellationToken ct = default);

    /// <summary>
    /// E2E run (plan ingame-e2e › Stages row 7 [H7]): the client-language name of a voiced NPC whose
    /// own token has the most voiced lines and only one voice-actor epoch — the name resolves back to
    /// that token, so a line spoken under it gets the game voice of the same NPC. Reads only the text
    /// index (no audio). Null while the feature is off or when no NPC qualifies.
    /// </summary>
    Task<string?> SampleVoicedNpcNameAsync(ClientLanguage language, CancellationToken ct = default);

    /// <summary>Drops all decoded PCM and text indexes held in memory (feature switched off).</summary>
    void ClearMemory();
}
