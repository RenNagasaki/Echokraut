using System;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Echokraut.Services;

/// <summary>
/// Stateless helpers for parsing Google Drive share links and scraping the confirm-token
/// download page. Split out of GoogleDriveSyncService (SRP: link handling).
/// </summary>
internal static class DriveLinkHelper
{
    /// <summary>Extracts the folder id from a Drive share URL (folders/&lt;id&gt; or ?id=&lt;id&gt;).</summary>
    public static string ExtractDriveFolderId(string url)
    {
        var m = Regex.Match(url, @"folders/([a-zA-Z0-9_-]+)");
        if (m.Success) return m.Groups[1].Value;

        m = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]+)");
        if (m.Success) return m.Groups[1].Value;

        throw new ArgumentException("Keine Folder-ID im Link gefunden.");
    }

    public static string CheckForGoogleAndConvertToDirectDownloadLink(string link, out bool isGoogle)
    {
        isGoogle = false;
        if (string.IsNullOrWhiteSpace(link))
            return link;

        try
        {
            string? fileId = null;

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

        downloadUrl =
            $"https://drive.usercontent.google.com/download?export=download&confirm={confirm}&id={id}";

        var downloadResponse = client.GetAsync(downloadUrl).Result;
        if (!downloadResponse.IsSuccessStatusCode)
        {
            throw new Exception("Error while downloading: " + downloadResponse.StatusCode);
        }

        return downloadResponse;
    }

    private static string? GetHiddenGoogleDriveInput(string html, string name)
    {
        var match = Regex.Match(html, $"<input[^>]*name=[\"']{name}[\"'][^>]*value=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
