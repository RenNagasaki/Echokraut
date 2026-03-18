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
        public bool AutoStartLocalInstance { get; set; } = false;
        public string LocalInstallPath { get; set; }  = "";
        public bool StreamingGeneration { get; set; } = true;
        public bool IsWindows11 { get; set; } = true;

        // TODO: Remove LocalInstance, RemoteInstance, NoInstance fields in a future update
        //       once all users have migrated to InstanceType. Keep for deserialization compat.
        public bool LocalInstance { get; set; } = false;
        public bool RemoteInstance { get; set; } = false;
        public bool NoInstance { get; set; } = false;

        /// <summary>
        /// The canonical instance type. Reads from InstanceType if set, otherwise migrates
        /// from the legacy boolean fields. Setter updates both the enum and the legacy fields.
        /// </summary>
        public AlltalkInstanceType InstanceType
        {
            get
            {
                if (LocalInstance) return AlltalkInstanceType.Local;
                if (RemoteInstance) return AlltalkInstanceType.Remote;
                return AlltalkInstanceType.None;
            }
            set
            {
                LocalInstance  = value == AlltalkInstanceType.Local;
                RemoteInstance = value == AlltalkInstanceType.Remote;
                NoInstance     = value == AlltalkInstanceType.None;
            }
        }

        public override string ToString()
        {
            return $"BaseUrl: {BaseUrl}, StreamPath: {StreamPath}, ReadyPath: {ReadyPath}, VoicesPath: {VoicesPath}, StopPath: {StopPath}";
        }
    }
}
