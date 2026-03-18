using System;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Services;

namespace Echokraut.Services;

public class AddonBattleTalkHelper : IAddonBattleTalkHelper
{
    private record struct AddonBattleTalkState(string? Speaker, string? Text);

    // Injected dependencies
    private readonly IVoiceMessageProcessor _voiceProcessor;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly ILogService _log;
    private readonly Configuration _configuration;
    private readonly IAddonCancelService _cancelService;
    private readonly IGameObjectService _gameObjects;
    private readonly ITextProcessingService _textProcessing;
    private readonly ISoundHelper _soundHelper;

    private bool nextIsVoice = false;
    private DateTime timeNextVoice = DateTime.Now;

    public void NotifyNextIsVoice()
    {
        nextIsVoice = true;
        timeNextVoice = DateTime.Now;
    }
    private AddonBattleTalkState lastValue;

    public AddonBattleTalkHelper(
        IVoiceMessageProcessor voiceProcessor,
        IAddonLifecycle addonLifecycle,
        ILogService log,
        Configuration configuration,
        IAddonCancelService cancelService,
        IGameObjectService gameObjects,
        ITextProcessingService textProcessing,
        ISoundHelper soundHelper)
    {
        _voiceProcessor = voiceProcessor ?? throw new ArgumentNullException(nameof(voiceProcessor));
        _addonLifecycle = addonLifecycle ?? throw new ArgumentNullException(nameof(addonLifecycle));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _cancelService = cancelService ?? throw new ArgumentNullException(nameof(cancelService));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _textProcessing = textProcessing ?? throw new ArgumentNullException(nameof(textProcessing));
        _soundHelper = soundHelper ?? throw new ArgumentNullException(nameof(soundHelper));
        _soundHelper.BattleBubbleVoiceLine += NotifyNextIsVoice;
        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        _addonLifecycle.RegisterListener(AddonEvent.PostDraw, "_BattleTalk", OnPostDraw);
    }

    private unsafe void OnPostDraw(AddonEvent type, AddonArgs args)
    {
        var addonBattleTalk = (AddonBattleTalk*)args.Addon.Address.ToPointer();
        Handle(addonBattleTalk);
    }
    private unsafe void Handle(AddonBattleTalk* addonBattleTalk)
    {
        if (!_configuration.Enabled) return;
        if (!_configuration.VoiceBattleDialogue) return;
        if (addonBattleTalk == null || !addonBattleTalk->Base.IsVisible) return;
        var state = GetTalkAddonState(addonBattleTalk);
        Mutate(state);
    }

    private unsafe AddonBattleTalkState GetTalkAddonState(AddonBattleTalk* addonBattleTalk)
    {
        var addonBattleTalkText = ReadText(addonBattleTalk);
        return addonBattleTalkText != null
            ? new AddonBattleTalkState(addonBattleTalkText.Speaker, addonBattleTalkText.Text)
            : default;
    }

    public unsafe AddonTalkText? ReadText(AddonBattleTalk* addonBattleTalk)
    {
        return addonBattleTalk == null ? null : TalkTextHelper.ReadTalkAddon(addonBattleTalk);
    }

    private void Mutate(AddonBattleTalkState nextValue)
    {
        if (lastValue.Equals(nextValue))
        {
            return;
        }

        lastValue = nextValue;
        HandleChange(nextValue);
    }

    private void HandleChange(AddonBattleTalkState state)
    {
        var (speaker, text) = state;
        var voiceNext = nextIsVoice;
        nextIsVoice = false;

        if (voiceNext && DateTime.Now > timeNextVoice.AddMilliseconds(1000))
            voiceNext = false;

        var eventId = _log.Start(nameof(HandleChange), TextSource.AddonBattleTalk);
        _log.Debug(nameof(HandleChange), $"\"{state}\"", eventId);

        // Notify observers that the addon state was advanced
        if (!_configuration.VoiceBattleDialogQueued)
            _cancelService.Cancel(DialogState.CurrentVoiceMessage);

        text = _textProcessing.NormalizePunctuation(text);

        _log.Info(nameof(HandleChange), $"\"{text}\"", eventId);

        if (voiceNext)
        {
            _log.Info(nameof(HandleChange), $"Skipping voice-acted line: {text}", eventId);
            _log.End(nameof(HandleChange), eventId);
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? _gameObjects.GetGameObjectByName(speaker, eventId) : null;

        if (speakerObj != null)
        {
            _ = _voiceProcessor.ProcessSpeechAsync(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            _ = _voiceProcessor.ProcessSpeechAsync(eventId, null, state.Speaker ?? "", text);
        }

        return;
    }

    public void Dispose()
    {
        _soundHelper.BattleBubbleVoiceLine -= NotifyNextIsVoice;
        _addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "_BattleTalk", OnPostDraw);
    }
}
