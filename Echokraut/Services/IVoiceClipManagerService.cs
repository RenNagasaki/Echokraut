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
    /// <summary>True when the clip's current audio was generated from a game-voice reference (VCM marker, plan §3/§6).</summary>
    bool IsGameVoiceClip(VoiceClipEntity voiceClip);

    /// <summary>
    /// Deletes every clip generated from a game-voice reference — files and generation rows
    /// (feature switched off, plan game-voice-reference §6). Returns the number of rows removed.
    /// </summary>
    int DeleteGameVoiceClips();

    event Action? VoiceClipUpdated;
}
