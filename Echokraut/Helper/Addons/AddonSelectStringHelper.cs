using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using System.Linq;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Echokraut.Helper.Addons;

public unsafe class AddonSelectStringHelper
{
    private record struct AddonSelectStringState(string? Speaker, string? Text, AddonPollSource PollSource);

    private List<string> options = new List<string>();

    public AddonSelectStringHelper()
    {
        HookIntoAddonLifecycle();
    }

    private void HookIntoAddonLifecycle()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnPostSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectString", OnPreFinalize);
    }

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled) return;
        if (!Plugin.Configuration.VoicePlayerChoices) return;
        if (!Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) return;

        GetAddonStrings(((AddonSelectString*)args.Addon)->PopupMenu.PopupMenu.List);
    }

    private unsafe void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled) return;
        if (!Plugin.Configuration.VoicePlayerChoices) return;
        if (!Plugin.Condition[ConditionFlag.OccupiedInQuestEvent]) return;

        HandleSelectedString(((AddonSelectString*)args.Addon)->PopupMenu.PopupMenu.List);
    }

    private unsafe void GetAddonStrings(AtkComponentList* list)
    {
        if (list is null) return;

        options.Clear();

        foreach (var index in Enumerable.Range(0, list->ListLength))
        {
            var listItemRenderer = list->ItemRendererList[index].AtkComponentListItemRenderer;
            if (listItemRenderer is null) continue;

            var buttonTextNode = listItemRenderer->AtkComponentButton.ButtonTextNode;
            if (buttonTextNode is null) continue;

            var buttonText = TalkTextHelper.ReadStringNode(buttonTextNode->NodeText);

            options.Add(buttonText);
        }
    }

    private unsafe void HandleSelectedString(AtkComponentList* list)
    {
        if (list is null) return;

        var selectedItem = list->SelectedItemIndex;
        if (selectedItem < 0 || selectedItem >= options.Count) return;

        var selectedString = options[selectedItem];
        var localPlayerName = Plugin.ClientState.LocalPlayer?.Name;

        HandleChange(new AddonSelectStringState()
        {
            PollSource = AddonPollSource.FrameworkUpdate,
            Text = selectedString,
            Speaker = localPlayerName.TextValue ?? "PLAYER"
        });
    }

    private void HandleChange(AddonSelectStringState state)
    {
        var (speaker, text, pollSource) = state;
        var eventId = LogHelper.Start(MethodBase.GetCurrentMethod().Name, TextSource.AddonSelectString);

        if (state == default)
        {
            // The addon was closed
            return;
        }

        // Notify observers that the addon state was advanced
        Plugin.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"\"{text}\"", eventId);


        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(Plugin.ClientState, Plugin.ObjectTable, speaker, eventId) : null;

        if (speakerObj != null)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"speakerobject: ({speakerObj.Name})", eventId);
            Plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"object: ({state.Speaker})", eventId);
            Plugin.Say(eventId, null, state.Speaker ?? "PLAYER", text);
        }
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnPostSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "SelectString", OnPreFinalize);
    }
}
