using System;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;

namespace Echokraut.Helper.Addons;

public class AddonBattleTalkHelper
{
    private record struct AddonBattleTalkState(string? Speaker, string? Text);

    public bool nextIsVoice = false;
    public DateTime timeNextVoice = DateTime.Now;
    private AddonBattleTalkState lastValue;

    public AddonBattleTalkHelper()
    {
        HookIntoFrameworkUpdate();
    }

    private void HookIntoFrameworkUpdate()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostDraw, "_BattleTalk", OnPostDraw);
    }

    private unsafe void OnPostDraw(AddonEvent type, AddonArgs args)
    {
        var addonBattleTalk = (AddonBattleTalk*)args.Addon.ToPointer();
        Handle(addonBattleTalk);
    }
    private unsafe void Handle(AddonBattleTalk* addonBattleTalk)
    {
        if (!Plugin.Configuration.Enabled) return;
        if (!Plugin.Configuration.VoiceBattleDialogue) return;
        if (addonBattleTalk == null || !addonBattleTalk->Base.IsVisible) return;
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

        var eventId = LogHelper.Start(MethodBase.GetCurrentMethod().Name, TextSource.AddonBattleTalk);
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"\"{state}\"", eventId);

        // Notify observers that the addon state was advanced
        if (!Plugin.Configuration.VoiceBattleDialogQueued)
            Plugin.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"\"{text}\"", eventId);

        if (voiceNext)
        {
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Skipping voice-acted line: {text}", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            return;
        }

        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(Plugin.ClientState, Plugin.ObjectTable, speaker, eventId) : null;

        if (speakerObj != null)
        {
            Plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            Plugin.Say(eventId, null, state.Speaker ?? "", text);
        }

        return;
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostDraw, "_BattleTalk", OnPostDraw);
    }
}
