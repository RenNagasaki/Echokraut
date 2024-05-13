using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class AlltalkData : BackendData
    {
        public string BaseUrl = "http://127.0.0.1:7851";
        public string StreamPath { get; } = "/api/tts-generate-streaming";
        public string ReadyPath { get; } = "/api/ready";
        public string VoicesPath { get; } = "/api/voices";
        public string StopPath { get; } = "/api/stop-generation";
    }
}
