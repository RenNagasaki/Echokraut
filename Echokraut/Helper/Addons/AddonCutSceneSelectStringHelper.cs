using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.UI;
using Echokraut.Enums;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Linq;
using System.Collections.Generic;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;

namespace Echokraut.Helper.Addons;

public class AddonCutSceneSelectStringHelper
{
    private record struct AddonCutSceneSelectStringState(string? Speaker, string? Text, AddonPollSource PollSource);

    private List<string> options = new List<string>();

    public AddonCutSceneSelectStringHelper()
    {
        HookIntoAddonLifecycle();
    }

    private void HookIntoAddonLifecycle() 
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "CutSceneSelectString", OnPostSetup);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "CutSceneSelectString", OnPreFinalize);
    }

    private unsafe void OnPostSetup(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled) return;
        if (!Plugin.Configuration.VoicePlayerChoicesCutscene) return;

        GetAddonStrings(((AddonCutSceneSelectString*)args.Addon.Address)->OptionList);
    }

    private unsafe void OnPreFinalize(AddonEvent type, AddonArgs args)
    {
        if (!Plugin.Configuration.Enabled) return;
        if (!Plugin.Configuration.VoicePlayerChoicesCutscene) return;

        HandleSelectedString(((AddonCutSceneSelectString*)args.Addon.Address)->OptionList);
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

        var eventId = LogHelper.Start(MethodBase.GetCurrentMethod().Name, TextSource.AddonCutsceneSelectString);
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
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "CutSceneSelectString", OnPostSetup);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "CutSceneSelectString", OnPreFinalize);
    }
}
