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
using Echokraut.Utils;
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Echokraut.Helper;

public class AddonBattleTalkHelper
{
    private record struct AddonBattleTalkState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly IObjectTable objects;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    private OnUpdateDelegate updateHandler;

    private readonly string name;

    protected nint Address { get; set; }
    private static nint oldAddress { get; set; }

    // Most recent speaker/text specific to this addon
    private string? lastAddonSpeaker;
    private string? lastAddonText;
    private AddonBattleTalkState lastValue;

    public AddonBattleTalkHelper(Echokraut plugin, IClientState clientState, ICondition condition, IGameGui gui, IFramework framework, IObjectTable objects, Configuration config)
    {
        this.plugin = plugin;
        this.clientState = clientState;
        this.condition = condition;
        this.gui = gui;
        this.framework = framework;
        this.config = config;
        this.objects = objects;

        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        updateHandler = new OnUpdateDelegate(Handle);
        framework.Update += updateHandler;

    }
    void Handle(IFramework f)
    {
        UpdateAddonAddress();
        if (!config.Enabled) return;
        if (!config.VoiceBattleDialogue) return;
        PollAddon(AddonPollSource.FrameworkUpdate);
    }

    private bool Mutate(AddonBattleTalkState nextValue)
    {
        if (lastValue.Equals(nextValue))
        {
            return false;
        }

        lastValue = nextValue;
        return HandleChange(nextValue);
    }

    private void UpdateAddonAddress()
    {
        if (!clientState.IsLoggedIn || condition[ConditionFlag.CreatingCharacter])
        {
            Address = nint.Zero;
            return;
        }

        Address = gui.GetAddonByName("_BattleTalk");
        if (Address != nint.Zero && oldAddress != Address)
        {
            oldAddress = Address;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonBattleTalk address found: {Address}", new EKEventId(0, TextSource.AddonBattleTalk));
        }
    }

    private AddonBattleTalkState GetTalkAddonState(AddonPollSource pollSource)
    {
        if (!IsVisible())
        {
            return default;
        }

        var addonTalkText = ReadText();
        return addonTalkText != null
            ? new AddonBattleTalkState(addonTalkText.Speaker, addonTalkText.Text, pollSource)
            : default;
    }

    public bool PollAddon(AddonPollSource pollSource)
    {
        var state = GetTalkAddonState(pollSource);
        return Mutate(state);
    }

    private bool HandleChange(AddonBattleTalkState state)
    {
        var (speaker, text, pollSource) = state;

        if (state == default)
        {
            // The addon was closed
            lastAddonSpeaker = "";
            lastAddonText = "";
            return false;
        }
        EKEventId eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonBattleTalk);
        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonBattleTalk ({pollSource}): \"{state}\"", eventId);

        // Notify observers that the addon state was advanced
        if (!config.VoiceBattleDialogQueued)
            plugin.Cancel(eventId);

        text = TalkUtils.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonBattleTalk ({pollSource}): \"{text}\"", eventId);


        if (SoundHelper.VoiceLinesToCome > 0 && pollSource == AddonPollSource.FrameworkUpdate)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping maybe voice line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return false;
        }

        {
            // This entire callback executes twice in a row - once for the voice line, and then again immediately
            // afterwards for the framework update itself. This prevents the second invocation from being spoken.
            if (lastAddonSpeaker == speaker && lastAddonText == text)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping duplicate line: {text}", eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                return false;
            }

            lastAddonSpeaker = speaker;
            lastAddonText = text;
        }

        if (pollSource == AddonPollSource.VoiceLinePlayback)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return true;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(clientState, objects, speaker, eventId) : null;

        if (speakerObj != null)
        {
            plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            plugin.Say(eventId, null, state.Speaker ?? "", text);
        }

        return false;
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonTalkBattle();
        return addonTalk == null ? null : TalkUtils.ReadTalkAddon(addonTalk);
    }

    public unsafe bool IsVisible()
    {
        var addonTalk = GetAddonTalkBattle();
        return addonTalk != null && addonTalk->AtkUnitBase.IsVisible;
    }
    private unsafe AddonBattleTalk* GetAddonTalkBattle()
    {
        return (AddonBattleTalk*)Address.ToPointer();
    }

    public void Dispose()
    {
        framework.Update -= updateHandler;
    }
}
