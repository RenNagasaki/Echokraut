using Echotools.Logging.Services;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using EKConfiguration = Echokraut.DataClasses.Configuration;
using Echokraut.Services.Queue;

using ManagedBass;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services;

public class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly IVoiceMessageQueue _queue;
    private readonly ILogService _log;
    private readonly EKConfiguration _configuration;
    private readonly IFramework _framework;
    private readonly Live3DAudioEngine _audioEngine;
    private readonly Dictionary<Guid, VoiceMessage> _currentlyPlayingDictionary = new();
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _playbackTask;

    private readonly ILipSyncHelper _lipSync;
    private readonly IAudioFileService _audioFiles;

    private bool _inDialog;
    private bool _isPlaying;
    private bool _recreationStarted;
    public event Action<EKEventId>? AutoAdvanceRequested;
    public event Action<VoiceMessage?>? CurrentMessageChanged;

    public bool IsPlaying => _isPlaying;

    public bool InDialog
    {
        get => _inDialog;
        set => _inDialog = value;
    }

    public bool RecreationStarted
    {
        get => _recreationStarted;
        set => _recreationStarted = value;
    }

    public PlaybackState GetStreamState(Guid streamId) => _audioEngine.GetState(streamId);

    public AudioPlaybackService(IVoiceMessageQueue queue, ILogService log, EKConfiguration configuration, IFramework framework, ILipSyncHelper lipSync, IAudioFileService audioFiles)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        _lipSync = lipSync ?? throw new ArgumentNullException(nameof(lipSync));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));

        _audioEngine = new Live3DAudioEngine(_log);
        _audioEngine.ConfigureListener(new Vector3D(0, 0, 0), new Vector3D(0, 0, 1), new Vector3D(0, 1, 0));
        _audioEngine.SourceEnded += OnSourceEnded;

        _cancellationTokenSource = new CancellationTokenSource();
        _playbackTask = Task.Run(() => PlaybackLoopAsync(_cancellationTokenSource.Token));
    }

    public void UpdateListenerState(Vector3 position, float frX, float frY, float frZ, float toX, float toY, float toZ)
        => _audioEngine.UpdateFromGameState(position, frX, frY, frZ, toX, toY, toZ);

    public void Update3DFactors(float audibleRange)
    {
        Bass.Set3DFactors(1, audibleRange, 1);
        Bass.Apply3D();
        _log.Info(nameof(Update3DFactors), $"Updated 3D factors to: {audibleRange}", new EKEventId(0, TextSource.AddonBubble));
    }

    public void AddToQueue(VoiceMessage voiceMessage)
    {
        if (voiceMessage == null) throw new ArgumentNullException(nameof(voiceMessage));
        var isPriority = voiceMessage.Source == TextSource.AddonTalk ||
                         voiceMessage.Source == TextSource.AddonBattleTalk;
        _queue.Enqueue(voiceMessage, isPriority);
    }

    public void StopPlaying(VoiceMessage? message)
    {
        if (message == null) return;
        _isPlaying = false;
        _log.Info(nameof(StopPlaying), "Stopping voice inference", message.EventId);
        if (_audioEngine.GetState(message.StreamId) != PlaybackState.Stopped)
            _audioEngine.Stop(message.StreamId);
    }

    public void PausePlaying(VoiceMessage? message)
    {
        if (message == null) return;
        _log.Info(nameof(PausePlaying), "Pausing voice inference", message.EventId);
        if (_audioEngine.GetState(message.StreamId) == PlaybackState.Playing)
            _audioEngine.Pause(message.StreamId);
    }

    public void ResumePlaying(VoiceMessage? message)
    {
        if (message == null) return;
        _log.Info(nameof(ResumePlaying), "Resuming voice inference", message.EventId);
        if (_audioEngine.GetState(message.StreamId) == PlaybackState.Paused)
            _audioEngine.Resume(message.StreamId);
    }

    public void ClearQueue(TextSource textSource = TextSource.None)
    {
        if (textSource == TextSource.None)
            _queue.CancelAll();
        else
            _queue.CancelBySource(textSource);
    }

    private void OnSourceEnded(Guid guid)
    {
        var eventId = new EKEventId(-1, TextSource.None);
        if (!_currentlyPlayingDictionary.TryGetValue(guid, out var message))
        {
            _log.End(nameof(OnSourceEnded), eventId);
            return;
        }

        eventId = message.EventId;

        if (message.Stream != null)
        {
            if (_configuration.CreateMissingLocalSaveLocation &&
                !Directory.Exists(_configuration.LocalSaveLocation))
                Directory.CreateDirectory(_configuration.LocalSaveLocation);

            if (_configuration.SaveToLocal && !message.LoadedLocally)
            {
                if (Directory.Exists(_configuration.LocalSaveLocation))
                {
                    _log.Debug(nameof(OnSourceEnded), $"Text: {message.Text}", eventId);
                    if (!string.IsNullOrWhiteSpace(message.Text) && !message.LoadedLocally)
                        _ = _audioFiles.WriteStreamToFile(eventId, message, message.Stream, _configuration.LocalSaveLocation, _configuration.GoogleDriveUpload);
                }
                else
                {
                    _log.Error(nameof(OnSourceEnded),
                        $"Couldn't save file locally. Save location doesn't exist: {_configuration.LocalSaveLocation}",
                        eventId);
                }
            }

            message.Stream.Dispose();
        }

        _isPlaying = false;

        if (message.Source == TextSource.VoiceTest)
            CurrentMessageChanged?.Invoke(null);

        if (message.IsLastInDialogue && _configuration.AutoAdvanceTextAfterSpeechCompleted)
        {
            try
            {
                if (_inDialog)
                    _framework.RunOnFrameworkThread(() => AutoAdvanceRequested?.Invoke(eventId));
                else
                    _log.Debug(nameof(OnSourceEnded), "Not inDialog", eventId);
            }
            catch (Exception ex)
            {
                _log.Error(nameof(OnSourceEnded),
                    $"Error while 'auto advance text after speech completed': {ex}", eventId);
            }
        }

        _log.End(nameof(OnSourceEnded), eventId);
    }

    private async Task PlaybackLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_isPlaying && _queue.TryDequeueReadyToPlay(out var entry) && entry != null)
                {
                    if (entry.State == Queue.VoiceMessageState.Cancelled)
                    {
                        entry.Message.Stream?.Dispose();
                        continue;
                    }
                    await PlayAudioAsync(entry, cancellationToken);
                }

                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error(nameof(PlaybackLoopAsync), $"Error in playback loop: {ex}", new EKEventId(0, TextSource.None));
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task PlayAudioAsync(VoiceMessageEntry entry, CancellationToken cancellationToken)
    {
        var message = entry.Message;
        var eventId = message.EventId;

        // Skip dialogue audio if the dialog closed while this message was queued
        var isDialogueSource = message.Source is TextSource.AddonTalk or TextSource.AddonBattleTalk;
        if (isDialogueSource && _configuration.CancelSpeechOnTextAdvance && !_inDialog)
        {
            _log.Info(nameof(PlayAudioAsync), "Skipping queued audio — dialog closed", eventId);
            _queue.MarkAsCompleted(entry.Id);
            message.Stream?.Dispose();
            return;
        }

        try
        {
            _log.Info(nameof(PlayAudioAsync), "Playing next queue item", eventId);
            _queue.MarkAsPlaying(entry.Id);

            message.StreamId = _audioEngine.PlayStream(
                message.Stream,
                channels: 1,
                initialPosition: new Vector3D(5, 0, 2),
                use3d: message.Is3D);

            _currentlyPlayingDictionary[message.StreamId] = message;

            if (message.Source == TextSource.AddonTalk || message.Source == TextSource.VoiceTest)
                CurrentMessageChanged?.Invoke(message);

            _log.Debug(nameof(PlayAudioAsync), $"Audio volume: {message.Volume}", eventId);
            _audioEngine.SetVolume(message.StreamId, message.Volume);

            if (message.Is3D)
            {
                _audioEngine.SetSourcePoller(message.StreamId, () => new Vector3D(
                    message.SpeakerFollowObj?.Position.X ?? 0,
                    message.SpeakerFollowObj?.Position.Y ?? 0,
                    message.SpeakerFollowObj?.Position.Z ?? 0));
            }

            _ = _lipSync.TryLipSync(message);

            _isPlaying = true;
            _recreationStarted = false;

            _log.Info(nameof(PlayAudioAsync), $"Lipsync data text: {message.Speaker.Name}", eventId);

            while (_audioEngine.GetState(message.StreamId) != PlaybackState.Stopped)
                await Task.Delay(50, cancellationToken);

            _queue.MarkAsCompleted(entry.Id);
            _isPlaying = false;
        }
        catch (OperationCanceledException)
        {
            _queue.MarkAsCancelled(entry.Id);
            _isPlaying = false;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(PlayAudioAsync), $"Error playing audio: {ex}", eventId);
            _queue.MarkAsFailed(entry.Id, ex);
            _isPlaying = false;
        }
    }

    public void Dispose()
    {
        _audioEngine.SourceEnded -= OnSourceEnded;
        try
        {
            _cancellationTokenSource.Cancel();
            _playbackTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Dispose), $"Error during disposal: {ex}", new EKEventId(0, TextSource.None));
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _audioEngine.Dispose();
        }
    }
}
