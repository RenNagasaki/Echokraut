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

namespace Echokraut.Backend
{
    public class AlltalkBackend : ITTSBackend
    {
        public async Task<Stream> GenerateAudioStreamFromVoice(BackendData data, string voiceLine, string voice, string language)
        {
            //LogHelper.logThread("Generating Alltalk Audio");
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(1);

            HttpResponseMessage res = null;
            while (res == null)
            {
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
                    //LogHelper.logThread("Requesting...");
                    using var req = new HttpRequestMessage(HttpMethod.Get, uriBuilder.Uri);

                    res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    EnsureSuccessStatusCode(res);

                    // Copy the sound to a new buffer and enqueue it
                    //LogHelper.logThread("Getting response...");
                    var responseStream = await res.Content.ReadAsStreamAsync();
                    //LogHelper.logThread("Done");

                    return responseStream;
                }
                catch (Exception ex)
                {
                    //LogHelper.logThread(ex.ToString());
                }
            }

            return null;
        }

        public List<BackendVoiceItem> GetAvailableVoices(BackendData data)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(1);
            //LogHelper.logThread("Loading Alltalk Voices");
            var mappedVoices = new List<BackendVoiceItem>();
            var uriBuilder = new UriBuilder(data.BaseUrl) { Path = data.VoicesPath };
            var result = httpClient.GetStringAsync(uriBuilder.Uri);
            result.Wait();
            string resultStr = result.Result.Replace("\\", "");
            AlltalkVoices voices = System.Text.Json.JsonSerializer.Deserialize<AlltalkVoices>(resultStr);

            foreach (string voice in voices.voices)
            {
                if (voice == Constants.NARRATORVOICE)
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
                    var gender = splitVoice[0];
                    var race = splitVoice[1];
                    string voiceName = splitVoice[2].Replace(".wav", "");

                    var voiceItem = new BackendVoiceItem()
                    {
                        gender = gender,
                        race = race,
                        voice = voice
                    };

                    voiceItem.patchVersion = 1.0m;
                    var splitVoicePatch = voiceName.Split("@");
                    voiceItem.voiceName = splitVoicePatch[0];
                    if (splitVoicePatch.Length > 1)
                        voiceItem.patchVersion = Convert.ToDecimal(splitVoicePatch[1], new CultureInfo("en-US"));
                    mappedVoices.Add(voiceItem);

                    if (voice.Contains("NPC") && Constants.RACESFORRANDOMNPC.Contains(race))
                    {
                        voiceItem = new BackendVoiceItem()
                        {
                            gender = gender,
                            race = "Default",
                            voiceName = voiceName,
                            voice = voice
                        };
                        mappedVoices.Add(voiceItem);
                    }
                }
            }

            //LogHelper.logThread("Done");
            return mappedVoices;
        }

        public async void StopGenerating(BackendData data)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(1);
            //LogHelper.logThread("Stopping Alltalk Generation");
            HttpResponseMessage res = null;
            while (res == null)
            {
                try
                {
                    var content = new StringContent("");
                    res = await httpClient.PutAsync(data.StopPath, content);
                } catch (Exception ex)
                {
                    //LogHelper.logThread(ex.ToString());
                }
            }
        }

        public async Task<string> CheckReady(BackendData data)
        {
            HttpClient httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(data.BaseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            //LogHelper.logThread("Checking if Alltalk is ready");
            var res = await httpClient.GetAsync(data.ReadyPath);

            var responseString = await res.Content.ReadAsStringAsync();
            //LogHelper.logThread("Ready");

            return responseString;
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
            }

            return "de";
        }
    }
}
