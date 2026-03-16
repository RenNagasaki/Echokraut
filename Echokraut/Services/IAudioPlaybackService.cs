using Echokraut.DataClasses;
using Echokraut.Enums;
using ManagedBass;
using System;
using System.Numerics;

namespace Echokraut.Services;

public interface IAudioPlaybackService
{
    bool IsPlaying { get; }
    bool InDialog { get; set; }
    bool RecreationStarted { get; set; }

    void StopPlaying(VoiceMessage message);
    void PausePlaying(VoiceMessage message);
    void ResumePlaying(VoiceMessage message);

    void AddToQueue(VoiceMessage voiceMessage);
    void ClearQueue(TextSource textSource = TextSource.None);

    void Update3DFactors(float audibleRange);
    PlaybackState GetStreamState(Guid streamId);

    void UpdateListenerState(Vector3 position, float frX, float frY, float frZ, float toX, float toY, float toZ);

    event Action<EKEventId>? AutoAdvanceRequested;
    event Action<VoiceMessage?>? CurrentMessageChanged;
}
