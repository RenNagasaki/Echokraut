using System;
using Echokraut.Enums;
using Echotools.Logging.Enums;

namespace Echokraut.DataClasses
{
    public class AlltalkData
    {
        public string BaseUrl = "http://127.0.0.1:7851";
        public string StreamPath { get; } = "/api/tts-generate-streaming";
        public string ReadyPath { get; } = "/api/ready";
        public string VoicesPath { get; } = "/api/voices";
        public string StopPath { get; } = "/api/stop-generation";
        public string ReloadPath { get; } = "/api/reload?tts_method=";
        public string ReloadModel = "xtts - xtts2.0.3";
        public string CustomModelUrl = "";
        public string CustomVoicesUrl = "";
        public bool LocalInstall { get; set; } = false;
        public bool AutoStartLocalInstance { get; set; } = true;
        public string LocalInstallPath { get; set; }  = "C:\\alltalk_tts";
        public bool StreamingGeneration { get; set; } = true;
        public bool IsWindows11 { get; set; } = true;
        public bool CpuMode { get; set; } = false;

        /// <summary>
        /// The canonical instance type. Persisted directly as an enum since
        /// <see cref="MigrateLegacyInstanceTypeFields"/> ran on first load after this migration
        /// landed. Old configs are still readable: the legacy booleans below stay deserializable
        /// for one cycle and the migration derives the enum from them.
        /// </summary>
        public AlltalkInstanceType InstanceType { get; set; } = AlltalkInstanceType.None;

        /// <summary>
        /// True when the plugin can produce new audio via a live TTS backend (Local or Remote).
        /// False in <see cref="AlltalkInstanceType.None"/> — that mode plays only pre-existing
        /// audio files and must NOT call generation, voice routing, voice tests, or any other
        /// path that would hit a TTS backend. Single source of truth for "is generation
        /// available" — services and UI both gate on this.
        /// </summary>
        public bool HasLiveGeneration => InstanceType != AlltalkInstanceType.None;

        // Legacy boolean fields — kept for one release so older configs can be deserialized
        // and migrated to <see cref="InstanceType"/>. After migration runs they're forced
        // back to false, so future Save() calls write inert values. Remove after enough time
        // has passed for active users to have loaded + saved at least once on the new code.
        [Obsolete("Use InstanceType. Kept for legacy config deserialization.", false)]
        public bool LocalInstance { get; set; } = false;
        [Obsolete("Use InstanceType. Kept for legacy config deserialization.", false)]
        public bool RemoteInstance { get; set; } = false;
        [Obsolete("Use InstanceType. Kept for legacy config deserialization.", false)]
        public bool NoInstance { get; set; } = false;

        /// <summary>
        /// Translate legacy <c>LocalInstance</c>/<c>RemoteInstance</c>/<c>NoInstance</c> booleans
        /// into <see cref="InstanceType"/> if the enum is still at its default and a boolean is
        /// set. After the call the legacy booleans are forced to false so subsequent Save() calls
        /// don't carry redundant data. Idempotent — safe to call repeatedly. Should be called once
        /// during plugin initialization, before any code reads <see cref="InstanceType"/>.
        /// </summary>
        public void MigrateLegacyInstanceTypeFields()
        {
#pragma warning disable CS0618 // Migration intentionally reads obsolete fields.
            if (InstanceType == AlltalkInstanceType.None)
            {
                if (LocalInstance) InstanceType = AlltalkInstanceType.Local;
                else if (RemoteInstance) InstanceType = AlltalkInstanceType.Remote;
                // No legacy boolean (or NoInstance=true) → InstanceType stays None, the default.
            }
            LocalInstance = false;
            RemoteInstance = false;
            NoInstance = false;
#pragma warning restore CS0618
        }

        public override string ToString()
        {
            return $"BaseUrl: {BaseUrl}, StreamPath: {StreamPath}, ReadyPath: {ReadyPath}, VoicesPath: {VoicesPath}, StopPath: {StopPath}";
        }
    }
}
