using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;

namespace Echokraut.Services;

/// <summary>
/// Uploads audio files and voice-line JSON to Google Drive, creating folders + an upload marker
/// as needed. Split out of GoogleDriveSyncService (SRP: upload).
/// </summary>
internal sealed class DriveUploadService
{
    private const string LASTUPLOAD = "lastUpload";

    private readonly ILogService _log;
    private readonly DriveAuthProvider _auth;

    public DriveUploadService(ILogService log, DriveAuthProvider auth)
    {
        _log = log;
        _auth = auth;
    }

    public async Task<bool> UploadVoiceLine(string driveLink, VoiceLine voiceLine, EKEventId eventId)
    {
        try
        {
            _log.Info("UploadVoiceLine", $"Uploading to VoiceLine GoogleDrive", eventId);
            _log.Debug("UploadVoiceLine", $"Drive Link -> {driveLink}", eventId);
            _log.Debug("UploadVoiceLine", $"Voice Line -> {voiceLine.GetDebugInfo()}", eventId);
            var service = await _auth.CreateDriveServiceAsync();

            var parentId = DriveLinkHelper.ExtractDriveFolderId(driveLink);
            _log.Debug("UploadVoiceLine", $"ParentId for Upload: {parentId}", eventId);
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
                _log.Info("UploadVoiceLine", $"Voice Line already known", eventId);

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("UploadVoiceLine", $"Error while uploading to VoiceLine GoogleDrive: {ex}", eventId);
            return false;
        }
    }

    public async Task<bool> UploadFile(string drivePath, string fileName, string filePath, EKEventId eventId)
    {
        try
        {
            _log.Info("UploadFile", $"Uploading to GoogleDrive", eventId);
            _log.Debug("UploadFile", $"Drive Path -> Echokraut/{drivePath}/{fileName}", eventId);
            var service = await _auth.CreateDriveServiceAsync();

            var echokrautId = EnsureDrivePath(service, $"Echokraut");
            var echokrautFile = await FindFile(service, $"{LASTUPLOAD}.txt", echokrautId);

            var parentId = EnsureDrivePath(service, $"Echokraut/{drivePath}");
            _log.Debug("UploadFile", $"ParentId for Upload: {parentId}", eventId);
            var existing = await FindFile(service, fileName, parentId);

            using var stream = new FileStream(filePath, FileMode.Open);
            await UpsertFileAsync(service, existing, fileName, parentId, stream, "audio/wav");

            using var echokrautStream = new MemoryStream(
                Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            await UpsertFileAsync(service, echokrautFile, $"{LASTUPLOAD}.txt", echokrautId, echokrautStream, "text/plain");

            return true;
        }
        catch (Exception ex)
        {
            _log.Error("UploadFile", $"Error while uploading to GoogleDrive: {ex}", eventId);
            return false;
        }
    }

    private string EnsureDrivePath(DriveService service, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string parentId = "root";

        foreach (var part in parts)
        {
            var listRequest = service.Files.List();
            listRequest.Q =
                $"name = '{EscapeDriveQueryString(part)}' and " +
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

    /// <summary>Creates <paramref name="name"/> under <paramref name="parentId"/>, or updates it in
    /// place when <paramref name="existing"/> is non-null. Shared by both uploads in UploadFile.</summary>
    private static async Task UpsertFileAsync(
        DriveService service,
        Google.Apis.Drive.v3.Data.File? existing,
        string name,
        string parentId,
        Stream content,
        string mimeType)
    {
        if (existing != null)
        {
            var update = service.Files.Update(new Google.Apis.Drive.v3.Data.File(), existing.Id, content, mimeType);
            await update.UploadAsync();
        }
        else
        {
            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = name,
                Parents = new List<string> { parentId }
            };
            var request = service.Files.Create(fileMetadata, content, mimeType);
            request.Fields = "id";
            await request.UploadAsync();
        }
    }

    private async Task<Google.Apis.Drive.v3.Data.File?> FindFile(
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

    private string EscapeDriveQueryString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
               .Replace("\\", "\\\\")
               .Replace("'", "\\'");
    }
}
