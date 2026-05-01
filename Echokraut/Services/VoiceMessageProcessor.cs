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
        ILodestoneService lodestone)
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
            if (_config.VoiceChatIn3D && speaker == null)
            {
                _log.Info(nameof(CleanText), "Player is not on the same map. Can't voice", eventId);
                return null;
            }

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

    private async Task<NpcMapData> GetOrCreateNpcDataAsync(IGameObject? speaker, SeString speakerName, string cleanText, TextSource source, EKEventId eventId, string worldEnglish = "")
    {
        var cleanSpeakerName = _textProcessing.NormalizePunctuation(speakerName.TextValue);
        var objectKind = speaker?.ObjectKind ?? ObjectKind.None;
        var npcData = new NpcMapData(objectKind);

        // Get character race and gender
        npcData.Race = _characterData.GetSpeakerRace(eventId, speaker, out var raceStr, out var modelId);
        npcData.RaceStr = raceStr;
        npcData.Gender = _characterData.GetCharacterGender(eventId, speaker, npcData.Race, out var modelBody);
        npcData.Name = _textProcessing.CleanUpName(cleanSpeakerName);
        npcData.Language = _clientState.ClientLanguage;
        npcData.World = worldEnglish ?? "";

        // Handle NPC name mapping
        if (npcData.ObjectKind != ObjectKind.Pc)
            npcData.Name = _jsonData.GetNpcName(npcData.Name);

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
