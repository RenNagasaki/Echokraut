using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IAudioFileService
{
    string GetLocalAudioPath(string localSaveLocation, VoiceMessage voiceMessage);
    string GetParentFolderPath(string localSaveLocation, VoiceMessage voiceMessage);
    string GetSpeakerAudioPath(string localSaveLocation, string speaker);
    /// <summary>
    /// Returns the deterministic on-disk audio path for <paramref name="voiceMessage"/> if a
    /// file already lives there, otherwise <c>null</c>. Used by the live path to adopt
    /// pre-existing audio (e.g. WAVs copied from a friend's install) into the DB instead of
    /// regenerating. Pure file-system check — does not touch the DB.
    /// </summary>
    string? TryFindExistingLocalAudio(string localSaveLocation, VoiceMessage voiceMessage);
    string RemovePlayerNameInText(string text);
    string VoiceMessageToFileName(string voiceMessage);
    Task<bool> WriteStreamToFile(EKEventId eventId, VoiceMessage voiceMessage, Stream stream, string localSaveLocation, bool googleDriveUpload);
    bool RemoveSavedNpcFiles(string localSaveLocation, string speaker);
    int RemoveAllSavedFiles(string localSaveLocation);
}
