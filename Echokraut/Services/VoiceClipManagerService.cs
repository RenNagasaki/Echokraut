using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

public class VoiceClipManagerService : IVoiceClipManagerService
{
    private readonly IDatabaseService _db;
    private readonly IBackendService _backend;
    private readonly IAudioFileService _audioFiles;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly INpcDataService _npcData;
    private readonly IGameObjectService _gameObjects;
    private readonly ILogService _log;
    private readonly Configuration _config;

    private VoiceMessage? _currentlyPlaying;

    public bool IsGenerating { get; private set; }
    public event Action? VoiceClipUpdated;

    public VoiceClipManagerService(
        IDatabaseService db,
        IBackendService backend,
        IAudioFileService audioFiles,
        IAudioPlaybackService audioPlayback,
        INpcDataService npcData,
        IGameObjectService gameObjects,
        ILogService log,
        Configuration config)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _audioPlayback.CurrentMessageChanged += msg => _currentlyPlaying = msg;
    }

    /// <summary>
    /// Get the effective player content ID for a voice clip.
    /// Clips with player placeholders use the real player ID; others use 0 (player-independent).
    /// </summary>
    private long GetEffectivePlayerId(VoiceClipEntity voiceClip)
    {
        return voiceClip.HasPlayerPlaceholder ? (long)_gameObjects.LocalPlayerContentId : 0;
    }

    public VoiceMessage BuildVoiceMessage(VoiceClipEntity voiceClip)
    {
        var character = voiceClip.Character;
        var name = character?.Name ?? "";
        var race = character != null ? (NpcRaces)character.Race : NpcRaces.Unknown;
        var gender = character != null ? (Genders)character.Gender : Genders.None;
        var bodyType = (BodyType)voiceClip.BodyType;
        var objectKind = character != null ? (ObjectKind)character.ObjectKind : ObjectKind.None;

        var npcData = new NpcMapData(objectKind)
        {
            Name = name,
            Race = race,
            RaceStr = character?.RaceStr ?? "",
            Gender = gender,
            BodyType = bodyType,
            voice = voiceClip.VoiceKey
        };

        // Resolve voice list so Voice property can look up by BackendVoice
        npcData.Voices = _npcData.GetEchokrautVoices();

        var baseEventId = _log.Start(nameof(BuildVoiceMessage), (TextSource)voiceClip.TextSource);
        var eventId = new EKEventId(baseEventId.Id, baseEventId.TextSource);

        // Substitute player name placeholders for TTS
        var playerName = _gameObjects.LocalPlayerName;
        var isMale = _gameObjects.LocalPlayerIsMale;
        var ttsText = TalkTextHelper.SubstitutePlaceholders(voiceClip.CleanedText, playerName, isMale);
        var originalText = TalkTextHelper.SubstitutePlaceholders(voiceClip.OriginalText, playerName, isMale);

        return new VoiceMessage
        {
            Text = ttsText,
            OriginalText = originalText,
            Speaker = npcData,
            Source = (TextSource)voiceClip.TextSource,
            Language = (Dalamud.Game.ClientLanguage)voiceClip.Language,
            Volume = 1.0f,
            EventId = eventId,
            Is3D = false
        };
    }

    public void PlayVoiceClip(VoiceClipEntity voiceClip)
    {
        try
        {
            var path = GetAudioPath(voiceClip);
            if (!File.Exists(path))
            {
                _log.Warning(nameof(PlayVoiceClip), $"Audio file not found: {path}",
                    new EKEventId(0, TextSource.None));
                return;
            }

            var message = BuildVoiceMessage(voiceClip);
            message.Stream = File.OpenRead(path);
            message.LoadedLocally = true;
            message.Source = TextSource.VoiceTest; // bypass dialog-closed check
            _audioPlayback.AddToQueue(message);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(PlayVoiceClip), $"Error playing voice clip {voiceClip.Id}: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    public void StopPlayback()
    {
        if (_currentlyPlaying != null)
            _audioPlayback.StopPlaying(_currentlyPlaying);
        _audioPlayback.ClearQueue(TextSource.VoiceTest);
    }

    public string GetAudioPath(VoiceClipEntity voiceClip)
    {
        // Check per-player generation record first
        var playerId = GetEffectivePlayerId(voiceClip);
        var gen = _db.GetVoiceClipGeneration(voiceClip.Id, playerId);
        if (gen != null && !string.IsNullOrEmpty(gen.SavePath))
            return gen.SavePath;

        // Legacy fallback: check old SavePath column
        if (!string.IsNullOrEmpty(voiceClip.SavePath))
            return voiceClip.SavePath;

        var message = BuildVoiceMessage(voiceClip);
        return _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
    }

    public bool HasLocalAudio(VoiceClipEntity voiceClip)
    {
        var playerId = GetEffectivePlayerId(voiceClip);
        var gen = _db.GetVoiceClipGeneration(voiceClip.Id, playerId);
        if (gen == null)
            return false;

        try
        {
            return !string.IsNullOrEmpty(gen.SavePath) && File.Exists(gen.SavePath);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> GenerateForVoiceClip(VoiceClipEntity voiceClip)
    {
        EKEventId eventId = new EKEventId(0, TextSource.None);
        try
        {
            var message = BuildVoiceMessage(voiceClip);
            eventId = message.EventId;

            if (string.IsNullOrEmpty(message.Speaker?.voice))
            {
                _log.Warning(nameof(GenerateForVoiceClip), $"No voice assigned for voice clip {voiceClip.Id}", eventId);
                return false;
            }

            _log.Info(nameof(GenerateForVoiceClip), $"Generating audio for voice clip {voiceClip.Id}: {message.Text}", eventId);

            var success = await _backend.GenerateVoice(message);
            if (success && message.Stream != null)
            {
                var savePath = _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
                await _audioFiles.WriteStreamToFile(eventId, message, message.Stream,
                    _config.LocalSaveLocation, false);

                var playerId = GetEffectivePlayerId(voiceClip);
                var playerName = _gameObjects.LocalPlayerName;
                _db.LogVoiceClipGeneration(voiceClip.Id, playerId, playerName, savePath);

                _log.Info(nameof(GenerateForVoiceClip), $"Audio saved for voice clip {voiceClip.Id}", eventId);
                if (!IsGenerating) // suppress during batch — fires once at end
                    VoiceClipUpdated?.Invoke();
                return true;
            }

            _log.Error(nameof(GenerateForVoiceClip), $"Generation failed for voice clip {voiceClip.Id}", eventId);
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(GenerateForVoiceClip), $"Error generating voice clip {voiceClip.Id}: {ex.Message}", eventId);
            return false;
        }
    }

    public bool DeleteAudioForVoiceClip(VoiceClipEntity voiceClip)
    {
        try
        {
            // Stop playback first and wait for stream release
            StopPlayback();
            Thread.Sleep(100); // Brief wait for audio engine to release file handle

            var path = GetAudioPath(voiceClip);
            if (File.Exists(path))
                File.Delete(path);

            var playerId = GetEffectivePlayerId(voiceClip);
            _db.DeleteVoiceClipGeneration(voiceClip.Id, playerId);

            _log.Info(nameof(DeleteAudioForVoiceClip), $"Deleted audio for voice clip {voiceClip.Id}: {path}",
                new EKEventId(0, TextSource.None));
            VoiceClipUpdated?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DeleteAudioForVoiceClip), $"Error deleting audio for voice clip {voiceClip.Id}: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
        return false;
    }

    public async Task GenerateAllUnsaved(IEnumerable<VoiceClipEntity> voiceClips,
        Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        var list = new List<VoiceClipEntity>();
        foreach (var vc in voiceClips)
        {
            if (string.IsNullOrEmpty(vc.VoiceKey)) continue;
            if (!HasLocalAudio(vc))
                list.Add(vc);
        }

        var total = list.Count;
        var completed = 0;

        IsGenerating = true;
        try
        {
            foreach (var voiceClip in list)
            {
                ct.ThrowIfCancellationRequested();
                await GenerateForVoiceClip(voiceClip);
                completed++;
                onProgress?.Invoke(completed, total);
            }
        }
        finally
        {
            IsGenerating = false;
            VoiceClipUpdated?.Invoke();
        }
    }

    public void DeleteAllSaved(IEnumerable<VoiceClipEntity> voiceClips,
        Action<int, int>? onProgress = null)
    {
        var list = new List<VoiceClipEntity>();
        foreach (var vc in voiceClips)
        {
            if (HasLocalAudio(vc))
                list.Add(vc);
        }

        var total = list.Count;
        var completed = 0;

        foreach (var voiceClip in list)
        {
            DeleteAudioForVoiceClip(voiceClip);
            completed++;
            onProgress?.Invoke(completed, total);
        }
    }
}
