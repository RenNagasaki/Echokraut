using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Properties;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Echokraut.Helper.Data;
using Dalamud.Plugin.Services;

namespace Echokraut.Helper.Functional
{
    public static class DetectLanguageHelper
    {

        private static HttpClient httpClient;
        private static Configuration configuration;
        private static IClientState clientState;

        public static void Setup(Configuration configuration, IClientState clientState)
        {
            DetectLanguageHelper.configuration = configuration;
            DetectLanguageHelper.clientState = clientState;
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public static void Dispose()
        {
            httpClient.Dispose();
        }

        public async static Task<ClientLanguage> GetTextLanguage(string text, EKEventId eventId)
        {
            var languageString = "en";
            if (configuration.VoiceChatLanguageAPIKey.Length == 32)
            {
                try
                {
                    var detectLanguagesApiKey = configuration.VoiceChatLanguageAPIKey;
                    var uriBuilder = new UriBuilder(@"https://ws.detectlanguage.com/0.2/") { Path = "/0.2/detect" };
                    var detectData = new Dictionary<string, string>();
                    detectData.Add("q", text);
                    var httpRequestMessage = new HttpRequestMessage
                    {
                        Method = HttpMethod.Post,
                        RequestUri = uriBuilder.Uri,

                        Headers = {
                            { HttpRequestHeader.Authorization.ToString(), $"Bearer {detectLanguagesApiKey}" },
                            { HttpRequestHeader.Accept.ToString(), "application/json" }
                        },
                        Content = new FormUrlEncodedContent(detectData)
                    };
                    var response = httpClient.SendAsync(httpRequestMessage).Result;
                    var jsonResult = response.Content.ReadAsStringAsync().Result;
                    dynamic resultObj = JObject.Parse(jsonResult);

                    if (resultObj.data.detections.Count > 0)
                        languageString = resultObj.data.detections[0].language;
                    else
                        languageString = "en";
                }
                catch (Exception ex)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while detecting language. Using client language. Exception: {ex}", eventId);
                    return clientState.ClientLanguage;
                }

                var language = ClientLanguage.English;
                switch (languageString)
                {
                    case "de":
                        language = ClientLanguage.German;
                        break;
                    case "en":
                        language = ClientLanguage.English;
                        break;
                    case "ja":
                        language = ClientLanguage.Japanese;
                        break;
                    case "fr":
                        language = ClientLanguage.French;
                        break;
                }

                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found language for chat: {languageString}/{language.ToString()}", eventId);
                return language;
            }
            else
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping language detection for chat. Using client language.", eventId);

                return clientState.ClientLanguage;
            }
        }
    }
}
