using Echokraut.DataClasses;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IAudioFileService
{
    string GetLocalAudioPath(string localSaveLocation, VoiceMessage voiceMessage);
    string GetParentFolderPath(string localSaveLocation, VoiceMessage voiceMessage);
    string GetSpeakerAudioPath(string localSaveLocation, string speaker);
    string RemovePlayerNameInText(string text);
    string VoiceMessageToFileName(string voiceMessage);
    Task<bool> WriteStreamToFile(EKEventId eventId, VoiceMessage voiceMessage, Stream stream, string localSaveLocation, bool googleDriveUpload);
    int DeleteLastNFiles(int nFilesToDelete = 10);
    int DeleteLastNMinutesFiles(int nMinutesFilesToDelete = 10);
    bool RemoveSavedNpcFiles(string localSaveLocation, string speaker);
}
