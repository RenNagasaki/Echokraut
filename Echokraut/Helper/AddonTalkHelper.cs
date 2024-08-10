using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Utils;
using Dalamud.Configuration;
using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using Echokraut.Utils;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;

namespace Echokraut.Helper;

public class AddonTalkHelper
{
    private record struct AddonTalkState(string? Speaker, string? Text);

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly Echokraut echokraut;
    public bool nextIsVoice = false;
    public DateTime timeNextVoice = DateTime.Now;

    public static nint Address { get; set; }

    public AddonTalkHelper(Echokraut plugin, IAddonLifecycle addonLifecycle, IClientState clientState, IObjectTable objects, Configuration config)
    {
        this.echokraut = plugin;
        this.addonLifecycle = addonLifecycle;
        this.clientState = clientState;
        this.config = config;
        this.objects = objects;

        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        addonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
    }

    private unsafe void OnPostDraw(AddonEvent type, AddonArgs args)
    {
        var addonTalk = (AddonTalk*)args.Addon.ToPointer();
        Address = args.Addon;
        Handle(addonTalk);
    }

    private unsafe void Handle(AddonTalk* addonTalk)
    {
        if (!config.Enabled) return;
        if (!config.VoiceDialogue) return;
        var state = GetTalkAddonState(addonTalk);
        Mutate(state);
    }

    private void Mutate(AddonTalkState nextValue)
    {
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
        var (speaker, text) = state;
        var voiceNext = nextIsVoice;
        nextIsVoice = false;

        if (voiceNext && DateTime.Now > timeNextVoice.AddSeconds(1))
            voiceNext = false;

        if (state == default)
        {
            PlayingHelper.InDialog = false;
            // The addon was closed
            echokraut.Cancel(new EKEventId(0, Enums.TextSource.AddonTalk));
            return;
        }
        var eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);

        // Notify observers that the addon state was advanced
        echokraut.Cancel(eventId);

        text = TalkUtils.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonTalk: \"{text}\"", eventId);

        //ObjectTableUtils.TryGetUnnamedObject(clientState, objects, speaker, eventId);
        if (voiceNext)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(clientState, objects, speaker, eventId) : null;

        PlayingHelper.InDialog = true;
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Setting inDialog true", eventId);

        if (speakerObj != null)
        {
            echokraut.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            echokraut.Say(eventId, null, state.Speaker ?? "", text);
        }
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonTalk();
        return addonTalk == null ? null : TalkUtils.ReadTalkAddon(addonTalk);
    }

    private unsafe AddonTalk* GetAddonTalk()
    {
        return (AddonTalk*)Address.ToPointer();
    }

    public void Click(EKEventId eventId)
    {
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Auto advancing...", eventId);
        ClickHelper.Click(Address);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
    }
}
