using System;
using Echokraut.DataClasses;
using Echokraut.Enums;

namespace Echokraut.Services;

public class AddonCancelService : IAddonCancelService
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly ILipSyncHelper _lipSync;

    public AddonCancelService(IAudioPlaybackService audioPlayback, ILipSyncHelper lipSync)
    {
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _lipSync = lipSync ?? throw new ArgumentNullException(nameof(lipSync));
    }

    public void Cancel(VoiceMessage? message, bool dialogClosed = false)
    {
        if (dialogClosed)
            _audioPlayback.ClearQueue(TextSource.AddonTalk);

        if (message != null)
        {
            _lipSync.TryStopLipSync(message);
            _audioPlayback.StopPlaying(message);
        }
    }
}
