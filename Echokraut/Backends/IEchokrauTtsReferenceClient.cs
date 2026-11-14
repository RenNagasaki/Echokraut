using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;

namespace Echokraut.Backend
{
    /// <summary>
    /// The two EchokrauTTS calls the game-voice reference needs besides <c>/tts</c>
    /// (plan <c>docs/plans/game-voice-reference.md</c> §2.4/§4/§5.3). Implemented by
    /// <see cref="EchokrauTtsBackend"/>; AllTalk has no equivalent and is never asked.
    /// </summary>
    public interface IEchokrauTtsReferenceClient
    {
        /// <summary><c>GET /health</c>, parsed; null when the server is unreachable or answers non-2xx.</summary>
        Task<EchokrauTtsHealthResponse?> GetHealthAsync(EKEventId eventId, CancellationToken ct = default);

        /// <summary>
        /// <c>POST /asr-check</c>: transcribes <paramref name="pcm"/> (mono 16-bit,
        /// <see cref="InlineVoiceReference.SampleRate"/>) in the wrapper's memory. Null on any
        /// failure — ranking then simply goes without the ASR term.
        /// </summary>
        Task<string?> AsrCheckAsync(EKEventId eventId, short[] pcm, string? languageCode, CancellationToken ct = default);
    }
}
