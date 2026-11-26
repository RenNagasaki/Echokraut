using Echotools.Logging.Services;
using System;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;

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
        var eventId = message?.EventId ?? new EKEventId(0, TextSource.None);
        _log.Info(nameof(Cancel), dialogClosed ? "Cancelling (dialog closed)" : "Cancelling (text advanced)", eventId);

        // Flush the queue on EVERY cancellation, not just when the dialog closes. Advancing past
        // a line makes its audio obsolete too — previously only the audible stream was stopped,
        // so a line still being generated finished later and played out of nowhere (skip 2 and 3
        // quickly, and 2 speaks up whenever the backend happens to be done with it).
        // ClearQueue also bumps the source's cancellation epoch, which covers the message that is
        // still being built right now and would otherwise enqueue after this flush.
        var source = dialogClosed ? TextSource.AddonTalk : message?.Source ?? TextSource.AddonTalk;
        _audioPlayback.ClearQueue(source);
        _log.Debug(nameof(Cancel), $"Cleared {source} queue", eventId);

        if (message != null)
        {
            _lipSync.TryStopLipSync(message);
            _audioPlayback.StopPlaying(message);
        }
    }
}
