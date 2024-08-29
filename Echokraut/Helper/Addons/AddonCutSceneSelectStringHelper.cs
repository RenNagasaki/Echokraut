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
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using static System.Windows.Forms.Design.AxImporter;
using System.Linq;
using System.Collections.Generic;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Addons;

public class AddonCutSceneSelectStringHelper
{
    private record struct AddonCutSceneSelectStringState(string? Speaker, string? Text, AddonPollSource PollSource);

    private readonly IAddonLifecycle addonLifecycle;
    private readonly IClientState clientState;
    private readonly IObjectTable objects;
    private readonly Configuration config;
    private readonly Echokraut plugin;
    private List<string> options = new List<string>();

    public AddonCutSceneSelectStringHelper(Echokraut plugin, IAddonLifecycle addonLifecycle, IClientState clientState, IObjectTable objects, Configuration config)
    {
        this.plugin = plugin;
        this.addonLifecycle = addonLifecycle;
        this.clientState = clientState;
        this.config = config;
        this.objects = objects;

        HookIntoAddonLifecycle();
    }

    private void HookIntoAddonLifecycle()
    {
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, "CutSceneSelectString", OnPostSetup);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "CutSceneSelectString", OnPreFinalize);
    }

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!config.Enabled) return;
        if (!config.VoicePlayerChoicesCutscene) return;

        GetAddonStrings(((AddonCutSceneSelectString*)args.Addon)->OptionList);
    }

    private unsafe void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        if (!config.Enabled) return;
        if (!config.VoicePlayerChoicesCutscene) return;

        HandleSelectedString(((AddonCutSceneSelectString*)args.Addon)->OptionList);
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

        HandleChange(new AddonCutSceneSelectStringState()
        {
            PollSource = AddonPollSource.FrameworkUpdate,
            Text = selectedString,
            Speaker = localPlayerName.TextValue ?? "PLAYER"
        });
    }

    private void HandleChange(AddonCutSceneSelectStringState state)
    {
        var (speaker, text, pollSource) = state;

        if (state == default)
        {
            return;
        }

        var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonCutsceneSelectString);
        // Notify observers that the addon state was advanced
        plugin.Cancel(eventId);

        text = TalkTextHelper.NormalizePunctuation(text);

        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"\"{text}\"", eventId);


        // Find the game object this speaker is representing
        var speakerObj = speaker != null ? DalamudHelper.GetGameObjectByName(clientState, objects, speaker, eventId) : null;

        if (speakerObj != null)
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"speakerobject: ({speakerObj.Name})", eventId);
            plugin.Say(eventId, speakerObj, speakerObj.Name, text);
        }
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"object: ({state.Speaker})", eventId);
            plugin.Say(eventId, null, state.Speaker ?? "PLAYER", text);
        }
    }

    public void Dispose()
    {
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CutSceneSelectString", OnPostSetup);
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "CutSceneSelectString", OnPreFinalize);
    }
}
