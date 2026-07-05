using Echokraut.Enums;

namespace Echokraut.DataClasses
{
    /// <summary>
    /// Per-engine config for the EchokrauTTS backend (a self-contained F5-TTS wrapper), parallel to
    /// <see cref="AlltalkData"/>. Does NOT store its own install path — the install root is shared
    /// across engines and lives in <see cref="Configuration.TtsInstallRoot"/>; both engines derive
    /// their subfolders from it via <c>TtsPaths</c>.
    ///
    /// <para>The wrapper server is single-language per run (pinned to the client language at start);
    /// the backend client omits the per-request <c>language</c> field so the server never rejects a
    /// differing-language line.</para>
    /// </summary>
    public class EchokrauTtsData
    {
        // Property, not a public field, to avoid the SonarQube S1104 the project is clearing.
        public string BaseUrl { get; set; } = "http://127.0.0.1:8765";

        // Endpoint paths are fixed (get-only with initializer; the config serializer skips them).
        public string TtsPath { get; } = "/tts";
        public string SamplesPath { get; } = "/samples";
        public string HealthPath { get; } = "/health";
        public string ShutdownPath { get; } = "/shutdown";
        public string CancelPath { get; } = "/cancel/"; // + {jobId}

        /// <summary>Optional Bearer token if the wrapper was started with an api_key.</summary>
        public string? ApiKey { get; set; }

        /// <summary>True once the local bootstrap install completed successfully.</summary>
        public bool LocalInstall { get; set; } = false;

        /// <summary>Auto-start the local instance on plugin load (when this engine is active).</summary>
        public bool AutoStartLocalInstance { get; set; } = true;

        /// <summary>Local / Remote / None — reuses the AllTalk instance-type enum.</summary>
        public AlltalkInstanceType InstanceType { get; set; } = AlltalkInstanceType.None;

        /// <summary>
        /// Sub-engine the LOCAL wrapper loads at startup (passed as <c>--tts-backend</c>). Default
        /// <see cref="EchokrauTtsEngine.XTTS"/> (better quality). Only meaningful for Local — a Remote
        /// server's engine is fixed by whoever started it. Both engines are installed by the
        /// bootstrap, so changing this restarts the local instance rather than reinstalling.
        /// </summary>
        public EchokrauTtsEngine TtsBackend { get; set; } = EchokrauTtsEngine.XTTS;

        /// <summary>The lower-cased wrapper arg value for <see cref="TtsBackend"/> (<c>xtts</c>/<c>f5</c>).</summary>
        public string TtsBackendArg => TtsBackend.ToString().ToLowerInvariant();

        // NOTE: no CpuMode in v1 — the wrapper auto-detects GPU/CPU (gpu_detect.py) and exposes no
        // CPU override. A force-CPU option is a tracked TODO (needs a wrapper-side flag + checkbox).

        /// <summary>True when this engine can produce new audio (Local or Remote). Mirrors
        /// <see cref="AlltalkData.HasLiveGeneration"/>; the engine-aware aggregate lives on
        /// <c>Configuration.HasLiveGeneration</c>.</summary>
        public bool HasLiveGeneration => InstanceType != AlltalkInstanceType.None;
    }
}
