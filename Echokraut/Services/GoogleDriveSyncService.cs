using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <summary>
/// Facade for the Google Drive integration. Owns the periodic sync loop and exposes
/// <see cref="IGoogleDriveSyncService"/>, delegating each responsibility to a focused
/// collaborator: <see cref="DriveAuthProvider"/> (OAuth), <see cref="DriveDownloadService"/>
/// (download), <see cref="DriveUploadService"/> (upload) and <see cref="DriveLinkHelper"/>
/// (link parsing). Constructed via DI with the same (ILogService, Configuration) signature as
/// before, so ServiceBuilder/callers are unchanged.
/// </summary>
public sealed class GoogleDriveSyncService : IGoogleDriveSyncService
{
    private readonly ILogService _log;
    private readonly Configuration _config;

    private readonly DriveAuthProvider _auth;
    private readonly DriveDownloadService _download;
    private readonly DriveUploadService _upload;

    private CancellationTokenSource? _cts;
    private Task? _runningTask;

    public GoogleDriveSyncService(ILogService log, Configuration config)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _auth = new DriveAuthProvider();
        _download = new DriveDownloadService(_log, _auth);
        _upload = new DriveUploadService(_log, _auth);
    }

    public void StartSync()
    {
        if (_runningTask != null && !_runningTask.IsCompleted)
            return;

        _log.Info(nameof(StartSync), $"Starting GoogleDrive sync", new EKEventId(0, TextSource.None));
        _cts = new CancellationTokenSource();
        _runningTask = RunAsync(_cts.Token);
    }

    public void StopSync()
    {
        _log.Info(nameof(StopSync), $"Stopping GoogleDrive sync", new EKEventId(0, TextSource.None));
        _cts?.Cancel();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(60), token);

                _log.Debug(nameof(RunAsync), $"Syncing GoogleDrive", eventId);
                try
                {
                    await DownloadFolder(_config.LocalSaveLocation, _config.GoogleDriveShareLink);
                }
                catch (TaskCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Error(nameof(RunAsync), $"Periodic sync failed: {ex.Message}", eventId);
                }
            }
        }
        catch (TaskCanceledException)
        {
            _log.Debug(nameof(RunAsync), $"Stopping periodic GoogleDrive sync", eventId);
        }
    }

    public Task DownloadFolder(string localSavePath, string shareLink)
        => _download.DownloadFolder(localSavePath, shareLink);

    public async Task CreateDriveServicePkceAsync()
        => await _auth.CreateDriveServiceAsync();

    public Task<bool> UploadFile(string drivePath, string fileName, string filePath, EKEventId eventId)
        => _upload.UploadFile(drivePath, fileName, filePath, eventId);

    public Task<bool> UploadVoiceLine(string driveLink, VoiceLine voiceLine, EKEventId eventId)
        => _upload.UploadVoiceLine(driveLink, voiceLine, eventId);

    public string CheckForGoogleAndConvertToDirectDownloadLink(string link, out bool isGoogle)
        => DriveLinkHelper.CheckForGoogleAndConvertToDirectDownloadLink(link, out isGoogle);

    public HttpResponseMessage DownloadGoogleDrive(string downloadUrl, HttpResponseMessage response, HttpClient client)
        => DriveLinkHelper.DownloadGoogleDrive(downloadUrl, response, client);
}
