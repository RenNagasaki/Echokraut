using System;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Echokraut.Helper.Addons;

public unsafe class AddonTalkHelper
{
    private record struct AddonTalkState(string? Speaker, string? Text);

    public bool nextIsVoice = false;
    private bool wasTalking = false;
    private bool wasWatchingCutscene = false;
    public DateTime timeNextVoice = DateTime.Now;

    public static nint Address { get; set; }
    private AddonTalkState lastValue;

    public AddonTalkHelper()
    {
        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
    }
    
    private void OnPreReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonReceiveEventArgs eventArgs)
            return;

        var eventData = (AtkEventData*)eventArgs.Data;
        if (eventData == null)
            return;

        var eventType = (AtkEventType)eventArgs.AtkEventType;
        var isControllerButtonClick = eventType == AtkEventType.InputReceived && eventData->InputData.InputId == 1;
        var isDialogueAdvancing = 
            (eventType == AtkEventType.MouseClick && ((byte)eventData->MouseData.Modifier & 0b0001_0000) == 0) || 
            eventArgs.AtkEventType == (byte)AtkEventType.InputReceived;

        if (isControllerButtonClick || isDialogueAdvancing)
            Plugin.Cancel(new EKEventId(0, TextSource.AddonTalk));
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
                Plugin.Cancel(new EKEventId(0, TextSource.AddonTalk));
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
        if (!Plugin.Configuration.Enabled) return;
        if (!Plugin.Configuration.VoiceDialogue) return;
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

        var eventId = LogHelper.Start(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);

        // Notify observers that the addon state was advanced
        Plugin.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"\"{text}\"", eventId);

        //ObjectTableUtils.TryGetUnnamedObject(clientState, objects, speaker, eventId);
        if (voiceNext)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return;
        }

        if (Plugin.Condition[ConditionFlag.WatchingCutscene] || Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent])
        {
            wasWatchingCutscene = true;
            DalamudHelper.TryGetNextUnkownCharacter(Plugin.ClientState, Plugin.ObjectTable, eventId);
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
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(Plugin.ClientState, Plugin.ObjectTable, speaker, eventId) : null;

        PlayingHelper.InDialog = true;

        wasTalking = true;
        if (speakerObj != null)
        {
            Plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            Plugin.Say(eventId, null, state.Speaker ?? "", text);
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
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostDraw, "Talk", OnPostDraw);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "Talk", OnPostUpdate);
    }
}
