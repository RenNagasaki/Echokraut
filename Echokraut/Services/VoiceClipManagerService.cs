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
        return _gameObjects.GetEffectivePlayerContentId(voiceClip.HasPlayerPlaceholder);
    }

    public VoiceMessage BuildVoiceMessage(VoiceClipEntity voiceClip)
        => BuildVoiceMessageInternal(voiceClip, null, null);

    /// <summary>
    /// Build a VoiceMessage that substitutes a generic localized alias (Adventurer / Abenteurer(in) /
    /// Aventurier(ière) / 冒険者) for the player-name placeholders, instead of the local player's
    /// real name. Uses the clip's language for alias localization. Used to produce shareable
    /// gender-specific variants of placeholder clips.
    /// </summary>
    private VoiceMessage BuildVoiceMessageWithAlias(VoiceClipEntity voiceClip, bool isMale)
    {
        var clipLang = (Dalamud.Game.ClientLanguage)voiceClip.Language;
        var alias = TalkTextHelper.GetPlayerAlias(clipLang, isMale);
        return BuildVoiceMessageInternal(voiceClip, alias, isMale);
    }

    private VoiceMessage BuildVoiceMessageInternal(VoiceClipEntity voiceClip, string? overridePlayerName, bool? overrideIsMale)
    {
        var character = voiceClip.Character;
        var name = character?.Name ?? "";
        var race = character != null ? (NpcRaces)character.Race : NpcRaces.Unknown;
        var gender = character != null ? (Genders)character.Gender : Genders.None;
        var bodyType = (BodyType)voiceClip.BodyType;
        var objectKind = character != null ? (ObjectKind)character.ObjectKind : ObjectKind.None;
        var language = (Dalamud.Game.ClientLanguage)voiceClip.Language;

        // Reuse the live NpcDataService cache entry when available so EnsureFittingVoice's
        // mutations land on the same NpcMapData instance the Edit window reads from. Building
        // a fresh transient (the original behaviour) lost the auto-assigned voice for the
        // user's view: Edit kept showing "(none)" and the next clip's BuildVoiceMessage
        // started over from an empty resolvedVoice, retriggering the auto-pick on every dialog.
        var liveCache = objectKind == ObjectKind.Pc ? _npcData.MappedPlayers : _npcData.MappedNpcs;
        var npcData = liveCache.Find(n =>
            n.Name == name && n.Gender == gender && n.Race == race && n.Language == language);

        if (npcData == null)
        {
            // No live entry yet — fall back to a transient. The voice resolution mirrors the
            // original: clip snapshot first (preserves history if regenerated under a specific
            // voice), then character row (handles voice assigned via Edit Character after the
            // clip was first logged).
            var resolvedVoice = !string.IsNullOrEmpty(voiceClip.VoiceKey)
                ? voiceClip.VoiceKey
                : (character?.VoiceKey ?? "");

            npcData = new NpcMapData(objectKind)
            {
                Name = name,
                Race = race,
                RaceStr = character?.RaceStr ?? "",
                Gender = gender,
                BodyType = bodyType,
                Language = language,
                voice = resolvedVoice,
            };
        }
        else
        {
            // Body type can vary per clip even for the same character (a child line for an
            // adult NPC, etc.). Don't write it back onto the shared cache entry — clone the
            // surface for this message instead, but keep the voice/voiceList aliasing so
            // EnsureFittingVoice still updates the cache.
            npcData.BodyType = bodyType;
            // If the clip's own snapshot pins a specific voice (e.g. regenerate-under-voice
            // workflow), prefer that for the message but don't overwrite the cache's current
            // voice — only write back if the cache is empty so we still get a valid pick.
            if (string.IsNullOrEmpty(npcData.voice) && !string.IsNullOrEmpty(voiceClip.VoiceKey))
                npcData.voice = voiceClip.VoiceKey;
        }

        // Resolve voice list so Voice property can look up by BackendVoice
        npcData.Voices = _npcData.GetEchokrautVoices();

        var baseEventId = _log.Start(nameof(BuildVoiceMessage), (TextSource)voiceClip.TextSource);
        var eventId = new EKEventId(baseEventId.Id, baseEventId.TextSource);

        // Substitute player name placeholders for TTS. Caller can override with an alias
        // (e.g. "Abenteurer") to produce a shareable variant.
        var playerName = overridePlayerName ?? _gameObjects.LocalPlayerName;
        var isMale = overrideIsMale ?? _gameObjects.LocalPlayerIsMale;
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

        // None-mode short-circuit — also guards against the (slightly costly) EnsureFittingVoice
        // call below. BackendService.GenerateVoice has its own gate as defense in depth.
        if (!_config.HasLiveGeneration)
        {
            _log.Debug(nameof(GenerateForVoiceClip), $"Skipping generation for clip {voiceClip.Id}: live generation disabled (InstanceType=None)", eventId);
            return false;
        }

        try
        {
            var message = BuildVoiceMessage(voiceClip);
            eventId = message.EventId;

            // Auto-assign / re-assign a voice if the current one doesn't fit the NPC
            // (none assigned, voice disabled, gender/race/bodytype mismatch after a character edit, etc.).
            // BackendService.EnsureFittingVoice handles character-side persistence; we mirror the
            // resulting voice into the clip snapshot so future generations skip the lookup entirely.
            if (message.Speaker != null && _backend.EnsureFittingVoice(message.Speaker, eventId))
                _db.UpdateVoiceClipVoiceKey(voiceClip.Id, message.Speaker.voice ?? "");

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
                _db.LogVoiceClipGeneration(voiceClip.Id, playerId, playerName, savePath,
                    message.Speaker?.voice ?? "");

                _log.Info(nameof(GenerateForVoiceClip), $"Audio saved for voice clip {voiceClip.Id}", eventId);

                // Auto-generate shareable male + female alias variants for placeholder clips when
                // the user opted in. Each runs as a separate backend call — failure of an alias
                // variant doesn't fail the main generation.
                if (_config.AutoGenerateShareableAliases && voiceClip.HasPlayerPlaceholder)
                {
                    await GenerateAliasVariant(voiceClip, isMale: true);
                    await GenerateAliasVariant(voiceClip, isMale: false);
                }

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

    /// <summary>
    /// Generate one shareable alias variant (male or female) of a placeholder clip and persist
    /// it as its own row in voice_clip_generations (player_content_id=0, alias_gender=1|2).
    /// Best-effort — failures are logged at warning level and do not affect the main generation.
    /// </summary>
    private async Task GenerateAliasVariant(VoiceClipEntity voiceClip, bool isMale)
    {
        var aliasGender = isMale ? 1 : 2;
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            var message = BuildVoiceMessageWithAlias(voiceClip, isMale);
            eventId = message.EventId;

            if (string.IsNullOrEmpty(message.Speaker?.voice))
            {
                _log.Warning(nameof(GenerateAliasVariant),
                    $"No voice on clip {voiceClip.Id}; skipping alias gender={aliasGender}", eventId);
                return;
            }

            var success = await _backend.GenerateVoice(message);
            if (!success || message.Stream == null)
            {
                _log.Warning(nameof(GenerateAliasVariant),
                    $"Backend generation failed for alias gender={aliasGender} on clip {voiceClip.Id}", eventId);
                return;
            }

            var savePath = _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
            await _audioFiles.WriteStreamToFile(eventId, message, message.Stream,
                _config.LocalSaveLocation, false);

            // playerContentId = 0 → alias rows are not bound to any specific player.
            // playerName carries the alias string for human-readable identification in the DB.
            _db.LogVoiceClipGeneration(voiceClip.Id, 0,
                TalkTextHelper.GetPlayerAlias((Dalamud.Game.ClientLanguage)voiceClip.Language, isMale),
                savePath,
                message.Speaker?.voice ?? "",
                aliasGender);

            _log.Info(nameof(GenerateAliasVariant),
                $"Alias variant gender={aliasGender} saved for clip {voiceClip.Id}: {savePath}", eventId);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(GenerateAliasVariant),
                $"Alias gender={aliasGender} for clip {voiceClip.Id} failed: {ex.Message}", eventId);
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
        // Pre-check: if AllTalk isn't reachable, abort early instead of blasting hundreds
        // of failed generation attempts into the log.
        if (!await _backend.IsBackendReachableAsync())
        {
            _log.Warning(nameof(GenerateAllUnsaved),
                "Backend not reachable — bulk generation aborted. Check AllTalk connection.",
                new EKEventId(0, TextSource.None));
            return;
        }

        var list = new List<VoiceClipEntity>();
        foreach (var vc in voiceClips)
        {
            // Don't pre-filter on voice key — GenerateForVoiceClip auto-assigns a fitting voice
            // via PickVoice when none is set or the current one doesn't fit. Only skip clips
            // that already have generated audio.
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
