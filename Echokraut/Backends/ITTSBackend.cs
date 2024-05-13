using Echokraut.DataClasses;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Echokraut.Backend
{
    public interface ITTSBackend
    {
        List<BackendVoiceItem> GetAvailableVoices(BackendData data);
        Task<Stream> GenerateAudioStreamFromVoice(BackendData data, string voiceLine, string voice, string language);
        Task<string> CheckReady(BackendData data);
        void StopGenerating(BackendData data);
    }
}
