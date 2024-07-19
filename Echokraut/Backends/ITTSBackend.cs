using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Backend
{
    public interface ITTSBackend
    {
        List<BackendVoiceItem> GetAvailableVoices(int eventId);
        Task<Stream> GenerateAudioStreamFromVoice(int eventId, string voiceLine, string voice, string language);
        Task<string> CheckReady(int eventId);
        void StopGenerating(int eventId);
    }
}
