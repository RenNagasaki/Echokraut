using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class Config
    {
        public string FF14UrlBase { get; set; }
        public string XIVAPIUrlBase { get; set; }
        public string XIVAPIPath { get; set; }
        public string UrlBase { get; set; }
        public string GeneratePath { get; set; }
        public string ReadyPath { get; set; }
        public string VoicePath { get; set; }
        public string StopPath { get; set; }
        public string StartPath { get; set; }
        public string SetModelFT { get; set; }
        public string? Language { get; set; }
        public bool? TopMost { get; set; }
        public bool? QueueText { get; set; }
    }
}
