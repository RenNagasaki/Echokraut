using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Utils;
using Echokraut.DataClasses;
using static Dalamud.Plugin.Services.IFramework;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using Echokraut.Utils;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace Echokraut.Helper;

public class AddonSelectStringHelper
{
    private record struct AddonSelectStringState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly IObjectTable objects;
    private readonly IClientState clientState;
    private readonly ICondition condition;
    private readonly IGameGui gui;
    private readonly IFramework framework;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    private OnUpdateDelegate updateHandler;

    public static nint Address { get; set; }
    private static nint oldAddress { get; set; }
    private AddonSelectStringState lastValue;

    public AddonSelectStringHelper(Echokraut plugin, IClientState clientState, ICondition condition, IGameGui gui, IFramework framework, IObjectTable objects, Configuration config)
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
        if (!config.VoicePlayerChoices) return;
        PollAddon(AddonPollSource.FrameworkUpdate);
    }

    private void Mutate(AddonSelectStringState nextValue)
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
        if (!clientState.IsLoggedIn || condition[ConditionFlag.CreatingCharacter] || !condition[ConditionFlag.OccupiedInQuestEvent])
        {
            Address = nint.Zero;
            return;
        }

        Address = gui.GetAddonByName("SelectString");
        if (Address != nint.Zero && oldAddress != Address)
        {
            oldAddress = Address;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonSelectString address found: {Address}", 0);
        }
    }

    private AddonSelectStringState GetSelectStringAddonState(AddonPollSource pollSource)
    {
        if (!IsVisible())
        {
            return default;
        }

        var addonSelectStringText = ReadText();
        return addonSelectStringText != null
            ? new AddonSelectStringState(addonSelectStringText.Speaker, addonSelectStringText.Text, pollSource)
            : default;
    }

    public void PollAddon(AddonPollSource pollSource)
    {
        var state = GetSelectStringAddonState(pollSource);
        Mutate(state);
    }

    private void HandleChange(AddonSelectStringState state)
    {
        var (speaker, text, pollSource) = state;
        var eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name);

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString ({state})", eventId);
        if (state == default)
        {
            // The addon was closed
            return;
        }

        // Notify observers that the addon state was advanced
        plugin.Cancel(eventId);

        text = TalkUtils.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonSelectString ({pollSource}): \"{text}\"", eventId);


        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(objects, speaker) : null;

        if (speakerObj != null)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString for speakerobject: ({speakerObj.Name})", eventId);
            plugin.Say(eventId, speakerObj, speakerObj.Name, text, TextSource.AddonSelectString);
        }
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString for object: ({state.Speaker})", eventId);
            plugin.Say(eventId, null, state.Speaker ?? "PLAYER", text, TextSource.AddonSelectString);
        }
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonSelectString();
        return addonTalk == null ? null : TalkUtils.ReadSelectStringAddon(addonTalk);
    }

    public unsafe bool IsVisible()
    {
        var addonSelectString = GetAddonSelectString();
        return addonSelectString != null && addonSelectString->AtkUnitBase.IsVisible;
    }

    private unsafe AddonSelectString* GetAddonSelectString()
    {
        return (AddonSelectString*)Address.ToPointer();
    }

    public void Dispose()
    {
        framework.Update -= updateHandler;
    }
}
