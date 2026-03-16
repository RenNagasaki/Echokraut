using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Echokraut.DataClasses;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// Processes text and speaker information into voice messages for TTS playback
/// </summary>
public interface IVoiceMessageProcessor
{
    /// <summary>
    /// Process a speech event and queue it for voice generation
    /// </summary>
    Task ProcessSpeechAsync(EKEventId eventId, IGameObject? speaker, SeString speakerName, string textValue);
}
