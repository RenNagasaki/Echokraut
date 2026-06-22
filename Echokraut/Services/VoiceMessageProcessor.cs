using Echotools.Logging.Services;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Echokraut.Services;

/// <summary>
/// Orchestrates the complete voice message processing pipeline
/// Replaces the massive Plugin.Say() method with clean, testable architecture
/// </summary>
public class VoiceMessageProcessor : IVoiceMessageProcessor
{
    private readonly ILogService _log;
    private readonly ITextProcessingService _textProcessing;
    private readonly ICharacterDataService _characterData;
    private readonly ILuminaService _lumina;
    private readonly IVolumeService _volume;
    private readonly IBackendService _backend;
    private readonly IClientState _clientState;
    private readonly Configuration _config;
    private readonly ILanguageDetectionService _languageDetection;
    private readonly IJsonDataService _jsonData;
    private readonly INpcDataService _npcData;
    private readonly IGameObjectService _gameObjects;
    private readonly IDatabaseService _db;
    private readonly ILodestoneService _lodestone;
    private readonly IAudioFileService _audioFiles;

    public VoiceMessageProcessor(
        ILogService log,
        ITextProcessingService textProcessing,
        ICharacterDataService characterData,
        ILuminaService lumina,
        IVolumeService volume,
        IBackendService backend,
        IClientState clientState,
        Configuration config,
        ILanguageDetectionService languageDetection,
        IJsonDataService jsonData,
        INpcDataService npcData,
        IGameObjectService gameObjects,
        IDatabaseService db,
        ILodestoneService lodestone,
        IAudioFileService audioFiles)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _textProcessing = textProcessing ?? throw new ArgumentNullException(nameof(textProcessing));
        _characterData = characterData ?? throw new ArgumentNullException(nameof(characterData));
        _lumina = lumina ?? throw new ArgumentNullException(nameof(lumina));
        _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _languageDetection = languageDetection ?? throw new ArgumentNullException(nameof(languageDetection));
        _jsonData = jsonData ?? throw new ArgumentNullException(nameof(jsonData));
        _npcData = npcData ?? throw new ArgumentNullException(nameof(npcData));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _lodestone = lodestone ?? throw new ArgumentNullException(nameof(lodestone));
        _audioFiles = audioFiles ?? throw new ArgumentNullException(nameof(audioFiles));
    }

    /// <summary>
    /// Background enrichment for chat-only players (not currently visible).
    /// Looks up Race/Gender via Lodestone and persists if found. Best-effort, never throws to caller.
    /// </summary>
    private async Task TryEnrichPlayerFromLodestoneAsync(NpcMapData npcData, EKEventId eventId)
    {
        try
        {
            var result = await _lodestone.LookupAsync(npcData.Name, npcData.World);
            if (result == null) return;

            var oldName = npcData.Name;
            var oldGender = npcData.Gender;
            var oldRace = npcData.Race;

            npcData.Race = result.Race;
            npcData.RaceStr = result.RaceStr;
            npcData.Gender = result.Gender;

            _npcData.SaveCharacterWithOldIdentity(npcData, oldName, oldGender, oldRace);
            _log.Info(nameof(TryEnrichPlayerFromLodestoneAsync),
                $"Enriched player {npcData.Name}@{npcData.World}: Race={result.Race}, Gender={result.Gender}",
                eventId);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(TryEnrichPlayerFromLodestoneAsync),
                $"Enrichment failed for {npcData.Name}@{npcData.World}: {ex.Message}", eventId);
        }
    }

    public async Task ProcessSpeechAsync(EKEventId eventId, IGameObject? speaker, SeString speakerName, string textValue,
        string worldEnglish = "")
    {
        try
        {
            // Step 1: Check backend availability
            var onlyRequest = false;
            if (!_backend.IsBackendAvailable())
            {
                if (_config.GoogleDriveRequestVoiceLine)
                    onlyRequest = true;
                else
                {
                    _log.Info(nameof(ProcessSpeechAsync), "No backend available yet, skipping!", eventId);
                    _log.End(nameof(ProcessSpeechAsync), eventId);
                    return;
                }
            }

            var source = eventId.TextSource;
            var language = _clientState.ClientLanguage;
            _log.Debug(nameof(ProcessSpeechAsync), $"Preparing for Inference: {speakerName} - {textValue} - {source}", eventId);

            // Step 2: Clean and normalize text
            var originalText = textValue;
            var cleanText = CleanText(textValue, source, eventId, ref language, ref speaker);
            
            if (string.IsNullOrWhiteSpace(cleanText))
            {
                _log.Info(nameof(ProcessSpeechAsync), $"Text not speakable after cleaning", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            // Step 3: Get or create NPC data
            var npcData = await GetOrCreateNpcDataAsync(speaker, speakerName, cleanText, source, eventId, worldEnglish);
            
            if (npcData == null)
            {
                _log.Warning(nameof(ProcessSpeechAsync), "Failed to create NPC data", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            // Step 4: Check if NPC/voice is muted
            if (IsNpcMuted(npcData, source, speaker, eventId))
            {
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            // Step 5: Assign voice if needed
            if (string.IsNullOrEmpty(npcData.voice) && !onlyRequest && _config.Alltalk.InstanceType != AlltalkInstanceType.None)
            {
                _log.Info(nameof(ProcessSpeechAsync), "Getting voice since not set.", eventId);
                _backend.GetVoiceOrRandom(eventId, npcData);
            }

            if (string.IsNullOrEmpty(npcData.voice) && !onlyRequest && _config.Alltalk.InstanceType != AlltalkInstanceType.None)
            {
                _log.Info(nameof(ProcessSpeechAsync), "Skipping voice inference. No Voice set.", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            if (npcData.Voice != null && npcData.Voice.Volume == 0f && !onlyRequest && _config.Alltalk.InstanceType != AlltalkInstanceType.None)
            {
                _log.Info(nameof(ProcessSpeechAsync), $"Voice is muted: {npcData}", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            // Step 6: Calculate final volume
            var npcVolume = GetNpcVolume(npcData, source);
            var finalVolume = _volume.GetVoiceVolume(eventId) * (npcData.Voice?.Volume ?? 1) * npcVolume;
            _log.Debug(nameof(ProcessSpeechAsync), $"Voice volume: {finalVolume}", eventId);

            if (finalVolume <= 0)
            {
                _log.Info(nameof(ProcessSpeechAsync), "Skipping voice inference. Volume is 0.", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            // Step 7: Build voice message
            var is3d = ShouldUse3DAudio(source);
            var isDialogue = source is TextSource.AddonTalk or TextSource.AddonBattleTalk
                             or TextSource.AddonCutsceneSelectString or TextSource.AddonSelectString
                             or TextSource.VoiceTest;
            var voiceMessage = new VoiceMessage
            {
                SpeakerObj = speaker,
                SpeakerFollowObj = is3d && speaker != null ? speaker : _gameObjects.LocalPlayer,
                Source = source,
                Speaker = npcData,
                Text = cleanText,
                OriginalText = originalText,
                Language = language,
                EventId = eventId,
                OnlyRequest = onlyRequest,
                Volume = finalVolume,
                IsLastInDialogue = isDialogue,  // each processed line is self-contained → always the last
                Is3D = is3d
            };

            _log.Debug(nameof(ProcessSpeechAsync), voiceMessage.GetDebugInfo(), eventId);

            // Log voice clip to database (regardless of mute/volume state)
            LogVoiceClip(voiceMessage);

            // Update DialogState so UI controls (mute/unmute) have access to the current
            // speaker even when the NPC is muted and audio won't play.
            if (isDialogue)
                DialogState.CurrentVoiceMessage = voiceMessage;

            // Step 8: Check if dialogue is muted
            if (speaker != null && _db.GetMutedBaseIds().Contains(speaker.BaseId))
            {
                _log.Info(nameof(ProcessSpeechAsync), $"Skipping muted dialogue: {cleanText}", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            // Step 8.5: Cache hit — if we've previously generated this exact clip for this
            // player and the file is still on disk, skip the backend call and play it back.
            // Without this every dialog re-generation costs a full backend round-trip even
            // though SaveToLocal had cached the audio. Voice changes invalidate via the live
            // path's StopPlaying + RecreateInference flow, so cached files always reflect the
            // currently-assigned voice for that (clip, player) pair.
            // Also adopts orphan WAVs (file on disk, no DB row yet) — covers backups copied
            // from another install. See TryLoadCachedAudio.
            TryLoadCachedAudio(voiceMessage, eventId);

            // Step 9: Process the voice message
            _backend.ProcessVoiceMessage(voiceMessage);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(ProcessSpeechAsync), $"Error while starting voice inference: {ex}", eventId);
        }
    }

    private string? CleanText(string textValue, TextSource source, EKEventId eventId, ref Dalamud.Game.ClientLanguage language, ref IGameObject? speaker)
    {
        var cleanText = _textProcessing.StripAngleBracketedText(textValue);
        cleanText = _textProcessing.ReplaceSsmlTokens(cleanText);
        cleanText = _textProcessing.NormalizePunctuation(cleanText);
        cleanText = _config.RemoveStutters ? _textProcessing.RemoveStutters(cleanText) : cleanText;

        // Special handling for chat
        if (source == TextSource.Chat)
        {
            // Off-map chat senders (no resolvable GameObject) are still valid Player entries.
            // Race / Gender start as Unknown and get filled in asynchronously by
            // TryEnrichPlayerFromLodestoneAsync — that's why we hard-set ObjectKind.Pc for the
            // Chat source in GetOrCreateNpcDataAsync, so that enrichment branch fires.
            // SpeakerFollowObj already falls back to LocalPlayer when 3D audio is enabled but
            // the speaker isn't on this map (see VoiceMessage construction below), so we don't
            // need to drop the message just because it came from a different zone.
            if (!_config.VoiceChatIn3D)
                speaker = _gameObjects.LocalPlayer;

            language = _languageDetection.GetTextLanguage(cleanText, eventId).Result;
        }

        // Apply transformations
        cleanText = _textProcessing.ReplaceDate(cleanText, language);
        cleanText = _textProcessing.ReplaceTime(cleanText, language);
        cleanText = _textProcessing.ReplaceRomanNumbers(cleanText);
        cleanText = _textProcessing.ReplaceCurrency(cleanText);
        cleanText = _textProcessing.ReplaceIntWithVerbal(cleanText, language);
        var phoneticCorrections = _db.GetPhoneticCorrections()
            .Select(p => new PhoneticCorrection(p.OriginalText, p.CorrectedText)).ToList();
        cleanText = _textProcessing.ReplacePhonetics(cleanText, phoneticCorrections);
        cleanText = _textProcessing.AnalyzeAndImproveText(cleanText);

        if (source == TextSource.Chat)
            cleanText = _textProcessing.ReplaceEmoticons(cleanText);

        cleanText = cleanText.Trim();

        _log.Debug(nameof(CleanText), $"Cleantext: {cleanText}", eventId);

        // Validate result
        if (string.IsNullOrWhiteSpace(cleanText) || !cleanText.Any() || !_textProcessing.IsSpeakable(cleanText))
        {
            _log.Info(nameof(CleanText), $"Text not speakable: {cleanText}", eventId);
            return null;
        }

        return cleanText;
    }

    /// <summary>
    /// Resolve a fakename (e.g. "???" or "Mysterious Lady") to a single character row,
    /// disambiguating across multi-match cases. Priority:
    /// <list type="number">
    /// <item>If the alias resolves to exactly one character row, return it.</item>
    /// <item>If multiple match, prefer the row whose <c>CharacterInstance.NpcBaseId</c>
    ///     equals the live <paramref name="speaker"/>'s BaseId — that's a direct match
    ///     against the actually-spoken NPC and beats heuristics.</item>
    /// <item>Otherwise filter to characters physically present in the object table now
    ///     (anonymous "???" speakers always have their real ENpcBase spawned in the
    ///     scene, even when the dialog box hides their name).</item>
    /// <item>If still ambiguous, prefer one not yet resolved during this dialog session
    ///     (<see cref="DialogState.SpeakersResolvedThisDialog"/>). FFXIV cutscenes
    ///     typically have each actor speak with a different fakename in turn — this
    ///     tie-breaker mirrors the harvest's silent-actor heuristic.</item>
    /// </list>
    /// Returns <c>null</c> when no candidate survives. Callers leave the speaker name
    /// unchanged and fall through to existing "???"-handling in <c>AddonTalkHelper</c>.
    /// </summary>
    private int? ResolveCharacterByAlias(string fakename, Dalamud.Game.ClientLanguage language, IGameObject? speaker, EKEventId eventId)
    {
        // BaseId is authoritative — when the live speaker has a known ENpcBase AND a
        // character_instance row exists for it, that character row beats any name-based
        // alias lookup (even an unambiguous single-candidate match). The alias map can
        // be missing the real speaker entirely; falling through to a wrong-but-popular
        // candidate is what caused dialogues to land under unrelated NPCs.
        var fastPath = TryResolveByBaseId(speaker, language, fakename, eventId);
        if (fastPath.HasValue) return fastPath;

        var candidates = _db.FindCharacterIdsByAlias(fakename, (int)language);
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        // Multi-match alias and no direct BaseId hit. Try matching candidates against
        // the speaker's ENpcBase via their instance lists — slower than the direct lookup
        // but covers edge cases where the instance row hasn't been seen recently enough
        // to win the OrderBy in FindCharacterIdByNpcBaseId.
        var speakerBaseId = speaker?.BaseId ?? 0u;
        if (speakerBaseId != 0)
        {
            foreach (var charId in candidates)
            {
                var instances = _db.GetInstancesForCharacter(charId);
                if (instances.Any(i => (uint)i.NpcBaseId == speakerBaseId))
                {
                    _log.Debug(nameof(ResolveCharacterByAlias),
                        $"Alias '{fakename}' has {candidates.Count} candidates — picked {charId} via speaker.BaseId={speakerBaseId} match.",
                        eventId);
                    return charId;
                }
            }
        }

        // No BaseId match (or no speaker) — fall back to physical-presence filter.
        var spawned = _gameObjects.GetSpawnedNpcBaseIds();
        var present = candidates;
        if (spawned.Count > 0)
        {
            var filtered = candidates
                .Where(charId => _db.GetInstancesForCharacter(charId)
                    .Any(i => spawned.Contains((uint)i.NpcBaseId)))
                .ToList();
            if (filtered.Count > 0) present = filtered;
            // If no candidate is physically present, fall through with the original list —
            // the alias was registered for a reason and an inexact pick beats no pick.
        }

        if (present.Count == 1)
        {
            _log.Debug(nameof(ResolveCharacterByAlias),
                $"Alias '{fakename}' has {candidates.Count} candidates — narrowed to {present[0]} via physical presence.",
                eventId);
            return present[0];
        }

        // Still multiple. Prefer one that hasn't been used this dialog yet.
        var spoken = DialogState.SpeakersResolvedThisDialog;
        var notYetSpoken = present.FirstOrDefault(c => !spoken.Contains(c));
        var pick = notYetSpoken != 0 ? notYetSpoken : present[0];
        _log.Debug(nameof(ResolveCharacterByAlias),
            $"Alias '{fakename}' has {present.Count} present candidates — picked {pick} (not-yet-spoken tie-break={notYetSpoken != 0}).",
            eventId);
        return pick;
    }

    /// <summary>
    /// Fast-path of <see cref="ResolveCharacterByAlias"/>: if the live speaker exposes a
    /// known ENpcBase and a <c>character_instance</c> row for it exists in the same
    /// language, return that character row directly. Cutscene actors keep their real
    /// ENpcBase even when the dialog box hides the name as "???" or a fakename, so this
    /// is the most reliable identifier we have. Returns <c>null</c> when speaker is
    /// missing/zero or no instance row matches.
    /// </summary>
    private int? TryResolveByBaseId(IGameObject? speaker, Dalamud.Game.ClientLanguage language, string fakename, EKEventId eventId)
    {
        var baseId = speaker?.BaseId ?? 0u;
        if (baseId == 0) return null;
        var hit = _db.FindCharacterIdByNpcBaseId(baseId, (int)language);
        if (!hit.HasValue) return null;
        _log.Debug(nameof(ResolveCharacterByAlias),
            $"Speaker '{fakename}' resolved by BaseId={baseId} → character {hit.Value} (BaseId beats alias).",
            eventId);
        return hit;
    }

    private async Task<NpcMapData> GetOrCreateNpcDataAsync(IGameObject? speaker, SeString speakerName, string cleanText, TextSource source, EKEventId eventId, string worldEnglish = "")
    {
        var cleanSpeakerName = _textProcessing.NormalizePunctuation(speakerName.TextValue);
        // Chat senders are always Players. Without forcing this, off-map senders (speaker==null)
        // would fall through with ObjectKind.None and skip the Pc-only Lodestone enrichment
        // branch further down — leaving Race/Gender as Unknown indefinitely.
        var objectKind = source == TextSource.Chat
            ? ObjectKind.Pc
            : speaker?.ObjectKind ?? ObjectKind.None;
        var npcData = new NpcMapData(objectKind);

        // Get character race and gender
        npcData.Race = _characterData.GetSpeakerRace(eventId, speaker, out var raceStr, out var modelId);
        npcData.RaceStr = raceStr;
        npcData.Gender = _characterData.GetCharacterGender(eventId, speaker, npcData.Race, out var modelBody);
        npcData.Name = _textProcessing.CleanUpName(cleanSpeakerName);
        npcData.Language = _clientState.ClientLanguage;
        npcData.World = worldEnglish ?? "";

        // Handle NPC name mapping. Two-step canonicalization:
        //   1) VoiceNames{LANG}.json — community-curated voice families (e.g. "Y'shtola's
        //      Avatar" → "Y'shtola"). Static, ships with the plugin.
        //   2) DB speaker aliases — harvest-discovered (-Fakename-) hints (e.g.
        //      "Mysterious Lady" → "Y'shtola Rhul"). Per-installation, populated by
        //      DialogHarvestService.PersistLinkedDialogs. May be ambiguous for anonymous
        //      markers like "???"; resolved via physical-presence + already-spoken
        //      tracking inside ResolveCharacterByAlias.
        // The DB step only runs when (1) didn't change the name (i.e. VoiceNames missed),
        // so a VoiceNames hit always wins over a per-user alias.
        if (npcData.ObjectKind != ObjectKind.Pc)
        {
            var beforeJson = npcData.Name;
            npcData.Name = _jsonData.GetNpcName(npcData.Name);
            if (string.Equals(beforeJson, npcData.Name, StringComparison.OrdinalIgnoreCase))
            {
                var resolvedCharId = ResolveCharacterByAlias(npcData.Name, npcData.Language, speaker, eventId);
                if (resolvedCharId.HasValue)
                {
                    var aliasChar = _db.GetAllCharacters().FirstOrDefault(c => c.Id == resolvedCharId.Value);
                    if (aliasChar != null && !string.IsNullOrEmpty(aliasChar.Name))
                    {
                        npcData.Name = aliasChar.Name;
                        // Track that this character has now been "used" via alias resolution
                        // in this dialog session. Drives the disambiguation tie-breaker for
                        // future multi-match lookups in the same cutscene.
                        DialogState.SpeakersResolvedThisDialog.Add(resolvedCharId.Value);
                    }
                }
            }
        }

        if (npcData.Name == "PLAYER")
            npcData.Name = _gameObjects.LocalPlayer?.Name.ToString() ?? "PLAYER";
        else if (string.IsNullOrWhiteSpace(npcData.Name) && source == TextSource.AddonBubble)
            npcData.Name = _textProcessing.GetBubbleName(_lumina.GetTerritory(), speaker, cleanText);

        // Get or add to database
        var resNpcData = _npcData.GetAddCharacterMapData(npcData, eventId, _backend);
        npcData = resNpcData;

        // Re-pick the voice if the cached assignment doesn't fit anymore (e.g. user edited race/gender
        // via "Edit Character"). For first-time NPCs GetAddCharacterMapData already assigned a voice,
        // EnsureFittingVoice short-circuits in that case.
        _backend.EnsureFittingVoice(npcData, eventId);

        // For Player characters with a World but unknown Race/Gender (e.g. chat sender not currently visible),
        // kick off an async Lodestone enrichment. Fire-and-forget — the first message is processed with what we have;
        // subsequent ones use the resolved values.
        if (npcData.ObjectKind == ObjectKind.Pc
            && !string.IsNullOrWhiteSpace(npcData.World)
            && (npcData.Race == NpcRaces.Unknown || npcData.Gender == Genders.None))
        {
            _ = TryEnrichPlayerFromLodestoneAsync(npcData, eventId);
        }

        // Check body type (child/elder/adult)
        var changed = false;
        if (speaker != null && (source == TextSource.AddonBubble || source == TextSource.AddonTalk))
        {
            var npcBase = _lumina.GetENpcBase(speaker.BaseId, eventId);
            var bodyType = npcBase?.BodyType switch
            {
                4 => BodyType.Child,
                3 => BodyType.Elder,
                _ => BodyType.Adult
            };
            if (npcData.BodyType != bodyType)
            {
                npcData.BodyType = bodyType;
                changed = true;
            }
        }

        // Update object kind if needed
        if (npcData.ObjectKind != objectKind && objectKind != ObjectKind.None)
        {
            npcData.ObjectKind = objectKind;
            changed = true;
        }

        if (changed)
            _npcData.SaveCharacter(npcData);

        return npcData;
    }

    private void LogVoiceClip(VoiceMessage message)
    {
        try
        {
            var speaker = message.Speaker;
            var character = speaker != null
                ? _db.FindCharacter(speaker.Name, speaker.Gender, speaker.Race, (int)speaker.Language)
                : null;
            if (character == null) return;

            // Get zone name and map coordinates
            var zoneName = "";
            var mapX = 0f;
            var mapY = 0f;
            try
            {
                var territory = _lumina.GetTerritory();
                if (territory != null)
                {
                    zoneName = territory.Value.PlaceName.Value.Name.ToString();
                    if (message.SpeakerObj != null)
                    {
                        var pos = message.SpeakerObj.Position;
                        var map = territory.Value.Map.Value;
                        var sf = map.SizeFactor / 100.0f;
                        mapX = 41.0f / sf * ((pos.X + map.OffsetX) * sf + 1024.0f) / 2048.0f + 1.0f;
                        mapY = 41.0f / sf * ((pos.Z + map.OffsetY) * sf + 1024.0f) / 2048.0f + 1.0f;
                    }
                }
            }
            catch { /* Map coordinate calculation may fail for some territories */ }

            // Create or update character instance with location data
            if (message.SpeakerObj != null && message.SpeakerObj.BaseId != 0)
                _db.GetOrCreateInstance(character.Id, message.SpeakerObj.BaseId, zoneName, mapX, mapY);

            var npcBaseId = message.SpeakerObj != null ? (long)message.SpeakerObj.BaseId : 0;
            var originalText = message.OriginalText ?? "";

            // wav_file_name = the same on-disk hash AudioFileService uses when it writes
            // the file. Stored on every live-path clip so DatabaseService.LogOrUpdateVoiceClip
            // can rescue legacy "orphan" rows the audio backfill created (clips with a
            // wav_file_name but no text yet). When this dialog re-fires in-game and lands
            // here for the first time, the orphan-resolve fallback finds the existing row
            // by (CharacterId, WavFileName) and promotes it instead of inserting a duplicate.
            var wavFileName = _audioFiles.VoiceMessageToFileName(
                _audioFiles.RemovePlayerNameInText(originalText));

            var persisted = _db.LogOrUpdateVoiceClip(new DataClasses.Database.VoiceClipEntity
            {
                CharacterId = character.Id,
                NpcBaseId = npcBaseId,
                Timestamp = DateTime.UtcNow,
                TextSource = (int)message.Source,
                Language = (int)message.Language,
                VoiceKey = speaker?.voice ?? "",
                OriginalText = originalText,
                CleanedText = message.Text ?? "",
                WavFileName = wavFileName,
                // Read-side (VoiceClipManagerService.GetEffectivePlayerId) keys generation rows on
                // this flag, so it must be set in the live path too — otherwise generations are
                // logged with the wrong player_content_id and the UI shows clips as "not generated".
                HasPlayerPlaceholder = TalkTextHelper.ContainsPlayerPlaceholder(originalText),
                SavedToDisk = false,
                BodyType = (int)(speaker?.BodyType ?? BodyType.Adult),
                ZoneName = zoneName,
                MapX = mapX,
                MapY = mapY
            });
            message.VoiceClipId = persisted.Id;
            message.HasPlayerPlaceholder = persisted.HasPlayerPlaceholder;
        }
        catch (Exception ex)
        {
            _log.Debug(nameof(LogVoiceClip), $"Failed to log voice clip: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    /// <summary>
    /// If a previous generation of this (clip, player, alias_gender=0) exists on disk, attach
    /// the file stream to <paramref name="voiceMessage"/> and flag it as locally-loaded so
    /// <see cref="IBackendService.GenerationLoopAsync"/> short-circuits the backend call.
    /// Gated on <see cref="Configuration.LoadFromLocalFirst"/> — SaveToLocal is the write side
    /// of the cache and not relevant for playback.
    ///
    /// Two-stage lookup:
    /// <list type="number">
    /// <item>DB <c>voice_clip_generations</c> row — fast path for clips this install has
    ///     already generated (or previously adopted).</item>
    /// <item>Disk fallback — if no DB row, probe the deterministic
    ///     <see cref="IAudioFileService.GetLocalAudioPath"/> path. A hit means the user copied
    ///     a WAV from elsewhere (friend's backup, manual restore); the file gets adopted into
    ///     the DB with the speaker's current voice key so the next playthrough takes the fast
    ///     path. Adoption uses <c>LogVoiceClipGeneration</c> which is upsert-keyed on
    ///     <c>(voice_clip_id, player_content_id, alias_gender)</c>, so re-firing the same
    ///     dialog never duplicates rows.</item>
    /// </list>
    /// </summary>
    private void TryLoadCachedAudio(VoiceMessage voiceMessage, EKEventId eventId)
    {
        if (!_config.LoadFromLocalFirst) return;
        if (voiceMessage.VoiceClipId <= 0) return;

        try
        {
            var playerId = _gameObjects.GetEffectivePlayerContentId(voiceMessage.HasPlayerPlaceholder);
            var gen = _db.GetVoiceClipGeneration(voiceMessage.VoiceClipId, playerId);

            // Voice-key staleness gate. The cached generation row stores the voice that was
            // used when the WAV was originally generated. If the user has since switched the
            // NPC's voice (e.g. NPC10 → NPC11), the on-disk audio is now stale — replaying
            // it would silently ignore the user's voice change. Treat as cache miss so the
            // backend regenerates; LogVoiceClipGeneration upserts the row in place with the
            // new voice_key + new save_path (filenames are voice-key-INDEPENDENT, so the
            // fresh WAV overwrites the stale one).
            //
            // Skipped when:
            //   - the speaker has no voice configured at all (Speaker.voice empty) — there's
            //     nothing to compare against, and the user can't have wanted a "different"
            //     voice without picking one
            //   - None-mode is active (HasLiveGeneration == false) — backend can't regenerate,
            //     so playing whatever's cached is the best we can do
            // Multi-epoch NPCs (per Issue 3 in voice-sample-improvements plan) will need a
            // valid-voice-set check here once that feature lands; today every NPC has at most
            // one configured voice so a plain equality compare is correct.
            var currentVoice = voiceMessage.Speaker?.voice ?? "";
            if (gen != null
                && _config.Alltalk.HasLiveGeneration
                && !string.IsNullOrEmpty(currentVoice)
                && !string.Equals(gen.VoiceKey ?? "", currentVoice, StringComparison.Ordinal))
            {
                _log.Info(nameof(TryLoadCachedAudio),
                    $"Cache invalidated for clip {voiceMessage.VoiceClipId}: " +
                    $"saved voice='{gen.VoiceKey}' != current voice='{currentVoice}'. Regenerating.",
                    eventId);
                // Don't fall through to disk-adopt either — the orphan WAV would be the SAME
                // stale file, and adopting it under the new voice_key would just re-cache the
                // bug. Hand off to the backend cleanly.
                return;
            }

            var path = gen?.SavePath;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                // No DB row (or row points at a missing file). Probe the deterministic
                // on-disk location — if a WAV lives there it was either written by an
                // older install, restored from a backup, or copied in by the user.
                // Promote it to a real generation row so future plays are fast-path.
                var orphan = _audioFiles.TryFindExistingLocalAudio(_config.LocalSaveLocation, voiceMessage);
                if (orphan == null) return;

                var voiceKey = voiceMessage.Speaker?.voice ?? "";
                var playerName = _gameObjects.LocalPlayerName ?? "";
                _db.LogVoiceClipGeneration(voiceMessage.VoiceClipId, playerId, playerName, orphan, voiceKey);
                _log.Info(nameof(TryLoadCachedAudio),
                    $"Adopted on-disk audio (no prior DB row): {orphan}", eventId);
                path = orphan;
            }

            voiceMessage.Stream = System.IO.File.OpenRead(path);
            voiceMessage.LoadedLocally = true;
            _log.Debug(nameof(TryLoadCachedAudio),
                $"Cache hit — replaying saved audio: {path}", eventId);
        }
        catch (Exception ex)
        {
            // Cache lookup failed — fall through to normal generation. Don't crash the pipeline.
            _log.Warning(nameof(TryLoadCachedAudio),
                $"Cache lookup failed, will regenerate: {ex.Message}", eventId);
        }
    }

    private bool IsNpcMuted(NpcMapData npcData, TextSource source, IGameObject? speaker, EKEventId eventId)
    {
        switch (source)
        {
            case TextSource.AddonBubble:
                if (!npcData.HasBubbles)
                    npcData.HasBubbles = true;

                if (npcData.VolumeBubble == 0f || !npcData.IsEnabledBubble)
                {
                    _log.Info(nameof(IsNpcMuted), $"Bubble is muted: {npcData}", eventId);
                    return true;
                }
                break;

            case TextSource.AddonBattleTalk:
            case TextSource.AddonTalk:
                if (npcData.Volume == 0f || !npcData.IsEnabled)
                {
                    _log.Info(nameof(IsNpcMuted), $"NPC is muted: {npcData}", eventId);
                    return true;
                }
                break;

            case TextSource.AddonCutsceneSelectString:
            case TextSource.AddonSelectString:
            case TextSource.Chat:
                if (npcData.Volume == 0f || !npcData.IsEnabled)
                {
                    _log.Info(nameof(IsNpcMuted), $"Player is muted: {npcData}", eventId);
                    return true;
                }
                break;
        }

        return false;
    }

    private float GetNpcVolume(NpcMapData npcData, TextSource source)
    {
        return source switch
        {
            TextSource.AddonBubble => npcData.VolumeBubble,
            _ => npcData.Volume
        };
    }

    private bool ShouldUse3DAudio(TextSource source)
    {
        return source switch
        {
            TextSource.AddonBubble => true,
            TextSource.AddonTalk => _config.VoiceDialogueIn3D,
            TextSource.Chat => _config.VoiceChatIn3D,
            _ => false
        };
    }
}
