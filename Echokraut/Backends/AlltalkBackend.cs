using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Exceptions;
using Dalamud.Game;
using Echokraut.Services;
using Echotools.Logging.Services;
using System.Net;

namespace Echokraut.Backend
{
    public class AlltalkBackend : ITTSBackend
    {
        private readonly ILogService _log;
        private readonly Configuration _configuration;
        private readonly IAudioFileService _audioFiles;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public AlltalkBackend(Configuration configuration, ILogService log, IAudioFileService audioFiles)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
        }

        public async Task<Stream?> GenerateAudioStreamFromVoice(EKEventId eventId, VoiceMessage message, string voice, ClientLanguage language)
        {
            _log.Info(nameof(GenerateAudioStreamFromVoice), "Generating Alltalk Audio", eventId);
            var handler = new SocketsHttpHandler {
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
            };
            using var streamingClient = new HttpClient(handler);
            streamingClient.BaseAddress = new Uri(_configuration.Alltalk.BaseUrl);
            streamingClient.Timeout = TimeSpan.FromSeconds(2);

            HttpResponseMessage? res = null;
            try
            {
                var uriBuilder = new UriBuilder(_configuration.Alltalk.BaseUrl);
                uriBuilder.Path = _configuration.Alltalk.StreamPath;
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["text"] = message.Text;
                query["voice"] = voice;
                query["language"] = getAlltalkLanguage(language);
                query["output_file"] = "ignoreme.wav";
                uriBuilder.Query = query.ToString();
                _log.Debug(nameof(GenerateAudioStreamFromVoice), $"Requesting... {uriBuilder.Uri}", eventId);
                using var req = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
                req.Version = HttpVersion.Version11;
                req.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
                req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                req.Headers.TryAddWithoutValidation("Cache-Control", "no-transform");

                res = await streamingClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                EnsureSuccessStatusCode(res);

                _log.Info(nameof(GenerateAudioStreamFromVoice), "Getting response...", eventId);
                var responseStream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var readSeekableStream = new ReadSeekableStream(responseStream, 2146435);

                if (!_configuration.Alltalk.StreamingGeneration)
                {
                    await _audioFiles.WriteStreamToFile(eventId, message, readSeekableStream, _configuration.LocalSaveLocation, _configuration.GoogleDriveUpload);
                    readSeekableStream.Seek(0, SeekOrigin.Begin);
                }

                _log.Info(nameof(GenerateAudioStreamFromVoice), "Done", eventId);
                return readSeekableStream;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(GenerateAudioStreamFromVoice), ex.ToString(), eventId);
            }

            return null;
        }

        public List<string>? GetAvailableVoices(EKEventId eventId)
        {
            _log.Info(nameof(GetAvailableVoices), "Loading Alltalk Voices", eventId);
            try
            {
                var uriBuilder = new UriBuilder(_configuration.Alltalk.BaseUrl) { Path = _configuration.Alltalk.VoicesPath };
                var resultStr = _httpClient.GetStringAsync(uriBuilder.Uri).GetAwaiter().GetResult().Replace("\\", "");
                var voices = System.Text.Json.JsonSerializer.Deserialize<AlltalkVoices>(resultStr);

                var mappedVoices = new List<string>();
                foreach (string voice in voices?.voices ?? [])
                    mappedVoices.Add(voice);

                _log.Info(nameof(GetAvailableVoices), $"Done, found {mappedVoices.Count} voices", eventId);
                return mappedVoices;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(GetAvailableVoices), ex.ToString(), eventId);
                return null; // null = backend unavailable, distinct from empty list (0 voices)
            }
        }

        public async Task StopGenerating(EKEventId eventId)
        {
            _log.Info(nameof(StopGenerating), "Stopping Alltalk Generation", eventId);
            try
            {
                var content = new StringContent("");
                await _httpClient.PutAsync(_configuration.Alltalk.BaseUrl.TrimEnd('/') + _configuration.Alltalk.StopPath, content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error(nameof(StopGenerating), ex.ToString(), eventId);
            }
        }

        public async Task<string> CheckReady(EKEventId eventId)
        {
            _log.Info(nameof(CheckReady), "Checking if Alltalk is ready", eventId);
            try
            {
                var url = _configuration.Alltalk.BaseUrl.TrimEnd('/') + _configuration.Alltalk.ReadyPath;
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var msg = $"Server returned {(int)res.StatusCode} {res.ReasonPhrase}";
                    if (!string.IsNullOrWhiteSpace(body))
                        msg += $" — {body.Trim()}";
                    _log.Error(nameof(CheckReady), msg, eventId);
                    return msg;
                }
                var responseString = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                _log.Debug(nameof(CheckReady), "Ready", eventId);
                return responseString;
            }
            catch (HttpRequestException ex)
            {
                var msg = $"Connection failed: {ex.Message}";
                _log.Error(nameof(CheckReady), msg, eventId);
                return msg;
            }
            catch (TaskCanceledException)
            {
                const string msg = "Connection timed out";
                _log.Error(nameof(CheckReady), msg, eventId);
                return msg;
            }
            catch (Exception ex)
            {
                var msg = $"Unexpected error: {ex.Message}";
                _log.Error(nameof(CheckReady), msg, eventId);
                return msg;
            }
        }

        private static void EnsureSuccessStatusCode(HttpResponseMessage res)
        {
            if (!res.IsSuccessStatusCode)
                throw new AlltalkFailedException(res.StatusCode, "Failed to make request.");
        }

        static string getAlltalkLanguage(ClientLanguage language)
        {
            switch (language)
            {
                case ClientLanguage.German:  return "de";
                case ClientLanguage.English: return "en";
                case ClientLanguage.French:  return "fr";
                case ClientLanguage.Japanese: return "ja";
            }

            return "de";
        }

        public async Task<bool> ReloadService(string reloadModel, EKEventId eventId)
        {
            _log.Info(nameof(ReloadService), "Reloading Alltalk Service", eventId);
            try
            {
                var content = new StringContent("");
                await _httpClient.PostAsync(_configuration.Alltalk.BaseUrl.TrimEnd('/') + _configuration.Alltalk.ReloadPath + reloadModel, content).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(ReloadService), ex.ToString(), eventId);
            }

            return false;
        }
    }
}
