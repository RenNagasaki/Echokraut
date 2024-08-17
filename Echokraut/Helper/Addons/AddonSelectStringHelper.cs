using System;
using Dalamud.Plugin.Services;
using R3;
using Echokraut.TextToTalk.Utils;
using Echokraut.DataClasses;
using static Dalamud.Plugin.Services.IFramework;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using static System.Windows.Forms.AxHost;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static System.Windows.Forms.Design.AxImporter;
using System.Collections.Generic;
using Lumina.Data.Parsing;
using System.Linq;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Addons;

public class AddonSelectStringHelper
{
    private record struct AddonSelectStringState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    private List<string> options = new List<string>();

    public AddonSelectStringHelper(Echokraut plugin, IAddonLifecycle addonLifecycle, IClientState clientState, IObjectTable objects, ICondition condition, Configuration config)
    {
        this.plugin = plugin;
        this.addonLifecycle = addonLifecycle;
        this.clientState = clientState;
        this.config = config;
        this.objects = objects;
        this.condition = condition;

        HookIntoAddonLifecycle();
    }

    private void HookIntoAddonLifecycle()
    {
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", OnPostSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "SelectString", OnPreFinalize);
    }

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!config.Enabled) return;
        if (!config.VoicePlayerChoices) return;
        if (!condition[ConditionFlag.OccupiedInQuestEvent]) return;

        GetAddonStrings(((AddonSelectString*)args.Addon)->PopupMenu.PopupMenu.List);
    }

    private unsafe void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        if (!config.Enabled) return;
        if (!config.VoicePlayerChoices) return;
        if (!condition[ConditionFlag.OccupiedInQuestEvent]) return;

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
        var localPlayerName = clientState.LocalPlayer?.Name;

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
        var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonSelectString);

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString ({state})", eventId);
        if (state == default)
        {
            // The addon was closed
            return;
        }

        // Notify observers that the addon state was advanced
        plugin.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"AddonSelectString ({pollSource}): \"{text}\"", eventId);


        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(clientState, objects, speaker, eventId) : null;

        if (speakerObj != null)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString for speakerobject: ({speakerObj.Name})", eventId);
            plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"AddonSelectString for object: ({state.Speaker})", eventId);
            plugin.Say(eventId, null, state.Speaker ?? "PLAYER", text);
        }
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", OnPostSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "SelectString", OnPreFinalize);
    }
}
