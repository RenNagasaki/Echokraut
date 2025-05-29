using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Globalization;
using Echokraut.DataClasses;
using Echokraut.Exceptions;
using Echokraut.Enums;
using Dalamud.Plugin.Services;
using System.Reflection;
using System.Threading;
using NAudio.Wave;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game;
using Echokraut.Helper.Data;
using System.Net;

namespace Echokraut.Backend
{
    public class AlltalkBackend : ITTSBackend
    {
        public async Task<Stream> GenerateAudioStreamFromVoice(EKEventId eventId, string voiceLine, string voice, ClientLanguage language)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating Alltalk Audio", eventId);
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(Plugin.Configuration.Alltalk.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(2);

            HttpResponseMessage res = null;
            try
            {
                var uriBuilder = new UriBuilder(Plugin.Configuration.Alltalk.BaseUrl);
                uriBuilder.Path = Plugin.Configuration.Alltalk.StreamPath;
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["text"] = voiceLine;
                query["voice"] = voice;
                query["language"] = getAlltalkLanguage(language);
                query["output_file"] = "ignoreme.wav";
                uriBuilder.Query = query.ToString();
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Requesting... {uriBuilder.Uri}", eventId);
                using var req = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);
                req.Version = HttpVersion.Version30;

                res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                EnsureSuccessStatusCode(res);

                // Copy the sound to a new buffer and enqueue it
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Getting response...", eventId);
                var responseStream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var readSeekableStream = new ReadSeekableStream(responseStream, 2146435);

                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Done", eventId);
                return readSeekableStream;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
            }

            return null;
        }

        public List<string> GetAvailableVoices(EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading Alltalk Voices", eventId);
            var mappedVoices = new List<string>();
            try
            {
                HttpClient httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(Plugin.Configuration.Alltalk.BaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                var uriBuilder = new UriBuilder(Plugin.Configuration.Alltalk.BaseUrl) { Path = Plugin.Configuration.Alltalk.VoicesPath };
                var result = httpClient.GetStringAsync(uriBuilder.Uri);
                result.Wait();
                string resultStr = result.Result.Replace("\\", "");
                AlltalkVoices voices = System.Text.Json.JsonSerializer.Deserialize<AlltalkVoices>(resultStr);

                foreach (string voice in voices.voices)
                {
                    mappedVoices.Add(voice);
                }
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Done", eventId);
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
            }

            return mappedVoices;
        }

        public async void StopGenerating(EKEventId eventId)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(Plugin.Configuration.Alltalk.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping Alltalk Generation", eventId);
            HttpResponseMessage res = null;
            try
            {
                var content = new StringContent("");
                res = await httpClient.PutAsync(Plugin.Configuration.Alltalk.StopPath, content).ConfigureAwait(false);
            } catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
            }
        }

        public async Task<string> CheckReady(EKEventId eventId)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(Plugin.Configuration.Alltalk.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Checking if Alltalk is ready", eventId);
            try
            {
                var res = await httpClient.GetAsync(Plugin.Configuration.Alltalk.ReadyPath).ConfigureAwait(false);

                var responseString = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Ready", eventId);
                return responseString;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
            }

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Not ready", eventId);
            return "NotReady";
        }

        private static void EnsureSuccessStatusCode(HttpResponseMessage res)
        {
            if (!res.IsSuccessStatusCode)
            {
                throw new AlltalkFailedException(res.StatusCode, "Failed to make request.");
            }
        }

        static string getAlltalkLanguage(ClientLanguage language)
        {
            switch (language)
            {
                case ClientLanguage.German:
                    return "de";
                case ClientLanguage.English:
                    return "en";
                case ClientLanguage.French:
                    return "fr";
                case ClientLanguage.Japanese:
                    return "ja";
            }

            return "de";
        }

        public async Task<bool> ReloadService(string reloadModel, EKEventId eventId)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(Plugin.Configuration.Alltalk.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Reloading Alltalk Service", eventId);
            HttpResponseMessage res = null;
            try
            {
                var content = new StringContent("");
                res = await httpClient.PostAsync(Plugin.Configuration.Alltalk.ReloadPath + reloadModel, content).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
            }

            return false;
        }
    }
}
