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

        /// <summary>Longest inline reference <c>/tts</c> accepts, in seconds (per engine: Qwen 20,
        /// the others 15). Missing (older wrapper) → <see cref="Echokraut.Helper.Functional.GameVoice.InlineReferenceAssembler.DefaultMaxSeconds"/>.</summary>
        public double? inline_ref_max_seconds { get; set; }

        /// <summary>True when <c>POST /asr-check</c> exists.</summary>
        public bool? asr_check { get; set; }

        /// <summary>True when <c>POST /speaker-embedding</c> works with the active engine (§11.3).</summary>
        public bool? speaker_embedding { get; set; }

        /// <summary>Vector space of <c>/speaker-embedding</c> (e.g. <c>xtts-v2-speaker</c>); null without one.</summary>
        public string? speaker_encoder { get; set; }
    }

    /// <summary>Response of EchokrauTTS <c>POST /speaker-embedding</c>.</summary>
    public class EchokrauTtsSpeakerEmbeddingResponse
    {
        public float[]? embedding { get; set; }
        public string? encoder { get; set; }
        public int dim { get; set; }
        public double seconds { get; set; }
    }

    /// <summary>A speaker vector plus the encoder that made it (vectors of different encoders never mix).</summary>
    public sealed record SpeakerEmbedding(float[] Vector, string Encoder);

    /// <summary>Response of EchokrauTTS <c>POST /asr-check</c>.</summary>
    public class EchokrauTtsAsrCheckResponse
    {
        public string? text { get; set; }
        public double seconds { get; set; }
    }
}
