using Echotools.Logging.Services;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Services.Queue;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// Service responsible for TTS backend communication and generation queue processing
/// </summary>
public class BackendService : IBackendService, IDisposable
{
    private readonly IVoiceMessageQueue _queue;
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IAlltalkInstanceService _alltalkInstance;
    private readonly INpcDataService _npcData;
    private readonly IAudioFileService _audioFiles;
    private readonly IDatabaseService _db;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _generationTask;

    private ITTSBackend? _backend;
    private readonly Random _random;

    public event Action? VoicesMapped;
    public event Action? CharacterMapped;

    public BackendService(
        IVoiceMessageQueue queue,
        ILogService log,
        Configuration config,
        IAlltalkInstanceService alltalkInstance,
        INpcDataService npcData,
        IAudioFileService audioFiles,
        IDatabaseService db,
        IAudioPlaybackService audioPlayback)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _alltalkInstance = alltalkInstance ?? throw new ArgumentNullException(nameof(alltalkInstance));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));

        _random = new Random(Guid.NewGuid().GetHashCode());
        _cancellationTokenSource = new CancellationTokenSource();
        _generationTask = Task.Run(() => GenerationLoopAsync(_cancellationTokenSource.Token));
        _alltalkInstance.OnInstanceReady += RefreshBackend;
        Task.Run(RefreshBackend);
    }

    public void RefreshBackend()
    {
        if (_config.BackendSelection != TTSBackends.Alltalk) return;

        var canConnect = _config.Alltalk.InstanceType == AlltalkInstanceType.Remote ||
                         (_config.Alltalk.InstanceType == AlltalkInstanceType.Local && _alltalkInstance.InstanceRunning);

        if (!canConnect) return;

        var eventId = new EKEventId(0, TextSource.None);
        _log.Info(nameof(RefreshBackend), $"Initializing backend: {_config.BackendSelection}", eventId);
        _backend = new AlltalkBackend(_config, _log, _audioFiles);
        MapVoices(eventId);
    }

    public void SetBackendType(TTSBackends backendType)
    {
        if (backendType != TTSBackends.Alltalk) return;

        if (_config.Alltalk.InstanceType == AlltalkInstanceType.Remote ||
            (_config.Alltalk.InstanceType == AlltalkInstanceType.Local && _alltalkInstance.InstanceRunning))
        {
            var eventId = new EKEventId(0, TextSource.None);
            _log.Info(nameof(SetBackendType), $"Creating backend instance: {backendType}", eventId);
            _backend = new AlltalkBackend(_config, _log, _audioFiles);
            MapVoices(eventId);
        }
    }

    public bool ReloadService(string reloadModel, EKEventId eventId)
    {
        if (_backend == null) return false;
        return _backend.ReloadService(reloadModel, eventId).Result;
    }

    private void MapVoices(EKEventId eventId)
    {
        _log.Info(nameof(MapVoices), "Loading and mapping voices", eventId);
        var backendVoices = _backend!.GetAvailableVoices(eventId);

        // null means backend was unavailable (connection error, timeout, etc.)
        // — skip voice mapping entirely to avoid wiping existing voice assignments.
        if (backendVoices == null)
        {
            _log.Warning(nameof(MapVoices), "Backend unavailable, skipping voice mapping to preserve existing data", eventId);
            return;
        }

        var existingVoices = _db.GetVoices();
        var existingKeys = existingVoices.Select(v => v.BackendVoice).ToHashSet();

        var newVoices = backendVoices.FindAll(p => !existingKeys.Contains(p));

        if (newVoices.Count > 0)
        {
            _log.Debug(nameof(MapVoices), $"Adding {newVoices.Count} new voices", eventId);
            foreach (var newVoice in newVoices)
            {
                var voiceName = Path.GetFileNameWithoutExtension(newVoice);
                var newEkVoice = new EchokrautVoice
                {
                    BackendVoice = newVoice,
                    VoiceName = voiceName,
                    Volume = 1,
                    AllowedGenders = new List<Genders>(),
                    AllowedRaces = new List<NpcRaces>(),
                    IsDefault = newVoice.Equals(Constants.NARRATORVOICE, StringComparison.OrdinalIgnoreCase),
                    UseAsRandom = voiceName.Contains("NPC")
                };
                _npcData.ReSetVoiceGenders(newEkVoice, eventId);
                _npcData.ReSetVoiceRaces(newEkVoice, eventId);
                _db.UpsertVoice(EchokrautVoiceToEntity(newEkVoice));
            }
        }

        // Refresh after adds
        existingVoices = _db.GetVoices();
        var ekVoices = existingVoices.Select(VoiceEntityToEchokrautVoice).ToList();
        var oldVoices = ekVoices.FindAll(
            p => backendVoices.Find(f => f == p.BackendVoice) == null);

        if (oldVoices.Count > 0)
        {
            foreach (var oldVoice in oldVoices)
            {
                EchokrautVoice? replacement = null;
                if (oldVoice.BackendVoice.Contains("NPC"))
                {
                    var candidates = ekVoices.FindAll(
                        f => !oldVoices.Contains(f) && f.VoiceName.Contains("NPC") &&
                             f.IsAdultVoice == oldVoice.IsAdultVoice &&
                             f.IsChildVoice == oldVoice.IsChildVoice &&
                             f.IsElderVoice == oldVoice.IsElderVoice &&
                             !oldVoice.AllowedRaces.Except(f.AllowedRaces).Any());
                    replacement = candidates.Count > 0 ? candidates[_random.Next(0, candidates.Count)] : null;
                }
                else
                {
                    replacement = ekVoices.Find(
                        f => !oldVoices.Contains(f) && f.VoiceName == oldVoice.VoiceName);
                }
                _npcData.MigrateOldData(oldVoice, replacement);
                _db.DeleteVoice(oldVoice.BackendVoice);
            }
        }

        _npcData.MigrateOldData();
        ekVoices = _db.GetVoices().Select(VoiceEntityToEchokrautVoice).ToList();
        _npcData.RefreshSelectables(ekVoices);
        VoicesMapped?.Invoke();
        _log.Info(nameof(MapVoices), "Voices mapped successfully", eventId);
    }

    public bool IsBackendAvailable()
    {
        switch (_config.BackendSelection)
        {
            case TTSBackends.Alltalk:
                if (_config.Alltalk.InstanceType == AlltalkInstanceType.Local && _config.Alltalk.LocalInstall)
                    return true; // Checked elsewhere if actually running

                if (_config.Alltalk.InstanceType == AlltalkInstanceType.Remote && !string.IsNullOrWhiteSpace(_config.Alltalk.BaseUrl))
                    return true;

                if (_config.Alltalk.InstanceType == AlltalkInstanceType.None)
                    return true;
                break;
        }

        return false;
    }

    public void ProcessVoiceMessage(VoiceMessage voiceMessage)
    {
        if (voiceMessage == null) throw new ArgumentNullException(nameof(voiceMessage));
        
        var eventId = voiceMessage.EventId;
        _log.Info(nameof(ProcessVoiceMessage), $"Processing [{voiceMessage.Language}]: {voiceMessage.Text[..Math.Min(100, voiceMessage.Text.Length)]}...", eventId);
        
        // Determine priority - dialogue gets priority over bubbles
        var isPriority = voiceMessage.Source switch
        {
            TextSource.AddonTalk or 
            TextSource.AddonBattleTalk or 
            TextSource.AddonCutsceneSelectString or 
            TextSource.AddonSelectString or 
            TextSource.VoiceTest => true,
            _ => false
        };
        
        _queue.Enqueue(voiceMessage, isPriority);
    }

    public async Task<bool> GenerateVoice(VoiceMessage message)
    {
        if (_backend == null)
        {
            _log.Error(nameof(GenerateVoice), "Backend not initialized", message.EventId);
            return false;
        }

        var eventId = message.EventId;
        _log.Info(nameof(GenerateVoice), "Generating...", eventId);
        
        try
        {
            var voice = message.Speaker.Voice?.BackendVoice;
            if (string.IsNullOrEmpty(voice))
            {
                _log.Warning(nameof(GenerateVoice), "No voice assigned to speaker", eventId);
                return false;
            }
            
            var responseStream = await _backend.GenerateAudioStreamFromVoice(
                eventId, 
                message, 
                voice, 
                message.Language);
            
            if (responseStream != null)
            {
                message.Stream = responseStream;
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(GenerateVoice), ex.ToString(), eventId);
            return false;
        }
    }

    public async Task<string> CheckReady(EKEventId eventId)
    {
        if (_backend == null)
        {
            if (_config.BackendSelection != TTSBackends.Alltalk)
                return "No backend selected";

            _log.Info(nameof(CheckReady), "Backend not initialized, creating instance for connection test", eventId);
            _backend = new AlltalkBackend(_config, _log, _audioFiles);
        }

        return await _backend.CheckReady(eventId);
    }

    public void CancelAll()
    {
        _log.Info(nameof(CancelAll), "Stopping all voice processing", new EKEventId(0, TextSource.None));
        _queue.CancelAll();
    }

    public void Cancel(VoiceMessage message)
    {
        if (message == null) return;
        
        _log.Info(nameof(Cancel), "Cancelling voice message", message.EventId);
        // Queue will handle cancellation by source if needed
    }

    public void Pause(VoiceMessage message)
    {
        // Handled by AudioPlaybackService
    }

    public void Resume(VoiceMessage message)
    {
        // Handled by AudioPlaybackService
    }

    public void NotifyCharacterMapped()
    {
        CharacterMapped?.Invoke();
    }

    public void GetVoiceOrRandom(EKEventId eventId, NpcMapData npcData)
    {
        _log.Debug(nameof(GetVoiceOrRandom),
            $"Searching voice: {npcData.Voice?.VoiceName ?? ""} for NPC: {npcData.Name}", eventId);

        var ekVoices = _db.GetVoices().Select(VoiceEntityToEchokrautVoice).ToList();
        var picked = PickVoice(npcData, ekVoices);

        if (picked != npcData.Voice)
        {
            npcData.Voice = picked;
            if (picked != null)
                _npcData.SaveCharacter(npcData);
        }

        if (picked != null)
            _log.Debug(nameof(GetVoiceOrRandom), $"Voice: {picked} for NPC: {npcData.Name}", eventId);
        else
            _log.Error(nameof(GetVoiceOrRandom), $"Couldn't find voice for NPC: {npcData.Name}", eventId);
    }

    public EchokrautVoice? PickVoice(NpcMapData npcData, IList<EchokrautVoice> voices)
    {
        var voiceItem = npcData.Voice;
        var bodyType = npcData.BodyType;

        EchokrautVoice? defaultVoice = null;
        for (var i = 0; i < voices.Count; i++)
        {
            if (voices[i].IsDefault) { defaultVoice = voices[i]; break; }
        }

        if (voiceItem != null && (defaultVoice == null || voiceItem.BackendVoice != defaultVoice.BackendVoice))
            return voiceItem;

        var npcName = npcData.Name ?? string.Empty;

        // Try to find voice by name
        for (var i = 0; i < voices.Count; i++)
        {
            if (voices[i].VoiceName.Contains(npcName, StringComparison.OrdinalIgnoreCase))
                return voices[i];
        }

        // Find by race/gender
        var isGenderedRace = _npcData.IsGenderedRace(npcData.Race);
        List<EchokrautVoice>? matches = null;
        for (var i = 0; i < voices.Count; i++)
        {
            if (voices[i].FitsNpcData(npcData.Gender, npcData.Race, bodyType, isGenderedRace))
                (matches ??= new List<EchokrautVoice>()).Add(voices[i]);
        }

        if (matches != null && matches.Count > 0)
            return matches[_random.Next(0, matches.Count)];

        return defaultVoice;
    }

    private async Task GenerationLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Try to get next message pending generation
                if (_queue.TryDequeuePendingGeneration(out var entry) && entry != null)
                {
                    if (entry.State == Queue.VoiceMessageState.Cancelled)
                        continue;
                    await ProcessGenerationAsync(entry, cancellationToken);
                }
                
                // Small delay to prevent tight loop
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when shutting down
                break;
            }
            catch (Exception ex)
            {
                var eventId = new EKEventId(0, TextSource.None);
                _log.Error(nameof(GenerationLoopAsync), $"Error in generation loop: {ex}", eventId);
                await Task.Delay(1000, cancellationToken); // Back off on error
            }
        }
    }

    private async Task ProcessGenerationAsync(VoiceMessageEntry entry, CancellationToken cancellationToken)
    {
        var message = entry.Message;
        var eventId = message.EventId;

        try
        {
            // Locally-loaded audio already has a stream — skip backend generation
            if (message.LoadedLocally && message.Stream != null)
            {
                _queue.MarkAsReadyToPlay(entry.Id);
                return;
            }

            _queue.MarkAsGenerating(entry.Id);
            _log.Info(nameof(ProcessGenerationAsync), "Generating next queued audio", eventId);
            
            // Generate audio
            var success = await GenerateVoice(message);
            
            if (success && message.Stream != null)
            {
                _queue.MarkAsReadyToPlay(entry.Id);
                _log.Info(nameof(ProcessGenerationAsync), "Audio generated successfully", eventId);
            }
            else
            {
                _queue.MarkAsFailed(entry.Id, new Exception("Failed to generate audio"));
                _audioPlayback.RecreationStarted = false;
                _log.Error(nameof(ProcessGenerationAsync), "Failed to generate audio", eventId);
            }
        }
        catch (Exception ex)
        {
            _queue.MarkAsFailed(entry.Id, ex);
            _audioPlayback.RecreationStarted = false;
            _log.Error(nameof(ProcessGenerationAsync), $"Error generating audio: {ex}", eventId);
        }
    }

    private static EchokrautVoice VoiceEntityToEchokrautVoice(VoiceEntity entity)
    {
        return new EchokrautVoice
        {
            BackendVoice = entity.BackendVoice,
            voiceName = entity.VoiceName,
            IsDefault = entity.IsDefault,
            IsEnabled = entity.IsEnabled,
            UseAsRandom = entity.UseAsRandom,
            IsAdultVoice = entity.IsAdultVoice,
            IsChildVoice = entity.IsChildVoice,
            IsElderVoice = entity.IsElderVoice,
            Volume = entity.Volume,
            Note = entity.Note,
            AllowedGenders = entity.AllowedGenders?.Select(g => (Genders)g.Gender).ToList() ?? new(),
            AllowedRaces = entity.AllowedRaces?.Select(r => (NpcRaces)r.Race).ToList() ?? new()
        };
    }

    private static VoiceEntity EchokrautVoiceToEntity(EchokrautVoice voice)
    {
        var entity = new VoiceEntity
        {
            BackendVoice = voice.BackendVoice ?? "",
            VoiceName = voice.voiceName ?? "",
            IsDefault = voice.IsDefault,
            IsEnabled = voice.IsEnabled,
            UseAsRandom = voice.UseAsRandom,
            IsAdultVoice = voice.IsAdultVoice,
            IsChildVoice = voice.IsChildVoice,
            IsElderVoice = voice.IsElderVoice,
            Volume = voice.Volume,
            Note = voice.Note ?? ""
        };
        entity.AllowedGenders = voice.AllowedGenders
            .Select(g => new VoiceAllowedGenderEntity { Gender = (int)g }).ToList();
        entity.AllowedRaces = voice.AllowedRaces
            .Select(r => new VoiceAllowedRaceEntity { Race = (int)r }).ToList();
        return entity;
    }

    public void Dispose()
    {
        _alltalkInstance.OnInstanceReady -= RefreshBackend;
        try
        {
            _cancellationTokenSource.Cancel();
            _generationTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            var eventId = new EKEventId(0, TextSource.None);
            _log.Error(nameof(Dispose), $"Error during disposal: {ex}", eventId);
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
        }
    }
}
