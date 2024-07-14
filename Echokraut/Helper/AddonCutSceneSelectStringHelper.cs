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

namespace Echokraut.Helper;

public class AddonCutSceneSelectStringHelper
{
    private record struct AddonCutSceneSelectStringState(string? Speaker, string? Text, AddonPollSource PollSource);

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
    private AddonCutSceneSelectStringState lastValue;

    public AddonCutSceneSelectStringHelper(Echokraut plugin, IClientState clientState, ICondition condition, IGameGui gui, IFramework framework, IObjectTable objects, Configuration config)
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
        if (!config.VoicePlayerChoicesCutscene) return;
        PollAddon(AddonPollSource.FrameworkUpdate);
    }

    private void Mutate(AddonCutSceneSelectStringState nextValue)
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

        Address = gui.GetAddonByName("CutSceneSelectString");
        if (Address != nint.Zero && oldAddress != Address)
        {
            oldAddress = Address;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonCutSceneSelectString address found: {Address}");
        }
    }

    private AddonCutSceneSelectStringState GetCutSceneSelectStringAddonState(AddonPollSource pollSource)
    {
        if (!IsVisible())
        {
            return default;
        }

        var addonCutSceneSelectStringText = ReadText();
        return addonCutSceneSelectStringText != null
            ? new AddonCutSceneSelectStringState(addonCutSceneSelectStringText.Speaker, addonCutSceneSelectStringText.Text, pollSource)
            : default;
    }

    public void PollAddon(AddonPollSource pollSource)
    {
        var state = GetCutSceneSelectStringAddonState(pollSource);
        Mutate(state);
    }

    private void HandleChange(AddonCutSceneSelectStringState state)
    {
        var (speaker, text, pollSource) = state;

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonCutSceneSelectString ({state})");
        if (state == default)
        {
            return;
        }

        // Notify observers that the addon state was advanced
        plugin.Cancel();

        text = TalkUtils.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonCutSceneSelectString ({pollSource}): \"{text}\"");


        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? ObjectTableUtils.GetGameObjectByName(objects, speaker) : null;

        if (speakerObj != null)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString for speakerobject: ({speakerObj.Name})");
            plugin.Say(speakerObj, speakerObj.Name, text, TextSource.AddonCutSceneSelectString);
        }
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString for object: ({state.Speaker})");
            plugin.Say(null, state.Speaker ?? "PLAYER", text, TextSource.AddonCutSceneSelectString);
        }
    }

    public unsafe AddonTalkText? ReadText()
    {
        var addonTalk = GetAddonCutSceneSelectString();
        return addonTalk == null ? null : TalkUtils.ReadCutSceneSelectStringAddon(addonTalk);
    }

    public unsafe bool IsVisible()
    {
        var addonSelectString = GetAddonCutSceneSelectString();
        return addonSelectString != null && addonSelectString->AtkUnitBase.IsVisible;
    }

    private unsafe AddonCutSceneSelectString* GetAddonCutSceneSelectString()
    {
        return (AddonCutSceneSelectString*)Address.ToPointer();
    }

    public void Dispose()
    {
        framework.Update -= updateHandler;
    }
}
