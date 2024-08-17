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

namespace Echokraut.Helper.Functional
{
    public static class DetectLanguageHelper
    {

        static HttpClient httpClient;

        public static void Setup()
        {
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
            try
            {
                var detectLanguagesApiKey = JsonSerializer.Deserialize<List<string>>(Resources.ApiKeys)[0];
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
                languageString = resultObj.data.detections[0].language;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while detecting language: {ex}", eventId);
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
    }
}
