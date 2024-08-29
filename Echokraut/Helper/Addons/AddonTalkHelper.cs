using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Utils;
using Dalamud.Configuration;
using Echokraut.DataClasses;
using static Dalamud.Plugin.Services.IFramework;
using System.IO;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using static System.Net.Mime.MediaTypeNames;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace Echokraut.Helper.Addons;

public class AddonTalkHelper
{
    private record struct AddonTalkState(string? Speaker, string? Text);

    private OnUpdateDelegate updateHandler;
    private readonly ICondition condition;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly Echokraut echokraut;
    public bool nextIsVoice = false;
    private bool wasTalking = false;
    private bool wasWatchingCutscene = false;
    public DateTime timeNextVoice = DateTime.Now;

    public static nint Address { get; set; }
    private AddonTalkState lastValue;

    public AddonTalkHelper(Echokraut plugin, ICondition condition, IAddonLifecycle addonLifecycle, IClientState clientState, IObjectTable objects, Configuration config)
    {
        echokraut = plugin;
        this.condition = condition;
        this.addonLifecycle = addonLifecycle;
        this.clientState = clientState;
        this.config = config;
        this.objects = objects;

        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        addonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
    }

    private unsafe void OnPostUpdate(AddonEvent type, AddonArgs args)
    {
        var addonTalk = (AddonTalk*)args.Addon.ToPointer();

        if (addonTalk != null)
        {
            var visible = addonTalk->AtkUnitBase.IsVisible;
            if (!visible && wasTalking)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Addon closed", new EKEventId(0, TextSource.AddonTalk));
                wasTalking = false;
                PlayingHelper.InDialog = false;
                lastValue = new AddonTalkState();
                echokraut.Cancel(new EKEventId(0, TextSource.AddonTalk));
            }
        }
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
        var (speaker, text) = state;
        var voiceNext = nextIsVoice;
        nextIsVoice = false;

        if (voiceNext && DateTime.Now > timeNextVoice.AddMilliseconds(500))
            voiceNext = false;

        var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);

        // Notify observers that the addon state was advanced
        echokraut.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"\"{text}\"", eventId);

        //ObjectTableUtils.TryGetUnnamedObject(clientState, objects, speaker, eventId);
        if (voiceNext)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return;
        }

        if (condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] || condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.OccupiedInQuestEvent])
        {
            wasWatchingCutscene = true;
            DalamudHelper.TryGetNextUnkownCharacter(clientState, objects, eventId);
            if (speaker == "???")
                speaker = DalamudHelper.nextUnknownCharacter?.Name.TextValue ?? "???";
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Got ??? speaker: \"{speaker}\"", eventId);
        }
        else if (wasWatchingCutscene)
        {
            DalamudHelper.ClearLastUnknownState();
            wasWatchingCutscene = false;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(clientState, objects, speaker, eventId) : null;

        PlayingHelper.InDialog = true;

        wasTalking = true;
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
        ClickHelper.ClickDialogue(Address, eventId);
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
        addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
    }
}
