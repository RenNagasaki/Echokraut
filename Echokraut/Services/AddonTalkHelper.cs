using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.Helper.Functional;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Echokraut.Services;
using Echotools.Logging.Services;

namespace Echokraut.Services;

public unsafe class AddonTalkHelper : IAddonTalkHelper
{
    private record struct AddonTalkState(string? Speaker, string? Text);

    // Injected dependencies
    private readonly IVoiceMessageProcessor _voiceProcessor;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly ICondition _condition;
    private readonly ILogService _log;
    private readonly Configuration _configuration;
    private readonly IAddonCancelService _cancelService;
    private readonly IGameObjectService _gameObjects;
    private readonly ITextProcessingService _textProcessing;
    private readonly ISoundHelper _soundHelper;

    public static Vector2 AddonPos { get; private set; }
    public static float AddonWidth { get; private set; }
    public static float AddonHeight { get; private set; }
    public static float AddonScale { get; private set; } = 1f;

    private bool nextIsVoice = false;
    private bool wasTalking = false;
    private bool wasWatchingCutscene = false;
    private DateTime timeNextVoice = DateTime.Now;

    public void NotifyNextIsVoice()
    {
        nextIsVoice = true;
        timeNextVoice = DateTime.Now;
    }

    public static nint Address { get; set; }
    private static AddonTalkState lastValue;

    public AddonTalkHelper(
        IVoiceMessageProcessor voiceProcessor,
        IAddonLifecycle addonLifecycle,
        IAudioPlaybackService audioPlayback,
        ICondition condition,
        ILogService log,
        Configuration configuration,
        IAddonCancelService cancelService,
        IGameObjectService gameObjects,
        ITextProcessingService textProcessing,
        ISoundHelper soundHelper)
    {
        _voiceProcessor = voiceProcessor ?? throw new ArgumentNullException(nameof(voiceProcessor));
        _addonLifecycle = addonLifecycle ?? throw new ArgumentNullException(nameof(addonLifecycle));
        _audioPlayback = audioPlayback ?? throw new ArgumentNullException(nameof(audioPlayback));
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cancelService = cancelService ?? throw new ArgumentNullException(nameof(cancelService));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _textProcessing = textProcessing ?? throw new ArgumentNullException(nameof(textProcessing));
        _soundHelper = soundHelper ?? throw new ArgumentNullException(nameof(soundHelper));
        _soundHelper.TalkVoiceLine += NotifyNextIsVoice;

        HookIntoFrameworkUpdate();
    }

    public void RecreateInference()
    {
        _audioPlayback.RecreationStarted = true;
        lastValue = new AddonTalkState(null, null);
    }

    private void HookIntoFrameworkUpdate()
    {
        _addonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
        _addonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
    }
    
    private void OnPreReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonReceiveEventArgs eventArgs)
            return;

        var eventData = (AtkEventData*)eventArgs.AtkEventData;
        if (eventData == null)
            return;

        var eventType = (AtkEventType)eventArgs.AtkEventType;
        var isControllerButtonClick = eventType == AtkEventType.InputReceived && eventData->InputData.InputId == 1;
        var isDialogueAdvancing = 
            (eventType == AtkEventType.MouseClick && ((byte)eventData->MouseData.Modifier & 0b0001_0000) == 0) || 
            eventArgs.AtkEventType == (byte)AtkEventType.InputReceived;

        if (isControllerButtonClick || isDialogueAdvancing)
            if (_configuration.CancelSpeechOnTextAdvance)
                _cancelService.Cancel(DialogState.CurrentVoiceMessage);
    }

    private unsafe void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        var addonTalk = (AddonTalk*)args.Addon.Address.ToPointer();

        if (addonTalk != null)
        {
            var visible = addonTalk->AtkUnitBase.IsVisible;
            if (!visible && wasTalking)
            {
                _log.Info(nameof(OnPostUpdate), $"Addon closed",
                          new EKEventId(0, TextSource.AddonTalk));
                wasTalking = false;
                _audioPlayback.InDialog = false;
                lastValue = new AddonTalkState();
                if (_configuration.CancelSpeechOnTextAdvance)
                    _cancelService.Cancel(DialogState.CurrentVoiceMessage, true);
                DialogState.CurrentVoiceMessage = null;
            }
        }
    }

    private unsafe void OnPostDraw(AddonEvent type, AddonArgs args)
    {
        var addonTalk = (AddonTalk*)args.Addon.Address.ToPointer();
        Address = args.Addon;
        if (addonTalk != null)
        {
            AddonPos = new Vector2(addonTalk->GetX(), addonTalk->GetY());
            AddonWidth = addonTalk->GetScaledWidth(true);
            AddonHeight = addonTalk->GetScaledHeight(true);
            AddonScale = addonTalk->Scale;
            Handle(addonTalk);
        }
    }

    private unsafe void Handle(AddonTalk* addonTalk)
    {
        if (!_configuration.Enabled) return;
        if (!_configuration.VoiceDialogue) return;
        if (addonTalk == null || !addonTalk->AtkUnitBase.IsVisible) return;
        var state = GetTalkAddonState(addonTalk);
        Mutate(state);
    }

    private void Mutate(AddonTalkState nextValue)
    {
        if (lastValue.Equals(nextValue))
            return;

        lastValue = nextValue;
        HandleChange(nextValue);
    }

    private unsafe AddonTalkState GetTalkAddonState(AddonTalk* addonTalk)
    {
        var addonTalkText = ReadText();
        return addonTalkText != null
            ? new AddonTalkState(addonTalkText.Speaker, addonTalkText.Text)
            : default;
    }

    private void HandleChange(AddonTalkState state)
    {
        _audioPlayback.RecreationStarted = true;
        var (speaker, text) = state;
        var voiceNext = nextIsVoice;
        nextIsVoice = false;
        DialogState.IsVoiced = false;

        if (voiceNext && DateTime.Now > timeNextVoice.AddMilliseconds(500))
            voiceNext = false;

        var eventId = _log.Start(nameof(HandleChange), TextSource.AddonTalk);

        // Notify observers that the addon state was advanced
        if (_configuration.CancelSpeechOnTextAdvance)
            _cancelService.Cancel(DialogState.CurrentVoiceMessage);

        text = _textProcessing.NormalizePunctuation(text);

        _log.Info(nameof(HandleChange), $"\"{text}\"", eventId);

        //ObjectTableUtils.TryGetUnnamedObject(clientState, objects, speaker, eventId);
        if (voiceNext)
        {
            _log.Info(nameof(HandleChange), $"Skipping voice-acted line: {text}", eventId);
            _log.End(nameof(HandleChange), eventId);
            _audioPlayback.RecreationStarted = false;
            DialogState.IsVoiced = true;
            return;
        }

        if (_condition[ConditionFlag.OccupiedSummoningBell] && !_configuration.VoiceRetainers)
        {
            _log.Info(nameof(HandleChange), $"Skipping retainer line: {text}", eventId);
            _log.End(nameof(HandleChange), eventId);
            _audioPlayback.RecreationStarted = false;
            return;
        }

        if (_condition[ConditionFlag.WatchingCutscene] || _condition[ConditionFlag.OccupiedInCutSceneEvent] || _condition[ConditionFlag.OccupiedInQuestEvent])
        {
            wasWatchingCutscene = true;
            _gameObjects.TryGetNextUnknownCharacter(eventId);
            if (speaker == "???")
                speaker = _gameObjects.NextUnknownCharacter?.Name.TextValue ?? "???";
            _log.Debug(nameof(HandleChange), $"Got ??? speaker: \"{speaker}\"", eventId);
        }
        else if (wasWatchingCutscene)
        {
            _gameObjects.ClearLastUnknownState();
            wasWatchingCutscene = false;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? _gameObjects.GetGameObjectByName(speaker, eventId) : null;

        _audioPlayback.InDialog = true;

        wasTalking = true;
        if (speakerObj != null)
        {
            _ = _voiceProcessor.ProcessSpeechAsync(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            _ = _voiceProcessor.ProcessSpeechAsync(eventId, null, state.Speaker ?? "", text);
        }
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonTalk();
        return addonTalk == null ? null : TalkTextHelper.ReadTalkAddon(addonTalk);
    }

    public unsafe bool IsVisible()
    {
        var addonTalk = GetAddonTalk();
        return addonTalk != null && addonTalk->AtkUnitBase.IsVisible;
    }

    private unsafe AddonTalk* GetAddonTalk()
    {
        return (AddonTalk*)Address.ToPointer();
    }

    public void Click(EKEventId eventId)
    {
        ClickHelper.ClickDialogue(_log, Address, eventId);
    }

    public void Dispose()
    {
        _soundHelper.TalkVoiceLine -= NotifyNextIsVoice;
        _addonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
        _addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
        _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
    }
}
