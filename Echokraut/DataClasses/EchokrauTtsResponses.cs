using System.Collections.Generic;

namespace Echokraut.DataClasses
{
    /// <summary>Response of EchokrauTTS <c>GET /samples</c> — the available voice sample filenames
    /// (with extension, e.g. <c>Female_Hyur_Iceheart.wav</c>).</summary>
    public class EchokrauTtsSamplesResponse
    {
        public List<string> samples { get; set; } = new();
    }

    /// <summary>Response of EchokrauTTS <c>GET /health</c>.</summary>
    public class EchokrauTtsHealthResponse
    {
        public string? status { get; set; }
        public string? backend { get; set; }
        public string? device { get; set; }
        public string? language { get; set; }

        /// <summary>True when the server keeps an inline <c>ref_audio</c> in memory (plan
        /// game-voice-reference §5.3). Missing (older wrapper) or false → the plugin never sends
        /// game audio there and stays on the voice pack.</summary>
        public bool? inline_reference { get; set; }

        /// <summary>True when <c>POST /asr-check</c> exists.</summary>
        public bool? asr_check { get; set; }
    }

    /// <summary>Response of EchokrauTTS <c>POST /asr-check</c>.</summary>
    public class EchokrauTtsAsrCheckResponse
    {
        public string? text { get; set; }
        public double seconds { get; set; }
    }
}
