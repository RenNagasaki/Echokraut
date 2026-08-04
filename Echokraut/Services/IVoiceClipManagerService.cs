using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;

namespace Echokraut.Services;

public interface IVoiceClipManagerService
{
    bool IsGenerating { get; }
    VoiceMessage BuildVoiceMessage(VoiceClipEntity voiceClip);
    Task<bool> GenerateForVoiceClip(VoiceClipEntity voiceClip);
    bool DeleteAudioForVoiceClip(VoiceClipEntity voiceClip);
    bool HasLocalAudio(VoiceClipEntity voiceClip);
    void PlayVoiceClip(VoiceClipEntity voiceClip);
    void StopPlayback();
    string GetAudioPath(VoiceClipEntity voiceClip);
    Task GenerateAllUnsaved(IEnumerable<VoiceClipEntity> voiceClips,
        Action<int, int>? onProgress = null, CancellationToken ct = default);
    void DeleteAllSaved(IEnumerable<VoiceClipEntity> voiceClips,
        Action<int, int>? onProgress = null);
    event Action? VoiceClipUpdated;
}
