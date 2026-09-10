using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;

namespace Echokraut.Backend
{
    /// <summary>
    /// <see cref="ITTSBackend"/> for the EchokrauTTS F5-TTS wrapper (FastAPI server):
    /// <c>POST /tts</c> (streaming raw PCM s16le @24 kHz mono), <c>GET /samples</c>,
    /// <c>GET /health</c>, <c>POST /cancel/{jobId}</c>.
    ///
    /// <para>The <c>/tts</c> body is raw PCM in exactly the shape the existing playback path already
    /// expects (the AllTalk streaming endpoint returns the same): the engine plays it as raw 16-bit
    /// mono at its 24000 default, and <c>WriteStreamToFile</c> wraps it into a WAV via
    /// <c>RawPcmToWav</c> (24000/16/1) on save. So this backend returns the raw stream unchanged —
    /// no WAV header is added here.</para>
    ///
    /// <para>The wrapper loads ONE language per run and rejects a mismatching per-request
    /// <c>language</c> — so the field is omitted; the server uses its loaded model.</para>
    /// </summary>
    public class EchokrauTtsBackend : ITTSBackend
    {
        private readonly ILogService _log;
        private readonly Configuration _config;

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Long-lived streaming client (same rationale as AlltalkBackend — avoid per-request socket
        // churn). Generous timeout because F5-TTS synthesis of a long line can take a while.
        private static readonly HttpClient _streamingClient = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
        })
        { Timeout = TimeSpan.FromSeconds(120) };

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        // No IAudioFileService dependency: unlike AllTalk's non-streaming branch, this backend
        // never writes to disk itself — the SaveToLocal path in AudioPlaybackService.OnSourceEnded
        // handles persistence from the returned stream.
        public EchokrauTtsBackend(Configuration config, ILogService log)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Joins the configured base URL and an endpoint path, collapsing any double slash.</summary>
        internal static string BuildUrl(string baseUrl, string path) => (baseUrl ?? "").TrimEnd('/') + path;

        /// <summary>
        /// Turns the raw /tts response body into the seekable stream the playback path consumes.
        /// <paramref name="streaming"/> true → wrap the live network stream (audio plays as it
        /// arrives). false → copy the whole body into an in-memory buffer first, so the caller only
        /// gets the stream once the full clip has been generated (no progressive playback). The
        /// returned stream is always positioned at 0 and seekable.
        /// </summary>
        internal static async Task<Stream> MaterializeAudioStream(Stream responseStream, bool streaming)
        {
            if (streaming)
                return new ReadSeekableStream(responseStream, 2146435);

            var buffered = new MemoryStream();
            await using (responseStream.ConfigureAwait(false))
                await responseStream.CopyToAsync(buffered).ConfigureAwait(false);
            buffered.Seek(0, SeekOrigin.Begin);
            return buffered;
        }

        /// <summary>Maps an EchokrauTTS health response to the literal "Ready" success token the
        /// connection-test UI matches on (case-insensitive), or a descriptive failure string.</summary>
        internal static string HealthToReady(EchokrauTtsHealthResponse? health, string rawBody)
        {
            if (string.Equals(health?.status, "ok", StringComparison.OrdinalIgnoreCase))
                return "Ready";
            var detail = string.IsNullOrWhiteSpace(health?.status) ? rawBody.Trim() : health!.status;
            return $"Not ready: {detail}";
        }

        private void AddAuth(HttpRequestMessage req)
        {
            var key = _config.EchokrauTts.ApiKey;
            if (!string.IsNullOrWhiteSpace(key))
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        }

        public List<string>? GetAvailableVoices(EKEventId eventId)
        {
            _log.Info(nameof(GetAvailableVoices), "Loading EchokrauTTS samples", eventId);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BuildUrl(_config.EchokrauTts.BaseUrl, _config.EchokrauTts.SamplesPath));
                AddAuth(req);
                var res = _httpClient.SendAsync(req).GetAwaiter().GetResult();
                res.EnsureSuccessStatusCode();
                var json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var parsed = JsonSerializer.Deserialize<EchokrauTtsSamplesResponse>(json, JsonOpts);
                // Keep the extension so BackendVoice keys match AllTalk's /api/voices convention
                // (MapVoices stores BackendVoice verbatim). /tts also resolves by the full filename.
                var list = parsed?.samples ?? new List<string>();
                _log.Info(nameof(GetAvailableVoices), $"Done, found {list.Count} samples", eventId);
                return list;
            }
            catch (Exception ex)
            {
                // null = backend unavailable (distinct from a genuinely empty list) so MapVoices
                // doesn't wipe existing voice assignments on a transient outage.
                // Warning, not Error: a backend that isn't up yet is the normal state at login,
                // and every Error entry raises Dalamud's "Echokraut is creating errors" popup.
                _log.Warning(nameof(GetAvailableVoices), ex.ToString(), eventId);
                return null;
            }
        }

        public async Task<Stream?> GenerateAudioStreamFromVoice(EKEventId eventId, VoiceMessage voiceLine, string voice, ClientLanguage language, Action<string>? onJobStarted = null)
        {
            _log.Info(nameof(GenerateAudioStreamFromVoice), "Generating EchokrauTTS Audio", eventId);
            try
            {
                // 'language' intentionally omitted — single-language server rejects a mismatch; an
                // omitted field uses the loaded model. 'voice' is the full sample filename (with
                // extension), which the wrapper resolves directly.
                var payload = JsonSerializer.Serialize(new { sample = voice, text = voiceLine.Text });
                using var req = new HttpRequestMessage(HttpMethod.Post, BuildUrl(_config.EchokrauTts.BaseUrl, _config.EchokrauTts.TtsPath))
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                AddAuth(req);

                // Streaming diagnostics: how long until the response HEADERS came back, and whether
                // we take the streaming path at all. A long header wait means the server held the
                // response; a short one means any remaining delay is further down (see the arrival
                // profile logged by Live3DAudioEngine.ReadLoop). Also settles from the log alone
                // whether Configuration.StreamingGeneration is on — with it off, buffering the whole
                // clip here is correct behaviour, not a bug.
                var headerWait = System.Diagnostics.Stopwatch.StartNew();
                var res = await _streamingClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                res.EnsureSuccessStatusCode();
                _log.Debug(nameof(GenerateAudioStreamFromVoice),
                    $"Response headers after {headerWait.ElapsedMilliseconds}ms " +
                    $"(streaming={_config.StreamingGeneration}, transferEncodingChunked={res.Headers.TransferEncodingChunked}, " +
                    $"contentLength={res.Content.Headers.ContentLength?.ToString() ?? "none"})", eventId);

                if (res.Headers.TryGetValues("X-Job-Id", out var jobIds))
                {
                    var jobId = jobIds.FirstOrDefault();
                    if (!string.IsNullOrEmpty(jobId))
                        onJobStarted?.Invoke(jobId);
                }

                var responseStream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                // Raw PCM s16le 24 kHz mono. When streaming is ON, hand the network stream straight to
                // the playback path so audio plays as it arrives. When OFF, fully buffer it here first
                // so generation completes before playback begins (parity with AllTalk's non-streaming
                // branch). Either way the returned stream is seekable, so the SaveToLocal path in
                // OnSourceEnded can re-read it (WriteStreamToFile seeks to 0 + wraps via RawPcmToWav).
                var result = await MaterializeAudioStream(responseStream, _config.StreamingGeneration).ConfigureAwait(false);
                _log.Info(nameof(GenerateAudioStreamFromVoice), "Done", eventId);
                return result;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(GenerateAudioStreamFromVoice), ex.ToString(), eventId);
                return null;
            }
        }

        public async Task<string> CheckReady(EKEventId eventId)
        {
            _log.Info(nameof(CheckReady), "Checking if EchokrauTTS is ready", eventId);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BuildUrl(_config.EchokrauTts.BaseUrl, _config.EchokrauTts.HealthPath));
                AddAuth(req);
                var res = await _httpClient.SendAsync(req).ConfigureAwait(false);
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                    return $"Server returned {(int)res.StatusCode} {res.ReasonPhrase}";
                var health = JsonSerializer.Deserialize<EchokrauTtsHealthResponse>(body, JsonOpts);
                return HealthToReady(health, body);
            }
            catch (HttpRequestException ex)
            {
                return $"Connection failed: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                return "Connection timed out";
            }
            catch (Exception ex)
            {
                return $"Unexpected error: {ex.Message}";
            }
        }

        public async Task StopGenerating(EKEventId eventId, string? jobId = null)
        {
            // No id = nothing to cancel. Deliberately does NOT fall back to a "most recent job"
            // field: that pointed at the *next* line as soon as generation moved on, so a skip
            // could abort the very line the player wanted to hear.
            if (string.IsNullOrEmpty(jobId)) return;
            _log.Info(nameof(StopGenerating), $"Cancelling EchokrauTTS job {jobId}", eventId);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, BuildUrl(_config.EchokrauTts.BaseUrl, _config.EchokrauTts.CancelPath + jobId));
                AddAuth(req);
                await _httpClient.SendAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(StopGenerating), ex.Message, eventId);
            }
        }
    }
}
