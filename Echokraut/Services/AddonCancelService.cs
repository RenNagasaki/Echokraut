using System;
using Echokraut.DataClasses;
using Echokraut.Enums;

namespace Echokraut.Services;

public class AddonCancelService : IAddonCancelService
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly ILipSyncHelper _lipSync;
    private readonly ILogService _log;

    public AddonCancelService(IAudioPlaybackService audioPlayback, ILipSyncHelper lipSync, ILogService log)
    {
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _lipSync = lipSync ?? throw new ArgumentNullException(nameof(lipSync));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Cancel(VoiceMessage? message, bool dialogClosed = false)
    {
        _log.Info(nameof(Cancel), dialogClosed ? "Cancelling (dialog closed)" : "Cancelling (manual)", message?.EventId ?? new EKEventId(0, TextSource.None));

        if (dialogClosed)
        {
            _audioPlayback.ClearQueue(TextSource.AddonTalk);
            _log.Debug(nameof(Cancel), "Cleared AddonTalk queue", message?.EventId ?? new EKEventId(0, TextSource.None));
        }

        if (message != null)
        {
            _lipSync.TryStopLipSync(message);
            _audioPlayback.StopPlaying(message);
        }
    }
}
