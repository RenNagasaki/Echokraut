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
using Echokraut.Helper;
using System.Reflection;
using System.Threading;
using NAudio.Wave;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Echokraut.Backend
{
    public class AlltalkBackend : ITTSBackend
    {
        AlltalkData data;
        Configuration configuration;
        public AlltalkBackend(AlltalkData data, Configuration config)
        {
            this.data = data;
            this.configuration = config;
        }

        public async Task<Stream> GenerateAudioStreamFromVoice(EKEventId eventId, string voiceLine, string voice, string language)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Generating Alltalk Audio", eventId);
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(2);

            HttpResponseMessage res = null;
            try
            {
                var uriBuilder = new UriBuilder(data.BaseUrl);
                uriBuilder.Path = data.StreamPath;
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["text"] = voiceLine;
                query["voice"] = voice;
                query["language"] = getAlltalkLanguage(language);
                query["output_file"] = "ignoreme.wav";
                uriBuilder.Query = query.ToString();
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Requesting... {uriBuilder.Uri}", eventId);
                using var req = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);

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

        public List<BackendVoiceItem> GetAvailableVoices(EKEventId eventId)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Loading Alltalk Voices", eventId);
            var mappedVoices = new List<BackendVoiceItem>();
            try
            {
                HttpClient httpClient = new HttpClient();
                httpClient.BaseAddress = new Uri(data.BaseUrl);
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                var uriBuilder = new UriBuilder(data.BaseUrl) { Path = data.VoicesPath };
                var result = httpClient.GetStringAsync(uriBuilder.Uri);
                result.Wait();
                string resultStr = result.Result.Replace("\\", "");
                AlltalkVoices voices = System.Text.Json.JsonSerializer.Deserialize<AlltalkVoices>(resultStr);

                foreach (string voice in voices.voices)
                {
                    if (voice.Equals(Constants.NARRATORVOICE, StringComparison.OrdinalIgnoreCase))
                    {
                        var voiceItem = new BackendVoiceItem()
                        {
                            voiceName = Constants.NARRATORVOICE.Replace(".wav", ""),
                            voice = voice
                        };
                        mappedVoices.Add(voiceItem);
                    }
                    else
                    {
                        string[] splitVoice = voice.Split('_');
                        var genderStr = splitVoice[0];
                        var raceStr = splitVoice[1];
                        string voiceName = splitVoice[2].Replace(".wav", "");

                        object gender = Gender.Male;
                        object race = NpcRaces.Default;
                        var voiceItem = new BackendVoiceItem()
                        {
                            gender = (Gender)gender,
                            race = (NpcRaces)race,
                            voiceName = voiceName,
                            voice = voice
                        };

                        if (Enum.TryParse(typeof(Gender), genderStr, true, out gender))
                        {
                            if (Enum.TryParse(typeof(NpcRaces), raceStr, true, out race))
                            {
                                voiceItem.gender = (Gender)gender;
                                voiceItem.race = (NpcRaces)race;
                            }
                            else
                                race = NpcRaces.Default;
                        }
                        else
                            gender = Gender.Male;

                        mappedVoices.Add(voiceItem);

                        if (voice.Contains("npc", StringComparison.OrdinalIgnoreCase) && Constants.RACESFORRANDOMNPC.Contains((NpcRaces)race))
                        {
                            voiceItem = new BackendVoiceItem()
                            {
                                gender = (Gender)gender,
                                race = NpcRaces.Default,
                                voiceName = voiceName,
                                voice = voice
                            };
                            mappedVoices.Add(voiceItem);
                        }
                    }
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
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Stopping Alltalk Generation", eventId);
            HttpResponseMessage res = null;
            try
            {
                var content = new StringContent("");
                res = await httpClient.PutAsync(data.StopPath, content).ConfigureAwait(false);
            } catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
            }
        }

        public async Task<string> CheckReady(EKEventId eventId)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, "Checking if Alltalk is ready", eventId);
            try
            {
                var res = await httpClient.GetAsync(data.ReadyPath).ConfigureAwait(false);

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

        static string getAlltalkLanguage(string language)
        {
            switch (language)
            {
                case "German":
                    return "de";
                case "English":
                    return "en";
                case "French":
                    return "fr";
                case "Japanese":
                    return "ja";
            }

            return "de";
        }
    }
}
