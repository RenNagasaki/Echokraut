using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public bool LocalInstance { get; set; } = false;
        public bool AutoStartLocalInstance { get; set; } = false;
        public bool RemoteInstance { get; set; } = false;
        public string LocalInstallPath { get; set; }  = "";

        public override string ToString()
        {
            return $"BaseUrl: {BaseUrl}, StreamPath: {StreamPath}, ReadyPath: {ReadyPath}, VoicesPath: {VoicesPath}, StopPath: {StopPath}";
        }
    }
}
