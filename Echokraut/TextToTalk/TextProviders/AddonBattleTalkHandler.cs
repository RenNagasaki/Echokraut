using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Talk;
using Dalamud.Configuration;
using Echokraut.TextToTalk.Utils;
using Echokraut.DataClasses;
using Echokraut.TextToTalk.Events;

namespace Echokraut.TextToTalk.TextProviders;

// This might be almost exactly the same as AddonTalkHandler, but it's too early to pull out a common base class.
public class AddonBattleTalkHandler : IAddonBattleTalkHandler
{
    private record struct AddonBattleTalkState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly AddonBattleTalkManager addonTalkManager;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly ComponentUpdateState<AddonBattleTalkState> updateState;
    private readonly IDisposable subscription;

    // Most recent speaker/text specific to this addon
    private string? lastAddonSpeaker;
    private string? lastAddonText;

    public Action<TextEmitEvent> OnTextEmit { get; set; }

    public AddonBattleTalkHandler(AddonBattleTalkManager addonTalkManager, IFramework framework, IObjectTable objects, Configuration config, IPluginLog log)
    {
        this.addonTalkManager = addonTalkManager;
        this.framework = framework;
        this.objects = objects;
        this.config = config;
        this.log = log;
        updateState = new ComponentUpdateState<AddonBattleTalkState>();
        updateState.OnUpdate += HandleChange;
        subscription = HandleFrameworkUpdate();

        OnTextEmit = _ => { };
    }

    private Observable<AddonPollSource> OnFrameworkUpdate()
    {
        return Observable.Create(this, static (Observer<AddonPollSource> observer, AddonBattleTalkHandler abth) =>
        {
            var handler = new IFramework.OnUpdateDelegate(Handle);
            abth.framework.Update += handler;
            return Disposable.Create(() => abth.framework.Update -= handler);

            void Handle(IFramework f)
            {
                if (!abth.config.Enabled) return;
                if (!abth.config.VoiceBattletalk) return;
                observer.OnNext(AddonPollSource.FrameworkUpdate);
            }
        });
    }

    private IDisposable HandleFrameworkUpdate()
    {
        return OnFrameworkUpdate().Subscribe(this, static (s, h) => h.PollAddon(s));
    }

    public void PollAddon(AddonPollSource pollSource)
    {
        var state = GetTalkAddonState(pollSource);
        updateState.Mutate(state);
    }

    private void HandleChange(AddonBattleTalkState state)
    {
        var (speaker, text, pollSource) = state;

        if (state == default)
        {
            // The addon was closed
            return;
        }

        text = TalkUtils.NormalizePunctuation(text);

        log.Debug($"AddonBattleTalk ({pollSource}): \"{text}\"");

        {
            // This entire callback executes twice in a row - once for the voice line, and then again immediately
            // afterwards for the framework update itself. This prevents the second invocation from being spoken.
            if (lastAddonSpeaker == speaker && lastAddonText == text)
            {
                log.Debug($"Skipping duplicate line: {text}");
                return;
            }

            lastAddonSpeaker = speaker;
            lastAddonText = text;
        }

        if (pollSource == AddonPollSource.VoiceLinePlayback)
        {
            log.Debug($"Skipping voice-acted line: {text}");
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(objects, speaker) : null;

        OnTextEmit.Invoke(speakerObj != null
            ? new AddonBattleTalkEmitEvent(speakerObj.Name, text, speakerObj)
            : new AddonBattleTalkEmitEvent(state.Speaker ?? "", text, null));
    }

    private AddonBattleTalkState GetTalkAddonState(AddonPollSource pollSource)
    {
        if (!addonTalkManager.IsVisible())
        {
            return default;
        }

        var addonTalkText = addonTalkManager.ReadText();
        return addonTalkText != null
            ? new AddonBattleTalkState(addonTalkText.Speaker, addonTalkText.Text, pollSource)
            : default;
    }

    public void Dispose()
    {
        subscription.Dispose();
    }

    Observable<TextEmitEvent> IAddonBattleTalkHandler.OnTextEmit()
    {
        return Observable.FromEvent<TextEmitEvent>(
            h => OnTextEmit += h,
            h => OnTextEmit -= h);
    }
}
