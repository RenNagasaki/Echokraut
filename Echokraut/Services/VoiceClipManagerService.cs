using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
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
    private readonly ILogService _log;
    private readonly Configuration _config;

    private VoiceMessage? _currentlyPlaying;

    public event Action? VoiceClipUpdated;

    public VoiceClipManagerService(
        IDatabaseService db,
        IBackendService backend,
        IAudioFileService audioFiles,
        IAudioPlaybackService audioPlayback,
        INpcDataService npcData,
        ILogService log,
        Configuration config)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _audioPlayback.CurrentMessageChanged += msg => _currentlyPlaying = msg;
    }

    public VoiceMessage BuildVoiceMessage(VoiceClipEntity encounter)
    {
        var character = encounter.Character;
        var name = character?.Name ?? "";
        var race = character != null ? (NpcRaces)character.Race : NpcRaces.Unknown;
        var gender = character != null ? (Genders)character.Gender : Genders.None;
        var bodyType = (BodyType)encounter.BodyType;
        var objectKind = character != null ? (ObjectKind)character.ObjectKind : ObjectKind.None;

        var npcData = new NpcMapData(objectKind)
        {
            Name = name,
            Race = race,
            RaceStr = character?.RaceStr ?? "",
            Gender = gender,
            BodyType = bodyType,
            voice = encounter.VoiceKey
        };

        // Resolve voice list so Voice property can look up by BackendVoice
        npcData.Voices = _npcData.GetEchokrautVoices();

        var baseEventId = _log.Start(nameof(BuildVoiceMessage), (TextSource)encounter.TextSource);
        var eventId = new EKEventId(baseEventId.Id, baseEventId.TextSource);

        return new VoiceMessage
        {
            Text = encounter.CleanedText,
            OriginalText = encounter.OriginalText,
            Speaker = npcData,
            Source = (TextSource)encounter.TextSource,
            Language = (Dalamud.Game.ClientLanguage)encounter.Language,
            Volume = 1.0f,
            EventId = eventId,
            Is3D = false
        };
    }

    public void PlayEncounter(VoiceClipEntity encounter)
    {
        try
        {
            var path = GetAudioPath(encounter);
            if (!File.Exists(path))
            {
                _log.Warning(nameof(PlayEncounter), $"Audio file not found: {path}",
                    new EKEventId(0, TextSource.None));
                return;
            }

            var message = BuildVoiceMessage(encounter);
            message.Stream = File.OpenRead(path);
            message.LoadedLocally = true;
            message.Source = TextSource.VoiceTest; // bypass dialog-closed check
            _audioPlayback.AddToQueue(message);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(PlayEncounter), $"Error playing encounter {encounter.Id}: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    public void StopPlayback()
    {
        if (_currentlyPlaying != null)
            _audioPlayback.StopPlaying(_currentlyPlaying);
        _audioPlayback.ClearQueue(TextSource.VoiceTest);
    }

    public string GetAudioPath(VoiceClipEntity encounter)
    {
        // Use saved path from DB if available, otherwise compute from VoiceMessage
        if (!string.IsNullOrEmpty(encounter.SavePath))
            return encounter.SavePath;

        var message = BuildVoiceMessage(encounter);
        return _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
    }

    public bool HasLocalAudio(VoiceClipEntity encounter)
    {
        // Skip expensive BuildVoiceMessage for clips that were never generated
        if (!encounter.SavedToDisk && string.IsNullOrEmpty(encounter.SavePath))
            return false;

        try
        {
            var path = GetAudioPath(encounter);
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> GenerateForEncounter(VoiceClipEntity encounter)
    {
        EKEventId eventId = new EKEventId(0, TextSource.None);
        try
        {
            var message = BuildVoiceMessage(encounter);
            eventId = message.EventId;

            if (string.IsNullOrEmpty(message.Speaker?.voice))
            {
                _log.Warning(nameof(GenerateForEncounter), $"No voice assigned for encounter {encounter.Id}", eventId);
                return false;
            }

            _log.Info(nameof(GenerateForEncounter), $"Generating audio for encounter {encounter.Id}: {encounter.CleanedText}", eventId);

            var success = await _backend.GenerateVoice(message);
            if (success && message.Stream != null)
            {
                var savePath = _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
                await _audioFiles.WriteStreamToFile(eventId, message, message.Stream,
                    _config.LocalSaveLocation, false);
                _db.UpdateVoiceClipSaved(encounter.Id, true, savePath);
                encounter.SavedToDisk = true;
                encounter.SavePath = savePath;
                _log.Info(nameof(GenerateForEncounter), $"Audio saved for encounter {encounter.Id}", eventId);
                VoiceClipUpdated?.Invoke();
                return true;
            }

            _log.Error(nameof(GenerateForEncounter), $"Generation failed for encounter {encounter.Id}", eventId);
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(GenerateForEncounter), $"Error generating encounter {encounter.Id}: {ex.Message}", eventId);
            return false;
        }
    }

    public bool DeleteAudioForEncounter(VoiceClipEntity encounter)
    {
        try
        {
            // Stop playback first and wait for stream release
            StopPlayback();
            Thread.Sleep(100); // Brief wait for audio engine to release file handle

            var path = GetAudioPath(encounter);
            if (File.Exists(path))
            {
                File.Delete(path);
                _db.UpdateVoiceClipSaved(encounter.Id, false, "");
                encounter.SavedToDisk = false;
                encounter.SavePath = "";
                _log.Info(nameof(DeleteAudioForEncounter), $"Deleted audio for encounter {encounter.Id}: {path}",
                    new EKEventId(0, TextSource.None));
                VoiceClipUpdated?.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DeleteAudioForEncounter), $"Error deleting audio for encounter {encounter.Id}: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
        return false;
    }

    public async Task GenerateAllUnsaved(IEnumerable<VoiceClipEntity> encounters,
        Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        var list = new List<VoiceClipEntity>();
        foreach (var e in encounters)
        {
            if (!e.SavedToDisk && !string.IsNullOrEmpty(e.VoiceKey))
                list.Add(e);
        }

        var total = list.Count;
        var completed = 0;

        foreach (var encounter in list)
        {
            ct.ThrowIfCancellationRequested();
            await GenerateForEncounter(encounter);
            completed++;
            onProgress?.Invoke(completed, total);
        }
    }

    public void DeleteAllSaved(IEnumerable<VoiceClipEntity> encounters,
        Action<int, int>? onProgress = null)
    {
        var list = new List<VoiceClipEntity>();
        foreach (var e in encounters)
        {
            if (e.SavedToDisk)
                list.Add(e);
        }

        var total = list.Count;
        var completed = 0;

        foreach (var encounter in list)
        {
            DeleteAudioForEncounter(encounter);
            completed++;
            onProgress?.Invoke(completed, total);
        }
    }
}
