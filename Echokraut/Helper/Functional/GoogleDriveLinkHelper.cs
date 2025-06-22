using System;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Echokraut.Helper.Functional;

public static class GoogleDriveLinkHelper
{
        public static string CheckForGoogleAndConvertToDirectDownloadLink(string link, out bool isGoogle)
        {
            isGoogle = false;
            if (string.IsNullOrWhiteSpace(link))
                return link;

            try
            {
                string fileId = null;

                if (link.Contains("google"))
                {
                    if (link.Contains("id="))
                    {
                        var parts = link.Split(new[] { "id=" }, StringSplitOptions.None);
                        fileId = parts[1].Split('&')[0];
                    }
                    else if (link.Contains("/d/"))
                    {
                        var parts = link.Split(new[] { "/d/" }, StringSplitOptions.None);
                        fileId = parts[1].Split('/')[0];
                    }

                    isGoogle = true;

                    if (string.IsNullOrEmpty(fileId))
                        return link;

                    return $"https://drive.google.com/uc?export=download&id={fileId}";
                }
            }
            catch
            {
                return link;
            }

            return link;
        }

        public static HttpResponseMessage DownloadGoogleDrive(string downloadUrl, HttpResponseMessage response, HttpClient client)
        {
            var content = response.Content.ReadAsStringAsync();

            var confirm = GetHiddenGoogleDriveInput(content.Result, "confirm");
            var id = GetHiddenGoogleDriveInput(content.Result, "id");

            if (string.IsNullOrEmpty(confirm) || string.IsNullOrEmpty(id))
            {
                throw new Exception("No google download parameters found.");
            }

            // Schritt 2: Zusammenbauen des echten Download-Links
            downloadUrl =
                $"https://drive.usercontent.google.com/download?export=download&confirm={confirm}&id={id}";

            // Schritt 3: Datei herunterladen
            var downloadResponse = client.GetAsync(downloadUrl).Result;
            if (!downloadResponse.IsSuccessStatusCode)
            {
                throw new Exception("Error while downloading: " + downloadResponse.StatusCode);
            }

            return downloadResponse;
        }

        public static string GetHiddenGoogleDriveInput(string html, string name)
        {
            var match = Regex.Match(html, $"<input[^>]*name=[\"']{name}[\"'][^>]*value=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        public static string ExtractGoogleDriveFileId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            // Muster 1: /file/d/FILE_ID
            var match1 = Regex.Match(url, @"\/file\/d\/([a-zA-Z0-9_-]+)");
            if (match1.Success)
                return match1.Groups[1].Value;

            // Muster 2: ?id=FILE_ID
            var match2 = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]+)");
            if (match2.Success)
                return match2.Groups[1].Value;

            return null; // Keine ID gefunden
        }
}
