using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using ManagedBass;
using System;
using System.Numerics;

namespace Echokraut.Services;

public interface IAudioPlaybackService
{
    bool IsPlaying { get; }
    bool InDialog { get; set; }
    bool RecreationStarted { get; set; }

    void StopPlaying(VoiceMessage? message);
    void PausePlaying(VoiceMessage? message);
    void ResumePlaying(VoiceMessage? message);

    void AddToQueue(VoiceMessage voiceMessage);
    void ClearQueue(TextSource textSource = TextSource.None);

    void Update3DFactors(float audibleRange);
    PlaybackState GetStreamState(Guid streamId);

    /// <summary>Stream underruns (rebuffer pauses) since the plugin started; E2E telemetry (plan › B2).</summary>
    long UnderrunCount { get; }

    /// <summary>The volume BASS holds for the stream right now (float readback), null when the stream is gone; E2E (plan › B7).</summary>
    float? ChannelVolume(Guid streamId);

    void UpdateListenerState(Vector3 position, float frX, float frY, float frZ, float toX, float toY, float toZ);

    event Action<EKEventId>? AutoAdvanceRequested;
    event Action<VoiceMessage?>? CurrentMessageChanged;
}
