using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;

public static class GoogleDriveHelper
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

        downloadUrl =
            $"https://drive.usercontent.google.com/download?export=download&confirm={confirm}&id={id}";

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
}
