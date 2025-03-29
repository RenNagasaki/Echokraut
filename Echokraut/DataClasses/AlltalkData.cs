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
        public string ReloadModel = "xtts - xtts2.0.3-trained";
        public bool LocalInstall = false;
        public string LocalInstallPath = "C:\\alltalk_tts";
        public string ModelUrl = "";
        public string VoicesUrl = "";
        public string AlltalkUrl = "https://github.com/RenNagasaki/alltalk_tts/releases/download/alltalk_tts-alltalkbeta/alltalk_tts-alltalkbeta.zip";

        public override string ToString()
        {
            return $"BaseUrl: {BaseUrl}, StreamPath: {StreamPath}, ReadyPath: {ReadyPath}, VoicesPath: {VoicesPath}, StopPath: {StopPath}";
        }
    }
}
