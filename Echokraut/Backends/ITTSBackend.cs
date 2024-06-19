using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Backend
{
    public interface ITTSBackend
    {
        List<BackendVoiceItem> GetAvailableVoices();
        Task<Stream> GenerateAudioStreamFromVoice(string voiceLine, string voice, string language);
        Task<string> CheckReady();
        void StopGenerating();
    }
}
