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

        // The most recent job id from an /tts response header — used for best-effort Stop.
        private volatile string? _lastJobId;

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
                _log.Error(nameof(GetAvailableVoices), ex.ToString(), eventId);
                return null;
            }
        }

        public async Task<Stream?> GenerateAudioStreamFromVoice(EKEventId eventId, VoiceMessage voiceLine, string voice, ClientLanguage language)
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

                var res = await _streamingClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                res.EnsureSuccessStatusCode();

                if (res.Headers.TryGetValues("X-Job-Id", out var jobIds))
                    _lastJobId = jobIds.FirstOrDefault();

                var responseStream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                // Raw PCM s16le 24 kHz mono — handed straight to the playback path (seekable so the
                // SaveToLocal path in OnSourceEnded can re-read it; WriteStreamToFile seeks to 0 and
                // wraps via RawPcmToWav).
                var seekable = new ReadSeekableStream(responseStream, 2146435);
                _log.Info(nameof(GenerateAudioStreamFromVoice), "Done", eventId);
                return seekable;
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

        public Task<bool> ReloadService(string reloadModel, EKEventId eventId)
        {
            // EchokrauTTS has no live reload — switching the loaded model means restarting the
            // process with --language, which the instance service owns. No-op here.
            return Task.FromResult(true);
        }

        public async Task StopGenerating(EKEventId eventId)
        {
            var jobId = _lastJobId;
            if (string.IsNullOrEmpty(jobId)) return; // best-effort: only the last job is cancellable
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
