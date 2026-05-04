using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper.Functional;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Echokraut.Services;

public class AudioFileService : IAudioFileService
{
    private readonly ILogService _log;
    private readonly IGameObjectService _gameObjects;
    private readonly IGoogleDriveSyncService _googleDrive;
    private readonly Dictionary<DateTime, string> _savedFiles = new();

    public AudioFileService(ILogService log, IGameObjectService gameObjects, IGoogleDriveSyncService googleDrive)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _googleDrive = googleDrive ?? throw new ArgumentNullException(nameof(googleDrive));
    }

    public string GetLocalAudioPath(string localSaveLocation, VoiceMessage voiceMessage)
    {
        return $"{GetParentFolderPath(localSaveLocation, voiceMessage)}/{VoiceMessageToFileName(RemovePlayerNameInText(voiceMessage.OriginalText))}.wav";
    }

    public string GetParentFolderPath(string localSaveLocation, VoiceMessage voiceMessage)
    {
        return GetSpeakerAudioPath(localSaveLocation, voiceMessage.Speaker.Name);
    }

    public string GetSpeakerAudioPath(string localSaveLocation, string speaker)
    {
        var filePath = localSaveLocation;
        if (!filePath.EndsWith(@"/") && !string.IsNullOrWhiteSpace(filePath))
            filePath += @"/";

        speaker = speaker != "" ? speaker : "NOPERSON";
        filePath += $"{speaker}/";

        return filePath;
    }

    public string RemovePlayerNameInText(string text)
    {
        var name = _gameObjects.LocalPlayerName;
        if (string.IsNullOrEmpty(name))
            return text;

        var nameArr = name.Split(' ');
        text = text.Replace(name, "<PLAYERNAME>");
        if (nameArr.Length > 0 && !string.IsNullOrEmpty(nameArr[0])) text = text.Replace(nameArr[0], "<PLAYERFIRSTNAME>");
        if (nameArr.Length > 1 && !string.IsNullOrEmpty(nameArr[1])) text = text.Replace(nameArr[1], "<PLAYERLASTNAME>");

        return text;
    }

    public string VoiceMessageToFileName(string voiceMessage)
    {
        var fileName = voiceMessage;
        var temp = fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries);
        fileName = string.Join("", temp).ToLower().Replace(" ", "").Replace(".", "").Replace("!", "").Replace(",", "").Replace("-", "").Replace("_", "");
        if (fileName.Length > 120)
            fileName = fileName.Substring(0, 120);

        return fileName;
    }

    public async Task<bool> WriteStreamToFile(EKEventId eventId, VoiceMessage voiceMessage, Stream stream, string localSaveLocation, bool googleDriveUpload)
    {
        string filePath;
        try
        {
            filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);
            _log.Debug(nameof(WriteStreamToFile), $"Saving audio locally: {filePath}", eventId);

            var parentDirectory = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(parentDirectory!);

            stream.Seek(0, SeekOrigin.Begin);
            await RawPcmToWav.CreateWaveFileAsync(filePath, stream, sampleRate: 24000, bitsPerSample: 16, channels: 1);
            _savedFiles.Add(DateTime.Now, filePath);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(WriteStreamToFile), $"Error while saving audio locally: {ex}", eventId);
            return false;
        }

        // Fire-and-forget the optional drive upload. Awaiting it would delay the live-path
        // generation log (it runs in a continuation off this Task) — and a failed upload
        // is not a reason to lose the on-disk file's DB row.
        if (googleDriveUpload)
        {
            var driveFilePath = filePath;
            var parentFolder = GetParentFolderPath(string.Empty, voiceMessage);
            var driveFileName = $"{VoiceMessageToFileName(RemovePlayerNameInText(voiceMessage.OriginalText))}.wav";
            _ = Task.Run(async () =>
            {
                try
                {
                    await _googleDrive.UploadFile(parentFolder, driveFileName, driveFilePath, eventId);
                }
                catch (Exception ex)
                {
                    _log.Warning(nameof(WriteStreamToFile),
                        $"GoogleDrive upload failed for {driveFilePath}: {ex.Message}", eventId);
                }
            });
        }

        return true;
    }

    public bool RemoveSavedNpcFiles(string localSaveLocation, string speaker)
    {
        var speakerFolderPath = GetSpeakerAudioPath(localSaveLocation, speaker);

        if (Directory.Exists(speakerFolderPath))
        {
            try
            {
                Directory.Delete(speakerFolderPath, true);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(RemoveSavedNpcFiles), $"Error while deleting local saves for: {speaker} - {ex}", new EKEventId(0, TextSource.None));
            }
        }

        return false;
    }

    public int RemoveAllSavedFiles(string localSaveLocation)
    {
        if (string.IsNullOrWhiteSpace(localSaveLocation) || !Directory.Exists(localSaveLocation))
            return 0;

        var deleted = 0;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(localSaveLocation))
            {
                try { Directory.Delete(dir, true); deleted++; }
                catch (Exception ex)
                {
                    _log.Error(nameof(RemoveAllSavedFiles), $"Failed to delete {dir}: {ex}", new EKEventId(0, TextSource.None));
                }
            }
            foreach (var file in Directory.EnumerateFiles(localSaveLocation))
            {
                try { File.Delete(file); deleted++; }
                catch (Exception ex)
                {
                    _log.Error(nameof(RemoveAllSavedFiles), $"Failed to delete {file}: {ex}", new EKEventId(0, TextSource.None));
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(RemoveAllSavedFiles), $"Error enumerating {localSaveLocation}: {ex}", new EKEventId(0, TextSource.None));
        }
        return deleted;
    }
}
