using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;

namespace Echokraut.Helper.API;

public static class GoogleDriveHelper
{
    public static CancellationTokenSource? _cts;
    private static Task? _runningTask;
    private const string CLIENTID = "1009198487564-0hef5tpd5u4nul19be53iu88unqajjbk.apps.googleusercontent.com";
    private const string CLIENTSECRET = "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";
    private const string LASTUPLOAD = "lastUpload";
    
    public static void StartSync()
    {
        if (_runningTask != null && !_runningTask.IsCompleted)
            return; 

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Starting GoogleDrive sync", new EKEventId(0, TextSource.None));
        _cts = new CancellationTokenSource();
        _runningTask = RunAsync(_cts.Token);
    }

    public static void StopSync()
    {
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping GoogleDrive sync", new EKEventId(0, TextSource.None));
        _cts?.Cancel();
    }

    private static async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(60), token);
                
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Syncing GoogleDrive", new EKEventId(0, TextSource.None));
                DownloadFolder(Plugin.Configuration.LocalSaveLocation, Plugin.Configuration.GoogleDriveShareLink);
            }
        }
        catch (TaskCanceledException)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Stopping periodic GoogleDrive sync", new EKEventId(0, TextSource.None));
        }
    }
        
    public static string EnsureDrivePath(
        DriveService service,
        string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string parentId = "root";

        foreach (var part in parts)
        {
            var listRequest = service.Files.List();
            listRequest.Q =
                $"name = '{part}' and " +
                $"mimeType = 'application/vnd.google-apps.folder' and " +
                $"'{parentId}' in parents and trashed = false";
            listRequest.Fields = "files(id, name)";

            var result = listRequest.Execute();
            var folder = result.Files.FirstOrDefault();

            if (folder == null)
            {
                var newFolder = new Google.Apis.Drive.v3.Data.File
                {
                    Name = part,
                    MimeType = "application/vnd.google-apps.folder",
                    Parents = new[] { parentId }
                };

                folder = service.Files.Create(newFolder).Execute();
            }

            parentId = folder.Id;
        }

        return parentId;
    }
    
    public static async Task<DriveService> CreateDriveServicePkceAsync()
    {
        var scopes = new[]
        {
            DriveService.Scope.DriveReadonly,
            DriveService.Scope.DriveFile
        };

        var dataStore = new FileDataStore("Echokraut.Auth", false);

        var flow = new PkceGoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = CLIENTID,
                ClientSecret = CLIENTSECRET
            },
            Scopes = scopes,
            DataStore = dataStore
        });

        var credential = await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver())
                             .AuthorizeAsync("Echokraut", CancellationToken.None);

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Echokraut"
        });
    }

    public static async void DownloadFolder(string localSavePath, string shareLink)
    {
        try
        {
            LogHelper.Info("DownloadFolder", $"Downloading generated audio from GoogleDrive", new EKEventId(0, TextSource.None));
            LogHelper.Debug("DownloadFolder", $"Share Path -> {shareLink}", new EKEventId(0, TextSource.None));

            if (!shareLink.Contains("drive.google.com"))
            {
                LogHelper.Error("DownloadFolder", $"Error while downloading from GoogleDrive: Share link is not a google drive share link", new EKEventId(0, TextSource.None));
                return;
            }
                
            var service = await CreateDriveServicePkceAsync();
            
            await SyncSharedFolderTreeAsync(
                service,
                shareLinkOrFolderId: shareLink,
                localRootPath: localSavePath,
                maxParallelDownloads: 8,
                setLocalLastWriteToRemoteModifiedTime: true,
                ct: CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            LogHelper.Error("DownloadFolder", $"Error while downloading from GoogleDrive: {ex}", new EKEventId(0, TextSource.None));
        }
    }
    public static async Task SyncSharedFolderTreeAsync(
        DriveService service,
        string shareLinkOrFolderId,
        string localRootPath,
        int maxParallelDownloads = 8,
        bool setLocalLastWriteToRemoteModifiedTime = true,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(localRootPath);

        string rootFolderId = IsLikelyId(shareLinkOrFolderId)
            ? shareLinkOrFolderId
            : ExtractDriveFolderId(shareLinkOrFolderId);

        var files = new ConcurrentBag<RemoteFile>();
        await TraverseFolderAsync(service, rootFolderId, localRootPath, files, ct);

        using var semaphore = new SemaphoreSlim(maxParallelDownloads);
        var tasks = files.Select(async rf =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await DownloadIfNewerAsync(service, rf, setLocalLastWriteToRemoteModifiedTime, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);
    }

    private static async Task TraverseFolderAsync(
        DriveService service,
        string folderId,
        string localPathForThisFolder,
        ConcurrentBag<RemoteFile> sink,
        CancellationToken ct)
    {
        Directory.CreateDirectory(localPathForThisFolder);

        string pageToken = null;
        do
        {
            ct.ThrowIfCancellationRequested();

            var list = service.Files.List();
            list.Q = $"'{folderId}' in parents and trashed = false";
            list.PageSize = 1000;
            list.PageToken = pageToken;

            list.Fields = "nextPageToken, files(id,name,mimeType,modifiedTime,size,md5Checksum)";
            list.SupportsAllDrives = true;
            list.IncludeItemsFromAllDrives = true;

            var res = await list.ExecuteAsync(ct);
            pageToken = res.NextPageToken;

            foreach (var f in res.Files)
            {
                var safeName = MakeSafeFileName(f.Name);
                var targetPath = Path.Combine(localPathForThisFolder, safeName);

                if (f.MimeType == "application/vnd.google-apps.folder")
                {
                    await TraverseFolderAsync(service, f.Id, targetPath, sink, ct);
                    continue;
                }

                if (f.MimeType != null && f.MimeType.StartsWith("application/vnd.google-apps."))
                {
                    continue;
                }

                var modifiedUtc = ParseDriveDateTimeUtc(f.ModifiedTimeRaw);

                sink.Add(new RemoteFile(
                    FileId: f.Id,
                    LocalPath: targetPath,
                    ModifiedUtc: modifiedUtc,
                    Size: f.Size,
                    Md5: f.Md5Checksum,
                    MimeType: f.MimeType
                ));
            }

        } while (!string.IsNullOrEmpty(pageToken));
    }

    private static async Task DownloadIfNewerAsync(
        DriveService service,
        RemoteFile rf,
        bool setLocalLastWriteToRemoteModifiedTime,
        CancellationToken ct)
    {
        if (File.Exists(rf.LocalPath))
        {
            var localUtc = File.GetLastWriteTimeUtc(rf.LocalPath);

            if (rf.ModifiedUtc <= localUtc.AddSeconds(1))
                return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(rf.LocalPath)!);

        var tmp = rf.LocalPath + ".tmp";

        await RetryAsync(async () =>
        {
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);

            var get = service.Files.Get(rf.FileId);
            get.SupportsAllDrives = true;

            var progress = await get.DownloadAsync(fs, ct);
            if (progress.Status != DownloadStatus.Completed)
                throw progress.Exception ?? new Exception($"Download failed: {progress.Status}");

        }, ct);

        if (File.Exists(rf.LocalPath)) File.Delete(rf.LocalPath);
        File.Move(tmp, rf.LocalPath);

        if (setLocalLastWriteToRemoteModifiedTime)
        {
            File.SetLastWriteTimeUtc(rf.LocalPath, rf.ModifiedUtc);
        }
    }

    private static DateTime ParseDriveDateTimeUtc(string iso8601)
    {
        if (string.IsNullOrWhiteSpace(iso8601))
            return DateTime.MinValue;

        var dt = DateTime.Parse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
        if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return dt;
    }

    private static string ExtractDriveFolderId(string url)
    {
        var m = Regex.Match(url, @"folders/([a-zA-Z0-9_-]+)");
        if (m.Success) return m.Groups[1].Value;

        m = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]+)");
        if (m.Success) return m.Groups[1].Value;

        throw new ArgumentException("Keine Folder-ID im Link gefunden.");
    }

    private static bool IsLikelyId(string s) =>
        !string.IsNullOrWhiteSpace(s) &&
        s.Length >= 10 &&
        Regex.IsMatch(s, @"^[a-zA-Z0-9_-]+$") &&
        !s.Contains("http", StringComparison.OrdinalIgnoreCase);

    private static string MakeSafeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var cleaned = Regex.Replace(name, $"[{Regex.Escape(invalid)}]", "_");
        cleaned = cleaned.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "_";
        return cleaned;
    }

    private static async Task RetryAsync(Func<Task> action, CancellationToken ct, int maxAttempts = 6)
    {
        var delayMs = 500;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await action();
                return;
            }
            catch (Google.GoogleApiException ex) when (
                ex.HttpStatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                (int)ex.HttpStatusCode >= 500)
            {
                if (attempt == maxAttempts) throw;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
            }

            await Task.Delay(delayMs, ct);
            delayMs = Math.Min(delayMs * 2, 8000);
        }
    }

    private readonly record struct RemoteFile(
        string FileId,
        string LocalPath,
        DateTime ModifiedUtc,
        long? Size,
        string Md5,
        string MimeType
    );
    
    public static async Task<bool> UploadVoiceLine(string driveLink, VoiceLine voiceLine, EKEventId eventId)
    {
        try
        {
            LogHelper.Info("UploadVoiceLine", $"Uploading to VoiceLine GoogleDrive", eventId);
            LogHelper.Debug("UploadVoiceLine", $"Drive Link -> {driveLink}", eventId);
            LogHelper.Debug("UploadVoiceLine", $"Voice Line -> {voiceLine.GetDebugInfo()}", eventId);
            var service = await CreateDriveServicePkceAsync();

            
            var parentId = ExtractDriveFolderId(driveLink);
            LogHelper.Debug("UploadVoiceLine", $"ParentId for Upload: {parentId}", eventId);
            var existing = await FindFile(service, $"{voiceLine.GetFileName()}", parentId);

            var jsonData = JsonSerializer.Serialize(voiceLine);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonData));
            if (existing == null)
            {
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = voiceLine.GetFileName(),
                    Parents = new List<string> { parentId }
                };
                var request = service.Files.Create(fileMetadata, stream, "text/plain");
                request.Fields = "id";
                var progress = await request.UploadAsync();
                var uploadedFile = request.ResponseBody;
            }
            else
                LogHelper.Info("UploadVoiceLine", $"Voice Line already known", eventId);

            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error("UploadVoiceLine", $"Error while uploading to VoiceLine GoogleDrive: {ex}", eventId);
            return false;
        }
    }
    
    public static async Task<bool> UploadFile(string drivePath, string fileName, string filePath, EKEventId eventId)
    {
        try
        {
            LogHelper.Info("UploadFile", $"Uploading to GoogleDrive", eventId);
            LogHelper.Debug("UploadFile", $"Drive Path -> Echokraut/{drivePath}/{fileName}", eventId);
            var service = await CreateDriveServicePkceAsync();

            var echokrautId = EnsureDrivePath(service, $"Echokraut");
            var echokrautFile = await FindFile(service, $"{LASTUPLOAD}.txt", echokrautId);
            
            var parentId = EnsureDrivePath(service, $"Echokraut/{drivePath}");
            LogHelper.Debug("UploadFile", $"ParentId for Upload: {parentId}", eventId);
            var existing = await FindFile(service, fileName, parentId);

            using var stream = new FileStream(filePath, FileMode.Open);
            if (existing != null)
            {
                var update = service.Files.Update(
                    new Google.Apis.Drive.v3.Data.File(),
                    existing.Id,
                    stream,
                    "audio/wav"
                );

                await update.UploadAsync();
            }
            else
            {
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = fileName,
                    Parents = new List<string> { parentId }
                };
                var request = service.Files.Create(fileMetadata, stream, "audio/wav");
                request.Fields = "id";
                var progress = await request.UploadAsync();
                var uploadedFile = request.ResponseBody;
            }
            
            using var echokrautStream = new MemoryStream(
                Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString(
                                           "O",
                                           CultureInfo.InvariantCulture
                                       ))
            );
            
            if (echokrautFile != null)
            {
                var update = service.Files.Update(
                    new Google.Apis.Drive.v3.Data.File(),
                    echokrautFile.Id,
                    echokrautStream,
                    "text/plain"
                );

                await update.UploadAsync();
            }
            else
            {
                var fileMetadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = $"{LASTUPLOAD}.txt",
                    Parents = new List<string> { echokrautId }
                };
                var request = service.Files.Create(fileMetadata, echokrautStream, "text/plain");
                request.Fields = "id";
                var progress = await request.UploadAsync();
                var uploadedFile = request.ResponseBody;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error("UploadFile", $"Error while uploading to GoogleDrive: {ex}", eventId);
            return false;
        }
    }
    
    static string EscapeDriveQueryString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
               .Replace("\\", "\\\\")
               .Replace("'", "\\'");
    }
    
    static async Task<Google.Apis.Drive.v3.Data.File?> FindFile(
        DriveService service,
        string name,
        string parentId)
    {
        var list = service.Files.List();
        list.Q =
            $"name = '{EscapeDriveQueryString(name)}' and " +
            $"'{parentId}' in parents and trashed = false";
        list.Fields = "files(id, name)";
        var files = await list.ExecuteAsync();
        return files.Files.FirstOrDefault();
    }
    
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

    public static string ExtractGoogleDriveFileId(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match1 = Regex.Match(url, @"\/file\/d\/([a-zA-Z0-9_-]+)");
        if (match1.Success)
            return match1.Groups[1].Value;

        var match2 = Regex.Match(url, @"[?&]id=([a-zA-Z0-9_-]+)");
        if (match2.Success)
            return match2.Groups[1].Value;

        return null;
    }
}
