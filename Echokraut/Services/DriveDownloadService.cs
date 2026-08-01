using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Google.Apis.Download;
using Google.Apis.Drive.v3;

namespace Echokraut.Services;

/// <summary>
/// Recursively mirrors a shared Google Drive folder tree to disk, downloading only newer files.
/// Split out of GoogleDriveSyncService (SRP: download).
/// </summary>
internal sealed class DriveDownloadService
{
    private readonly ILogService _log;
    private readonly DriveAuthProvider _auth;

    public DriveDownloadService(ILogService log, DriveAuthProvider auth)
    {
        _log = log;
        _auth = auth;
    }

    public async Task DownloadFolder(string localSavePath, string shareLink)
    {
        try
        {
            _log.Info("DownloadFolder", $"Downloading generated audio from GoogleDrive", new EKEventId(0, TextSource.None));
            _log.Debug("DownloadFolder", $"Share Path -> {shareLink}", new EKEventId(0, TextSource.None));

            if (!shareLink.Contains("drive.google.com"))
            {
                _log.Error("DownloadFolder", $"Error while downloading from GoogleDrive: Share link is not a google drive share link", new EKEventId(0, TextSource.None));
                return;
            }

            var service = await _auth.CreateDriveServiceAsync();

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
            _log.Error("DownloadFolder", $"Error while downloading from GoogleDrive: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private async Task SyncSharedFolderTreeAsync(
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
            : DriveLinkHelper.ExtractDriveFolderId(shareLinkOrFolderId);

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

    private async Task TraverseFolderAsync(
        DriveService service,
        string folderId,
        string localPathForThisFolder,
        ConcurrentBag<RemoteFile> sink,
        CancellationToken ct)
    {
        Directory.CreateDirectory(localPathForThisFolder);

        string? pageToken = null;
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

    private async Task DownloadIfNewerAsync(
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

    private DateTime ParseDriveDateTimeUtc(string iso8601)
    {
        if (string.IsNullOrWhiteSpace(iso8601))
            return DateTime.MinValue;

        var dt = DateTime.Parse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
        if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return dt;
    }

    private bool IsLikelyId(string s) =>
        !string.IsNullOrWhiteSpace(s) &&
        s.Length >= 10 &&
        Regex.IsMatch(s, @"^[a-zA-Z0-9_-]+$") &&
        !s.Contains("http", StringComparison.OrdinalIgnoreCase);

    private string MakeSafeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var cleaned = Regex.Replace(name, $"[{Regex.Escape(invalid)}]", "_");
        cleaned = cleaned.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "_";
        return cleaned;
    }

    private async Task RetryAsync(Func<Task> action, CancellationToken ct, int maxAttempts = 6)
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
        string? Md5,
        string? MimeType
    );
}
