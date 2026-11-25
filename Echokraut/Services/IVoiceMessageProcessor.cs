using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// Processes text and speaker information into voice messages for TTS playback
/// </summary>
public interface IVoiceMessageProcessor
{
    /// <summary>
    /// A Talk box was dropped because its whole text was already spoken with the previous box
    /// (sentence continuation). Nothing plays, so nothing advances it after playback.
    /// </summary>
    event System.Action<EKEventId>? AlreadySpoken;

    /// <summary>
    /// Process a speech event and queue it for voice generation.
    /// </summary>
    /// <param name="worldEnglish">Optional English world name (server) for player speakers from chat,
    /// used to enrich Race/Gender via Lodestone lookup when the player isn't currently visible.</param>
    Task ProcessSpeechAsync(EKEventId eventId, IGameObject? speaker, SeString speakerName, string textValue,
        string worldEnglish = "");

    /// <summary>
    /// The same pipeline for a line of the in-game self-test (speaker without game object). The source
    /// is the one of <paramref name="eventId"/>: <see cref="Echotools.Logging.Enums.TextSource.VoiceTest"/>
    /// for the checks, <see cref="Echotools.Logging.Enums.TextSource.AddonTalk"/> for the E2E scenarios
    /// that need the Talk-only rules (sentence continuation, NPC mute).
    /// The message is never saved to disk or reported to the community database. Returns what was
    /// handed to the backend (null = stopped earlier, see log) and the character that was
    /// resolved, so the caller can remove it again.
    /// </summary>
    Task<SelfTestSpeech> ProcessSelfTestSpeechAsync(EKEventId eventId, string speakerName, string text);
}

/// <summary>Outcome of <see cref="IVoiceMessageProcessor.ProcessSelfTestSpeechAsync"/>.</summary>
/// <param name="Queued">The message handed to the backend, null when the pipeline stopped before.</param>
/// <param name="Speaker">The character the pipeline resolved or created, null when it stopped before step 3.</param>
/// <param name="Error">The exception that stopped the pipeline (type: message), null when it stopped by a rule or ran through.</param>
public sealed record SelfTestSpeech(VoiceMessage? Queued, NpcMapData? Speaker, string? Error = null);
