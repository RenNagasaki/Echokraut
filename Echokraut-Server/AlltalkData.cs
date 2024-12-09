using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public static class AlltalkData
    {
        public static string BaseUrl = "http://127.0.0.1";
        public static string StreamPath { get; } = "/api/tts-generate-streaming";
        public static string ReadyPath { get; } = "/api/ready";
        public static string VoicesPath { get; } = "/api/voices";
        public static string StopPath { get; } = "/api/stop-generation";
        public static string ReloadPath { get; } = "/api/reload?tts_method=";
    }
}
