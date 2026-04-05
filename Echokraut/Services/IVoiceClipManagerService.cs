using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;

namespace Echokraut.Services;

public interface IVoiceClipManagerService
{
    VoiceMessage BuildVoiceMessage(VoiceClipEntity encounter);
    Task<bool> GenerateForEncounter(VoiceClipEntity encounter);
    bool DeleteAudioForEncounter(VoiceClipEntity encounter);
    bool HasLocalAudio(VoiceClipEntity encounter);
    void PlayEncounter(VoiceClipEntity encounter);
    void StopPlayback();
    string GetAudioPath(VoiceClipEntity encounter);
    Task GenerateAllUnsaved(IEnumerable<VoiceClipEntity> encounters,
        Action<int, int>? onProgress = null, CancellationToken ct = default);
    void DeleteAllSaved(IEnumerable<VoiceClipEntity> encounters,
        Action<int, int>? onProgress = null);
    event Action? VoiceClipUpdated;
}
