using System;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services
{
    /// <inheritdoc cref="ITtsVoiceSyncService"/>
    public class TtsVoiceSyncService : ITtsVoiceSyncService
    {
        private readonly Configuration _config;
        private readonly ILogService _log;
        private readonly IBackendService _backend;

        public TtsVoiceSyncService(Configuration config, ILogService log, IBackendService backend)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>The voice folder for an engine under the shared install root. AllTalk uses
        /// <c>alltalk_tts\voices</c>, EchokrauTTS uses <c>echokrautts\samples</c>.</summary>
        internal static string VoicesFolderFor(TTSBackends engine, string installRoot) =>
            engine == TTSBackends.EchokrauTTS
                ? TtsPaths.EchokrauTtsSamples(installRoot)
                : TtsPaths.AllTalkVoices(installRoot);

        public int CopyVoicesForSwitch(TTSBackends from, TTSBackends to)
        {
            if (from == to) return 0;

            var root = _config.TtsInstallRoot;
            var src = VoicesFolderFor(from, root);
            var dst = VoicesFolderFor(to, root);
            var eventId = new EKEventId(0, TextSource.None);

            var copied = DirectoryMerge.MergeCopy(src, dst, overwrite: true);
            _log.Info(nameof(CopyVoicesForSwitch),
                $"Voice sync {from}→{to}: copied {copied} file(s) from '{src}' to '{dst}' " +
                "(overwrote same-named, kept extras)", eventId);
            return copied;
        }

        public void SwitchEngine(TTSBackends newEngine)
        {
            var old = _config.BackendSelection;
            if (old == newEngine) return;

            var eventId = new EKEventId(0, TextSource.None);
            _log.Info(nameof(SwitchEngine), $"Switching TTS engine {old}→{newEngine}", eventId);

            // Flush in-flight generation before swapping the backend so a stale job/backend isn't
            // touched mid-switch.
            _backend.CancelAll();

            // Copy the previous engine's voices into the new engine so it's immediately usable.
            CopyVoicesForSwitch(old, newEngine);

            _config.BackendSelection = newEngine;
            _config.Save();

            _backend.SetBackendType(newEngine);
        }
    }
}
