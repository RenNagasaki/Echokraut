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
public sealed record GameVoiceSpeaker(int CharacterId, string Name, uint NpcBaseId, ClientLanguage Language);

/// <summary>
/// The ONLY place game audio may be used (root <c>CLAUDE.md</c> › hard constraint; plan
/// <c>docs/plans/game-voice-reference.md</c> §2, §4, §5): finds voiced cutscene lines of the
/// <b>same</b> NPC (exact name or <c>GameVoiceAliases.json</c>), ranks them, stores only their ids
/// (<c>game_voice_references</c>) and decodes the winners in memory into an
/// <see cref="InlineVoiceReference"/> for EchokrauTTS. Nothing is written to disk; without an index
/// (or with the feature off / an unsuitable server) the voice pack speaks.
/// </summary>
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
    /// Reference for one line, or null → use the voice pack. Never waits for ranking: an NPC without
    /// an index is queued for background ranking (when <c>GameVoiceRankOnConversationStart</c> is on)
    /// and this line uses the voice pack.
    /// </summary>
    InlineVoiceReference? TryGetReference(GameVoiceSpeaker speaker, EKEventId eventId);

    /// <summary>The server refused or failed a request with a reference: forget the probe so the next line asks again.</summary>
    void ReportReferenceRejected(EKEventId eventId);

    /// <summary>Ranks one NPC now and stores its index. Returns the number of stored clips (0 = voice pack).</summary>
    Task<int> RankAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct = default);

    /// <summary>
    /// Batch "build index" (F5): ranks every NPC of <paramref name="language"/> without a current
    /// index. Progress = (done, total). Returns the number of NPCs that got at least one clip.
    /// </summary>
    Task<int> BuildIndexAsync(ClientLanguage language, IProgress<(int Done, int Total)>? progress, CancellationToken ct = default);

    /// <summary>Drops all decoded PCM and text indexes held in memory (feature switched off).</summary>
    void ClearMemory();
}
