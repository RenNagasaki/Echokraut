using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Utils;
using Dalamud.Configuration;
using Echokraut.TextToTalk.Talk;
using Echokraut.DataClasses;
using Echokraut.TextToTalk.Events;

namespace Echokraut.TextToTalk.TextProviders;

public class AddonTalkHandler : IAddonTalkHandler
{
    private record struct AddonTalkState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly AddonTalkManager addonTalkManager;
    private readonly IObjectTable objects;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly ComponentUpdateState<AddonTalkState> updateState;
    private readonly IDisposable subscription;
    private readonly IPluginLog log;

    // Most recent speaker/text specific to this addon
    private string? lastAddonSpeaker;
    private string? lastAddonText;

    public Action<TextEmitEvent> OnTextEmit { get; set; }
    public Action<AddonTalkAdvanceEvent> OnAdvance { get; set; }
    public Action<AddonTalkCloseEvent> OnClose { get; set; }

    public AddonTalkHandler(AddonTalkManager addonTalkManager, IFramework framework, IObjectTable objects, Configuration config, IPluginLog log)
    {
        this.addonTalkManager = addonTalkManager;
        this.framework = framework;
        this.config = config;
        this.objects = objects;
        this.log = log;
        updateState = new ComponentUpdateState<AddonTalkState>();
        updateState.OnUpdate += HandleChange;
        subscription = HandleFrameworkUpdate();

        OnTextEmit = _ => { };
        OnAdvance = _ => { };
        OnClose = _ => { };
    }

    private Observable<AddonPollSource> OnFrameworkUpdate()
    {
        return Observable.Create(this, static (Observer<AddonPollSource> observer, AddonTalkHandler ath) =>
        {
            var handler = new IFramework.OnUpdateDelegate(Handle);
            ath.framework.Update += handler;
            return Disposable.Create(() => { ath.framework.Update -= handler; });

            void Handle(IFramework _)
            {
                if (!ath.config.Enabled) return;
                if (!ath.config.VoiceDialog) return;
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

    private void HandleChange(AddonTalkState state)
    {
        var (speaker, text, pollSource) = state;

        if (state == default)
        {
            // The addon was closed
            OnClose.Invoke(new AddonTalkCloseEvent());
            return;
        }

        // Notify observers that the addon state was advanced
        OnAdvance.Invoke(new AddonTalkAdvanceEvent());

        text = TalkUtils.NormalizePunctuation(text);

        log.Debug($"AddonTalk ({pollSource}): \"{text}\"");

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
            ? new AddonTalkEmitEvent(speakerObj.Name, text, speakerObj)
            : new AddonTalkEmitEvent(state.Speaker ?? "", text, null));
    }

    private AddonTalkState GetTalkAddonState(AddonPollSource pollSource)
    {
        if (!addonTalkManager.IsVisible())
        {
            return default;
        }

        var addonTalkText = addonTalkManager.ReadText();
        return addonTalkText != null
            ? new AddonTalkState(addonTalkText.Speaker, addonTalkText.Text, pollSource)
            : default;
    }

    public void Dispose()
    {
        subscription.Dispose();
    }

    Observable<TextEmitEvent> IAddonTalkHandler.OnTextEmit()
    {
        return Observable.FromEvent<TextEmitEvent>(
            h => OnTextEmit += h,
            h => OnTextEmit -= h);
    }

    Observable<AddonTalkAdvanceEvent> IAddonTalkHandler.OnAdvance()
    {
        return Observable.FromEvent<AddonTalkAdvanceEvent>(
            h => OnAdvance += h,
            h => OnAdvance -= h);
    }

    Observable<AddonTalkCloseEvent> IAddonTalkHandler.OnClose()
    {
        return Observable.FromEvent<AddonTalkCloseEvent>(
            h => OnClose += h,
            h => OnClose -= h);
    }
}
