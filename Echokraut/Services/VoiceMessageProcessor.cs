using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
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
        IGameObjectService gameObjects)
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
    }

    public async Task ProcessSpeechAsync(EKEventId eventId, IGameObject? speaker, SeString speakerName, string textValue)
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

            var source = eventId.textSource;
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
            var npcData = await GetOrCreateNpcDataAsync(speaker, speakerName, cleanText, source, eventId);
            
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
            if (npcData.Voice == null && !onlyRequest && !_config.Alltalk.NoInstance)
            {
                _log.Info(nameof(ProcessSpeechAsync), "Getting voice since not set.", eventId);
                _backend.GetVoiceOrRandom(eventId, npcData);
            }

            if (npcData.Voice == null && !onlyRequest && !_config.Alltalk.NoInstance)
            {
                _log.Info(nameof(ProcessSpeechAsync), "Skipping voice inference. No Voice set.", eventId);
                _log.End(nameof(ProcessSpeechAsync), eventId);
                return;
            }

            if (npcData.Voice != null && npcData.Voice.Volume == 0f && !onlyRequest && !_config.Alltalk.NoInstance)
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

            // Step 8: Check if dialogue is muted
            if (speaker != null && _config.MutedNpcDialogues.Contains(speaker.BaseId))
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
        cleanText = _textProcessing.ReplacePhonetics(cleanText, _config.PhoneticCorrections);
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

    private async Task<NpcMapData> GetOrCreateNpcDataAsync(IGameObject? speaker, SeString speakerName, string cleanText, TextSource source, EKEventId eventId)
    {
        var cleanSpeakerName = _textProcessing.NormalizePunctuation(speakerName.TextValue);
        var objectKind = speaker?.ObjectKind ?? ObjectKind.None;
        var npcData = new NpcMapData(objectKind);

        // Get character race and gender
        npcData.Race = _characterData.GetSpeakerRace(eventId, speaker, out var raceStr, out var modelId);
        npcData.RaceStr = raceStr;
        npcData.Gender = _characterData.GetCharacterGender(eventId, speaker, npcData.Race, out var modelBody);
        npcData.Name = _textProcessing.CleanUpName(cleanSpeakerName);

        // Handle NPC name mapping
        if (npcData.ObjectKind != ObjectKind.Player)
            npcData.Name = _jsonData.GetNpcName(npcData.Name);

        if (npcData.Name == "PLAYER")
            npcData.Name = _gameObjects.LocalPlayer?.Name.ToString() ?? "PLAYER";
        else if (string.IsNullOrWhiteSpace(npcData.Name) && source == TextSource.AddonBubble)
            npcData.Name = _textProcessing.GetBubbleName(_lumina.GetTerritory(), speaker, cleanText);

        // Get or add to configuration
        var resNpcData = _npcData.GetAddCharacterMapData(npcData, eventId, _backend);
        _config.Save();
        npcData = resNpcData;

        // Check if child character
        if (speaker != null && (source == TextSource.AddonBubble || source == TextSource.AddonTalk))
        {
            npcData.IsChild = _lumina.GetENpcBase(speaker.BaseId, eventId)?.BodyType == 4;
            _config.Save();
        }

        // Update object kind if needed
        if (npcData.ObjectKind != objectKind && objectKind != ObjectKind.None)
        {
            npcData.ObjectKind = objectKind;
            _config.Save();
        }

        return npcData;
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
