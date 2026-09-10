using System;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Backend
{
    public interface ITTSBackend
    {
        List<string>? GetAvailableVoices(EKEventId eventId);

        /// <summary>
        /// Synthesises <paramref name="voiceLine"/>. <paramref name="onJobStarted"/> is invoked as
        /// soon as the engine reports an id for this request (EchokrauTTS' <c>X-Job-Id</c>), so the
        /// caller can abort exactly this job later — see <see cref="StopGenerating"/>. Backends
        /// without a per-job handle simply never call it.
        /// </summary>
        Task<Stream?> GenerateAudioStreamFromVoice(EKEventId eventId, VoiceMessage voiceLine, string voice, ClientLanguage language, Action<string>? onJobStarted = null);

        Task<string> CheckReady(EKEventId eventId);

        /// <summary>
        /// Aborts a running synthesis. <paramref name="jobId"/> identifies the request to cancel;
        /// the caller owns it (see <c>GenerateAudioStreamFromVoice</c>'s <c>onJobStarted</c>)
        /// because a backend-side "last job" field points at the wrong request as soon as the next
        /// line starts, and is lost entirely when the backend instance is recreated. Backends with
        /// a global stop endpoint ignore it.
        /// </summary>
        Task StopGenerating(EKEventId eventId, string? jobId = null);
    }
}
