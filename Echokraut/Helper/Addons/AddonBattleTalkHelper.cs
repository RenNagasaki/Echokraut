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
using static System.Net.Mime.MediaTypeNames;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using static Lumina.Models.Models.Model;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Addons;

public class AddonBattleTalkHelper
{
    private record struct AddonBattleTalkState(string? Speaker, string? Text);

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    public bool nextIsVoice = false;
    public DateTime timeNextVoice = DateTime.Now;
    private AddonBattleTalkState lastValue;

    public AddonBattleTalkHelper(Echokraut plugin, IAddonLifecycle addonLifecycle, IClientState clientState, IObjectTable objects, Configuration config)
    {
        this.addonLifecycle = addonLifecycle;
        this.clientState = clientState;
        this.plugin = plugin;
        this.config = config;
        this.objects = objects;

        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        addonLifecycle.RegisterListener(AddonEvent.PostDraw, "_BattleTalk", OnPostDraw);
    }

    private unsafe void OnPostDraw(AddonEvent type, AddonArgs args)
    {
        var addonBattleTalk = (AddonBattleTalk*)args.Addon.ToPointer();
        Handle(addonBattleTalk);
    }
    private unsafe void Handle(AddonBattleTalk* addonBattleTalk)
    {
        if (!config.Enabled) return;
        if (!config.VoiceBattleDialogue) return;
        if (addonBattleTalk == null || !addonBattleTalk->AtkUnitBase.IsVisible) return;
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

        var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonBattleTalk);
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"\"{state}\"", eventId);

        // Notify observers that the addon state was advanced
        if (!config.VoiceBattleDialogQueued)
            plugin.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"\"{text}\"", eventId);

        if (voiceNext)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(clientState, objects, speaker, eventId) : null;

        if (speakerObj != null)
        {
            plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            plugin.Say(eventId, null, state.Speaker ?? "", text);
        }

        return;
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "_BattleTalk", OnPostDraw);
    }
}
