using System;
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
        public string AsrCheckPath { get; } = "/asr-check";
        public string SpeakerEmbeddingPath { get; } = "/speaker-embedding";
        public string ShutdownPath { get; } = "/shutdown";
        public string CancelPath { get; } = "/cancel/"; // + {jobId}

        /// <summary>Optional Bearer token if the wrapper was started with an api_key.</summary>
        public string? ApiKey { get; set; }

        /// <summary>True once the local bootstrap install completed successfully.</summary>
        public bool LocalInstall { get; set; } = false;

        /// <summary>
        /// Git release tag of the EchokrauTTS wrapper currently extracted under
        /// <c>{TtsInstallRoot}\echokrautts</c>. Written by <c>IEchokrauTtsInstanceService</c> after a
        /// successful install/update from <c>RemoteUrlsData.EchokrauTtsVersion</c>; compared against
        /// that same remote value to offer the Update button (<c>WrapperUpdatePolicy</c>).
        /// <para>Installs made before the handshake existed carry no value; they are backfilled once
        /// with <c>WrapperUpdatePolicy.AssumedLegacyVersion</c> by
        /// <c>Configuration.MigrateWrapperVersionForExistingInstalls</c>, because the only wrapper
        /// they can be running is the single published release. Still empty (Remote/None users, no
        /// wrapper on disk) reads as "unknown" and offers no update — the policy requires a local
        /// install first.</para>
        /// </summary>
        public string InstalledWrapperVersion { get; set; } = "";

        /// <summary>Auto-start the local instance on plugin load (when this engine is active).</summary>
        public bool AutoStartLocalInstance { get; set; } = true;

        /// <summary>
        /// URL (direct or Google Drive) to a zip holding a user-supplied custom model. Installed by
        /// "Install only custom data" into <c>echokrautts/models/echokraut_custom</c>, where the
        /// wrapper auto-detects it for the ACTIVE engine (an F5 checkpoint for the f5 backend, a full
        /// XTTS-v2 model directory for the xtts backend). Mirrors <see cref="AlltalkData.CustomModelUrl"/>.
        /// </summary>
        public string CustomModelUrl { get; set; } = "";

        /// <summary>
        /// URL (direct or Google Drive) to a zip of custom voice samples, merged additively into
        /// <c>echokrautts/samples</c> (existing voices are preserved). Mirrors
        /// <see cref="AlltalkData.CustomVoicesUrl"/>.
        /// </summary>
        public string CustomVoicesUrl { get; set; } = "";

        /// <summary>Local / Remote / None — reuses the AllTalk instance-type enum.</summary>
        public AlltalkInstanceType InstanceType { get; set; } = AlltalkInstanceType.None;

        /// <summary>
        /// Sub-engine the LOCAL wrapper loads at startup (passed as <c>--tts-backend</c>), stored as the
        /// wrapper's own id (<c>xtts</c> / <c>f5</c> / <c>moss</c> / whatever a future wrapper adds).
        /// Default <see cref="DefaultTtsEngine"/> (better quality). Only meaningful for Local — a
        /// Remote server's engine is fixed by whoever started it. Changing it restarts the local
        /// instance; since wrapper 0.0.2.0 that restart installs the engine if it is not there yet.
        /// <para>A free-form string rather than an enum on purpose: the list of engines comes from the
        /// wrapper repo (<see cref="TtsEngineCatalog"/>), so the plugin must be able to persist an
        /// engine it has never heard of.</para>
        /// </summary>
        public string TtsEngine { get; set; } = DefaultTtsEngine;

        /// <summary>Engine assumed when nothing is configured — the higher-quality one.</summary>
        public const string DefaultTtsEngine = "xtts";

        /// <summary>
        /// Legacy home of <see cref="TtsEngine"/>, back when the engine list was a hard-coded enum.
        /// Only ever read: <c>Configuration.MigrateTtsEngineId</c> folds a saved value into
        /// <see cref="TtsEngine"/> and clears it. Nullable so an untouched config stays untouched.
        /// </summary>
        public EchokrauTtsEngine? TtsBackend { get; set; }

        /// <summary>The wrapper arg value for <see cref="TtsEngine"/> (<c>xtts</c>/<c>f5</c>/…).</summary>
        public string TtsBackendArg =>
            string.IsNullOrWhiteSpace(TtsEngine) ? DefaultTtsEngine : TtsEngine.Trim().ToLowerInvariant();

        /// <summary>
        /// Load the XTTS model in half precision (fp16) on the LOCAL wrapper for a ~1.3–1.8x inference
        /// speedup. Passed as <c>--xtts-fp16</c>. Only takes effect with the XTTS engine on a CUDA/ROCm
        /// GPU (the wrapper ignores it for F5 / on CPU). Default off. Changing it restarts the local
        /// instance (the model is loaded with the chosen precision at startup).
        /// </summary>
        public bool XttsFp16 { get; set; } = false;

        /// <summary>The wrapper arg value for <see cref="XttsFp16"/> (<c>true</c>/<c>false</c>).</summary>
        public string XttsFp16Arg => XttsFp16 ? "true" : "false";

        /// <summary>
        /// Qwen engines only: sampling temperature (<c>--qwen-temperature</c>). 0.9 is the library's
        /// default and the wrapper's; 0.7 is the offered calmer alternative.
        /// </summary>
        public double QwenTemperature { get; set; } = DefaultQwenTemperature;

        public const double DefaultQwenTemperature = 0.9;
        public const double CalmQwenTemperature = 0.7;

        /// <summary>
        /// Local wrapper: reuse the audio of an identical request for a few minutes (response cache,
        /// any engine). On = nothing is passed and the wrapper's <c>config.json</c> decides
        /// (default 5 min); off = <c>--response-cache-minutes 0</c>, which overrides it — every line is
        /// generated anew, e.g. to hear how much one voice varies. Default on.
        /// </summary>
        public bool ResponseCache { get; set; } = true;

        /// <summary>The wrapper arg value for <see cref="ResponseCache"/>: empty (not passed) or <c>0</c>.</summary>
        public string ResponseCacheMinutesArg => ResponseCache ? string.Empty : "0";

        /// <summary>
        /// Pocket TTS only: model size (<c>--pocket-layers</c>), 24 (default, sounds better) or 6
        /// (about 2.4x faster, audibly a bit worse). An option of the one engine, not a second engine
        /// (user decision 2026-11-13).
        /// </summary>
        public int PocketLayers { get; set; } = DefaultPocketLayers;

        public const int DefaultPocketLayers = 24;
        public static readonly int[] PocketLayerOptions = [24, 6];

        /// <summary>
        /// Pocket TTS sampler decoding steps for the 24-layer model, 1 / 4 / 8. 1 is the fastest but
        /// occasionally adds reverb; 4 (default) and 8 do not.
        /// </summary>
        public int PocketDecodeSteps24 { get; set; } = DefaultPocketDecodeSteps;

        /// <summary>
        /// Pocket TTS decoding steps for the 6-layer model, 4 / 8 only — with 1 its word error rate
        /// more than doubled (measured 2026-11-13). Kept apart from <see cref="PocketDecodeSteps24"/>
        /// so switching the size never carries a 1 over, and each size remembers its own choice.
        /// </summary>
        public int PocketDecodeSteps6 { get; set; } = DefaultPocketDecodeSteps;

        public const int DefaultPocketDecodeSteps = 4;
        public static readonly int[] PocketDecodeStepOptions = [1, 4, 8];

        /// <summary>The steps a model size allows: all three for 24 layers, 4 and 8 for 6 layers.</summary>
        public static int[] PocketStepOptionsFor(int layers) => layers == 6 ? [4, 8] : PocketDecodeStepOptions;

        /// <summary>The configured size, 24 for anything unknown.</summary>
        public int PocketLayersEffective => Array.IndexOf(PocketLayerOptions, PocketLayers) >= 0 ? PocketLayers : DefaultPocketLayers;

        /// <summary>Steps for the active size; a value that size does not allow falls back to the default.</summary>
        public int PocketDecodeSteps
        {
            get
            {
                var layers = PocketLayersEffective;
                var steps = layers == 6 ? PocketDecodeSteps6 : PocketDecodeSteps24;
                return Array.IndexOf(PocketStepOptionsFor(layers), steps) >= 0 ? steps : DefaultPocketDecodeSteps;
            }
        }

        /// <summary>Stores <paramref name="steps"/> for the active size. False when that size does not
        /// allow it (1 on the 6-layer model) — nothing is changed then.</summary>
        public bool TrySetPocketDecodeSteps(int steps)
        {
            if (Array.IndexOf(PocketStepOptionsFor(PocketLayersEffective), steps) < 0) return false;
            if (PocketLayersEffective == 6) PocketDecodeSteps6 = steps;
            else PocketDecodeSteps24 = steps;
            return true;
        }

        /// <summary>The wrapper arg value for <see cref="PocketDecodeSteps"/>.</summary>
        public string PocketDecodeStepsArg => PocketDecodeSteps.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The wrapper arg value for <see cref="PocketLayers"/>.</summary>
        public string PocketLayersArg => PocketLayersEffective.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The wrapper arg value for <see cref="QwenTemperature"/> — invariant culture, or a
        /// German client would send <c>0,7</c>, which the wrapper's float parser rejects.</summary>
        public string QwenTemperatureArg =>
            QwenTemperature.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture);

        // NOTE: no CpuMode in v1 — the wrapper auto-detects GPU/CPU (gpu_detect.py) and exposes no
        // CPU override. A force-CPU option is a tracked TODO (needs a wrapper-side flag + checkbox).

        /// <summary>True when this engine can produce new audio (Local or Remote). Mirrors
        /// <see cref="AlltalkData.HasLiveGeneration"/>; the engine-aware aggregate lives on
        /// <c>Configuration.HasLiveGeneration</c>.</summary>
        public bool HasLiveGeneration => InstanceType != AlltalkInstanceType.None;
    }
}
