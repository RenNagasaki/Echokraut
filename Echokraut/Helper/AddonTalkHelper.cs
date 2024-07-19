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
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Anamnesis.Memory;
using Character = Dalamud.Game.ClientState.Objects.Types.ICharacter;
using Dalamud.Game.ClientState.Objects.Types;
using Anamnesis.Services;
using Anamnesis.GameData.Excel;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Echokraut.Helper;

public class AddonTalkHelper
{
    private record struct AddonTalkState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly IObjectTable objects;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    private OnUpdateDelegate updateHandler;

    private readonly string name;

    public static nint Address { get; set; }
    private static nint oldAddress {  get; set; }

    // Most recent speaker/text specific to this addon
    private string? lastAddonSpeaker;
    private string? lastAddonText;
    private AddonTalkState lastValue;

    public AddonTalkHelper(Echokraut plugin, IClientState clientState, ICondition condition, IGameGui gui, IFramework framework, IObjectTable objects, Configuration config)
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
        if (!config.VoiceDialog) return;
        PollAddon(AddonPollSource.FrameworkUpdate);
    }

    private void Mutate(AddonTalkState nextValue)
    {
        if (lastValue.Equals(nextValue))
        {
            return;
        }

        lastValue = nextValue;
        HandleChange(nextValue);
    }

    private void UpdateAddonAddress()
    {
        if (!clientState.IsLoggedIn || condition[ConditionFlag.CreatingCharacter])
        {
            Address = nint.Zero;
            return;
        }

        Address = gui.GetAddonByName("Talk");
        if (Address != nint.Zero && oldAddress != Address)
        {
            oldAddress = Address;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonTalk address found: {Address}", 0);
        }
    }

    private AddonTalkState GetTalkAddonState(AddonPollSource pollSource)
    {
        if (!IsVisible())
        {
            return default;
        }

        var addonTalkText = ReadText();
        return addonTalkText != null
            ? new AddonTalkState(addonTalkText.Speaker, addonTalkText.Text, pollSource)
            : default;
    }

    public void PollAddon(AddonPollSource pollSource)
    {
        var state = GetTalkAddonState(pollSource);
        Mutate(state);
    }

    private void HandleChange(AddonTalkState state)
    {
        var (speaker, text, pollSource) = state;

        if (state == default)
        {
            PlayingHelper.InDialog = false;
            // The addon was closed
            plugin.Cancel(0);
            lastAddonSpeaker = "";
            lastAddonText = "";
            return;
        }
        var eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name);

        // Notify observers that the addon state was advanced
        plugin.Cancel(eventId);

        text = TalkUtils.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonTalk ({pollSource}): \"{text}\"", eventId);

        {
            // This entire callback executes twice in a row - once for the voice line, and then again immediately
            // afterwards for the framework update itself. This prevents the second invocation from being spoken.
            if (lastAddonSpeaker == speaker && lastAddonText == text)
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping duplicate line: {text}", eventId);
                return;
            }

            lastAddonSpeaker = speaker;
            lastAddonText = text;
        }

        if (pollSource == AddonPollSource.VoiceLinePlayback)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(objects, speaker) : null;

        PlayingHelper.InDialog = true;
        LogHelper.Debug("TalkHelper.HandleChange", "Setting inDialog true", eventId);

        if (speakerObj != null)
        {
            plugin.Say(eventId, speakerObj, speakerObj.Name, text, TextSource.AddonTalk);
        }
        else
        {
            plugin.Say(eventId, null, state.Speaker ?? "", text, TextSource.AddonTalk);
        }
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonTalk();
        return addonTalk == null ? null : TalkUtils.ReadTalkAddon(addonTalk);
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

    public void Click(int eventId)
    {
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Auto advancing...", eventId);
        ClickHelper.Click(Address);
    }

    public void Dispose()
    {
        framework.Update -= updateHandler;
    }
}
