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
        try
        {
            var filePath = GetLocalAudioPath(localSaveLocation, voiceMessage);
            _log.Debug(nameof(WriteStreamToFile), $"Saving audio locally: {filePath}", eventId);

            var parentDirectory = Path.GetDirectoryName(filePath);
            Directory.CreateDirectory(parentDirectory!);

            stream.Seek(0, SeekOrigin.Begin);
            await RawPcmToWav.CreateWaveFileAsync(filePath, stream, sampleRate: 24000, bitsPerSample: 16, channels: 1);
            _savedFiles.Add(DateTime.Now, filePath);

            if (googleDriveUpload)
                await _googleDrive.UploadFile(GetParentFolderPath(string.Empty, voiceMessage), $"{VoiceMessageToFileName(RemovePlayerNameInText(voiceMessage.OriginalText))}.wav", filePath, eventId);

            return true;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(WriteStreamToFile), $"Error while saving audio locally: {ex}", eventId);
        }

        return false;
    }

    public int DeleteLastNFiles(int nFilesToDelete = 10)
    {
        var timeStamps = _savedFiles.Keys.ToList();
        timeStamps.Sort((a, b) => DateTime.Compare(b, a));
        var file = "";
        var deletedFiles = 0;

        for (int i = 0; i < nFilesToDelete; i++)
        {
            if (_savedFiles.Count > 0)
            {
                try
                {
                    file = _savedFiles[timeStamps[0]];
                    File.Delete(file);
                    deletedFiles++;
                    _log.Info(nameof(DeleteLastNFiles), $"Deleted local saved file: {file}", new EKEventId(0, TextSource.None));
                }
                catch (FileNotFoundException) { }
                catch (Exception ex)
                {
                    _log.Error(nameof(DeleteLastNFiles), $"Error while deleting local saved file: {file} - {ex}", new EKEventId(0, TextSource.None));
                }
                _savedFiles.Remove(timeStamps[0]);
                timeStamps.RemoveAt(0);
            }
            else
                break;
        }

        return deletedFiles;
    }

    public int DeleteLastNMinutesFiles(int nMinutesFilesToDelete = 10)
    {
        var timeStamps = _savedFiles.Keys.ToList().FindAll(p => p >= DateTime.Now.AddMinutes(-nMinutesFilesToDelete));
        var file = "";
        var deletedFiles = 0;

        foreach (var timeStamp in timeStamps)
        {
            if (_savedFiles.Count > 0)
            {
                try
                {
                    file = _savedFiles[timeStamp];
                    File.Delete(file);
                    deletedFiles++;
                    _log.Info(nameof(DeleteLastNMinutesFiles), $"Deleted local saved file: {file}", new EKEventId(0, TextSource.None));
                }
                catch (FileNotFoundException) { }
                catch (Exception ex)
                {
                    _log.Error(nameof(DeleteLastNMinutesFiles), $"Error while deleting local saved file: {file} - {ex}", new EKEventId(0, TextSource.None));
                }
                _savedFiles.Remove(timeStamp);
            }
            else
                break;
        }

        return deletedFiles;
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
}
