using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Backend
{
    public interface ITTSBackend
    {
        List<string> GetAvailableVoices(EKEventId eventId);
        Task<Stream> GenerateAudioStreamFromVoice(EKEventId eventId, VoiceMessage voiceLine, string voice, ClientLanguage language);
        Task<string> CheckReady(EKEventId eventId);
        Task<bool> ReloadService(string reloadModel, EKEventId eventId);
        void StopGenerating(EKEventId eventId);
    }
}
