using Echokraut.DataClasses;
using System.Net.Http;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IGoogleDriveSyncService
{
    void StartSync();
    void StopSync();
    void DownloadFolder(string localSavePath, string shareLink);
    Task CreateDriveServicePkceAsync();
    Task<bool> UploadFile(string drivePath, string fileName, string filePath, EKEventId eventId);
    Task<bool> UploadVoiceLine(string driveLink, VoiceLine voiceLine, EKEventId eventId);
    string CheckForGoogleAndConvertToDirectDownloadLink(string link, out bool isGoogle);
    HttpResponseMessage DownloadGoogleDrive(string downloadUrl, HttpResponseMessage response, HttpClient client);
}
