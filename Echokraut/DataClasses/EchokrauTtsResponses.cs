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
    }
}
