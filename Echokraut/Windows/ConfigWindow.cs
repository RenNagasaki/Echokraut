using System;
using System.Collections.Generic;
using System.Numerics;
using System.Xml.Linq;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using ImGuiNET;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using Echokraut.Backend;
using Echokraut.Helper;
using System.Reflection;
using System.IO;
using Dalamud.Interface.ImGuiFileDialog;
using OtterGui;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Echokraut plugin;
    private string testConnectionRes = "";
    private List<BackendVoiceItem> voices;
    private FileDialogManager fileDialogManager;
    private bool resetLogFilter = true;
    private string logFilter = "";
    private bool resetPlayerFilter = true;
    private string playerFilter = "";
    private List<NpcMapData> filteredPlayers;
    private bool resetNpcFilter = true;
    private string npcFilter = "";
    private List<NpcMapData> filteredNpcs;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Echokraut plugin, Configuration configuration) : base($"Echokraut configuration###EKSettings")
    {
        this.plugin = plugin;

        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar & ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        Configuration = configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        try 
        { 
            if (ImGui.BeginTabBar($"Echokraut##EKTab"))
            {
                if (ImGui.BeginTabItem("Settings"))
                {
                    DrawSettings();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("NPCs"))
                {
                    DrawNpcs();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Log"))
                {
                    DrawLogs();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", 0);
        }
    }

    private void DrawSettings()
    {
        try { 
            if (ImGui.CollapsingHeader("General"))
            {
                var enabled = this.Configuration.Enabled;
                if (ImGui.Checkbox("Enabled", ref enabled))
                {
                    this.Configuration.Enabled = enabled;
                    this.Configuration.Save();
                }
                var voiceDialog = this.Configuration.VoiceDialog;
                if (ImGui.Checkbox("Voice dialog", ref voiceDialog))
                {
                    this.Configuration.VoiceDialog = voiceDialog;
                    this.Configuration.Save();
                }
                var voiceBattleDialog = this.Configuration.VoiceBattleDialog;
                if (ImGui.Checkbox("Voice battle dialog", ref voiceBattleDialog))
                {
                    this.Configuration.VoiceBattleDialog = voiceBattleDialog;
                    this.Configuration.Save();
                }
                var voiceBattleDialogQueued = this.Configuration.VoiceBattleDialogQueued;
                if (ImGui.Checkbox("Voice battle dialog in a queue", ref voiceBattleDialogQueued))
                {
                    this.Configuration.VoiceBattleDialogQueued = voiceBattleDialogQueued;
                    this.Configuration.Save();
                }
                var voicePlayerChoicesCutscene = this.Configuration.VoicePlayerChoicesCutscene;
                if (ImGui.Checkbox("Voice player choices in cutscene", ref voicePlayerChoicesCutscene))
                {
                    this.Configuration.VoicePlayerChoicesCutscene = voicePlayerChoicesCutscene;
                    this.Configuration.Save();
                }
                var voicePlayerChoices = this.Configuration.VoicePlayerChoices;
                if (ImGui.Checkbox("Voice player choices outside of cutscene", ref voicePlayerChoices))
                {
                    this.Configuration.VoicePlayerChoices = voicePlayerChoices;
                    this.Configuration.Save();
                }
                var cancelAdvance = this.Configuration.CancelSpeechOnTextAdvance;
                if (ImGui.Checkbox("Cancel voice on text advance", ref cancelAdvance))
                {
                    this.Configuration.CancelSpeechOnTextAdvance = cancelAdvance;
                    this.Configuration.Save();
                }
                var autoAdvanceOnSpeechCompletion = this.Configuration.AutoAdvanceTextAfterSpeechCompleted;
                if (ImGui.Checkbox("Auto advance text on speech completion", ref autoAdvanceOnSpeechCompletion))
                {
                    this.Configuration.AutoAdvanceTextAfterSpeechCompleted = autoAdvanceOnSpeechCompletion;
                    this.Configuration.Save();
                }
                var removeStutters = this.Configuration.RemoveStutters;
                if (ImGui.Checkbox("Remove stutters", ref removeStutters))
                {
                    this.Configuration.RemoveStutters = removeStutters;
                    this.Configuration.Save();
                }
            }


            if (ImGui.CollapsingHeader("Backend"))
            {
                var backends = Enum.GetValues<TTSBackends>().ToArray();
                var backendsDisplay = backends.Select(b => b.ToString()).ToArray();
                var presetIndex = Enum.GetValues<TTSBackends>().ToList().IndexOf(this.Configuration.BackendSelection);
                if (ImGui.Combo($"Select Backend##EKCBoxBackend", ref presetIndex, backendsDisplay, backendsDisplay.Length))
                {
                    var backendSelection = backends[presetIndex];
                    this.Configuration.BackendSelection = backendSelection; 
                    this.Configuration.Save();
                    BackendHelper.SetBackendType(backendSelection);

                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}", 0);
                }

                if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
                {
                    if (ImGui.InputText($"Base Url##EKBaseUrl", ref this.Configuration.Alltalk.BaseUrl, 40))
                        this.Configuration.Save();

                }

                if (ImGui.Button($"Test Connection##EKTestConnection"))
                {
                    BackendCheckReady(0);
                }

                if (ImGui.Button($"Load Voices##EKLoadVoices"))
                {
                    BackendGetVoices();
                }

                if (!string.IsNullOrWhiteSpace(testConnectionRes))
                    ImGui.TextColored(new(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
            }


            if (ImGui.CollapsingHeader("Save locally"))
            {
                var saveLocally = this.Configuration.SaveToLocal;
                if (ImGui.Checkbox("Save generated audio locally", ref saveLocally))
                {
                    this.Configuration.SaveToLocal = saveLocally;
                    this.Configuration.Save();
                }
                var loadLocalFirst = this.Configuration.LoadFromLocalFirst;
                if (ImGui.Checkbox("Search audio locally first before generating", ref loadLocalFirst))
                {
                    this.Configuration.LoadFromLocalFirst = loadLocalFirst;
                    this.Configuration.Save();
                }
                var createMissingLocalSave = this.Configuration.CreateMissingLocalSaveLocation;
                if (ImGui.Checkbox("Create directory if not existing", ref createMissingLocalSave))
                {
                    this.Configuration.CreateMissingLocalSaveLocation = createMissingLocalSave;
                    this.Configuration.Save();
                }

                string localSaveLocation = this.Configuration.LocalSaveLocation;
                if (ImGui.InputText($"##EKSavePath", ref localSaveLocation, 40))
                {
                    this.Configuration.LocalSaveLocation = localSaveLocation;
                    this.Configuration.Save();
                }
                ImGui.SameLine();
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##import", new Vector2(25, 25),
                        "Select a directory via dialog.", false, true))
                {
                    var startDir = this.Configuration.LocalSaveLocation.Length > 0 && Directory.Exists(this.Configuration.LocalSaveLocation)
                    ? this.Configuration.LocalSaveLocation
                        : null;

                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {startDir}", 0);
                    fileDialogManager = new FileDialogManager();
                    fileDialogManager.OpenFolderDialog("Choose audiofiles directory", (b, s) =>
                    {
                        if (!b)
                            return;

                        this.Configuration.LocalSaveLocation = s;
                        this.Configuration.Save();
                        fileDialogManager = null;
                    }, startDir, false);
                }

                if (fileDialogManager != null)
                {
                    fileDialogManager.Draw();
                }

            }


            if (ImGui.CollapsingHeader("Bubbles"))
            {
                var voiceBubbles = this.Configuration.VoiceBubbles;
                if (ImGui.Checkbox("Voice NPC Bubbles", ref voiceBubbles))
                {
                    this.Configuration.VoiceBubbles = voiceBubbles;
                    this.Configuration.Save();
                }
                var voiceBubblesInCity = this.Configuration.VoiceBubblesInCity;
                if (ImGui.Checkbox("Voice NPC Bubbles in City", ref voiceBubblesInCity))
                {
                    this.Configuration.VoiceBubblesInCity = voiceBubblesInCity;
                    this.Configuration.Save();
                }
                var voiceSourceCam = this.Configuration.VoiceSourceCam;
                if (ImGui.Checkbox("Voice Bubbles with camera as center", ref voiceSourceCam))
                {
                    this.Configuration.VoiceSourceCam = voiceSourceCam;
                    this.Configuration.Save();
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", 0);
        }
    }

    private async void BackendCheckReady(int eventId)
    {
        try
        {
            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
                testConnectionRes = await BackendHelper.CheckReady(eventId);
            else
                testConnectionRes = "No backend selected";
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {testConnectionRes}", eventId);
        }
        catch (Exception ex)
        {
            testConnectionRes = ex.ToString();
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
        }
    }

    private async void BackendGetVoices()
    {
        try
        {
            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
              BackendHelper.SetBackendType(this.Configuration.BackendSelection);
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), 0);
        }
    }

    private void DrawNpcs()
    {
        try
        {
            if (ImGui.CollapsingHeader("Options:"))
            {
                var voicesAllOriginals = this.Configuration.VoicesAllOriginals;
                if (ImGui.Checkbox("Show all original voices as option", ref voicesAllOriginals))
                {
                    this.Configuration.VoicesAllOriginals = voicesAllOriginals;
                    this.Configuration.Save();

                    voices = BackendHelper.mappedVoices;
                    if (!voicesAllOriginals)
                        voices = voices.FindAll(b => b.voiceName.Contains("NPC"));
                }
                var voicesAllGenders = this.Configuration.VoicesAllGenders;
                if (ImGui.Checkbox("Show both genders as option", ref voicesAllGenders))
                {
                    this.Configuration.VoicesAllGenders = voicesAllGenders;
                    this.Configuration.Save();
                }
                var voicesAllRaces = this.Configuration.VoicesAllRaces;
                if (ImGui.Checkbox("Show all races as option", ref voicesAllRaces))
                {
                    this.Configuration.VoicesAllRaces = voicesAllRaces;
                    this.Configuration.Save();
                }
                var voicesAllBubbles = this.Configuration.VoicesAllBubbles;
                if (ImGui.Checkbox("Show all bubble npcs as option", ref voicesAllBubbles))
                {
                    this.Configuration.VoicesAllBubbles = voicesAllBubbles;
                    this.Configuration.Save();
                }
            }

            if (ImGui.CollapsingHeader("NPCs:"))
            {

                if (voices == null)
                {
                    voices = BackendHelper.mappedVoices;
                    if (!this.Configuration.VoicesAllOriginals)
                        voices = voices.FindAll(b => b.voiceName.Contains("NPC"));
                }

                if (filteredNpcs == null)
                    filteredNpcs = Configuration.MappedNpcs;

                if (ImGui.InputText($"Filter by npc name##EKFilterNpc", ref npcFilter, 40))
                {
                    filteredNpcs = Configuration.MappedNpcs.FindAll(b => b.name.ToLower().Contains(npcFilter.ToLower()));
                    resetNpcFilter = false;
                }
                else if (!resetNpcFilter && npcFilter.Length == 0)
                {
                    filteredNpcs = Configuration.MappedNpcs;
                }

                if (ImGui.BeginChild("LogsChild"))
                {
                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredNpcs)
                    {
                        if (!this.Configuration.VoicesAllOriginals && mapData.voiceItem.voiceName.ToLower().Contains(mapData.name.ToLower()))
                            continue;

                        if (!this.Configuration.VoicesAllBubbles && mapData.name.StartsWith("Bubble"))
                            continue;

                        var localVoices = new List<BackendVoiceItem>(voices);

                        if (!this.Configuration.VoicesAllGenders)
                            localVoices = localVoices.FindAll(b => b.gender == mapData.gender || (mapData.gender == Gender.None));

                        if (!this.Configuration.VoicesAllRaces)
                            localVoices = localVoices.FindAll(b => b.race == mapData.race);

                        localVoices = localVoices.OrderBy(p => p.ToString()).ToList();
                        localVoices.Insert(0, new BackendVoiceItem() { voiceName = "Remove", race = NpcRaces.Default, gender = Gender.None });
                        var voicesDisplay = localVoices.Select(b => b.ToString()).ToArray();
                        var presetIndex = localVoices.FindIndex(p => p.ToString() == mapData.voiceItem.ToString());
                        if (ImGui.Combo($"{mapData.ToString(true)}##EKCBoxNPC{mapData.ToString(Configuration.VoicesAllRaces)}", ref presetIndex, voicesDisplay, voicesDisplay.Length))
                        {
                            var newVoiceItem = localVoices[presetIndex];

                            if (newVoiceItem != null && newVoiceItem.voiceName != "Remove")
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString(true)} from: {mapData.voiceItem} to: {newVoiceItem}", 0);

                                mapData.voiceItem = newVoiceItem;
                                this.Configuration.Save();
                            }
                            else if (newVoiceItem.voiceName == "Remove")
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Removing Configuration for Character: {mapData}", 0);
                                toBeRemoved = mapData;
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", 0);
                        }

                    }

                    if (toBeRemoved != null)
                    {
                        Configuration.MappedNpcs.Remove(toBeRemoved);
                        Configuration.Save();
                    }
                }
            }

            if (ImGui.CollapsingHeader("Players:"))
            {

                if (voices == null)
                {
                    voices = BackendHelper.mappedVoices;
                    if (!this.Configuration.VoicesAllOriginals)
                        voices = voices.FindAll(b => b.voiceName.Contains("NPC"));
                }

                if (filteredPlayers == null)
                    filteredPlayers = Configuration.MappedPlayers;

                if (ImGui.InputText($"Filter by player name##EKFilterPlayer", ref playerFilter, 40))
                {
                    filteredPlayers = Configuration.MappedPlayers.FindAll(b => b.name.ToLower().Contains(playerFilter.ToLower()));
                    resetPlayerFilter = false;
                }
                else if (!resetPlayerFilter && playerFilter.Length == 0)
                {
                    filteredPlayers = Configuration.MappedPlayers;
                }

                if (ImGui.BeginChild("LogsChild"))
                {
                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredPlayers)
                    {
                        if (!this.Configuration.VoicesAllOriginals && mapData.voiceItem.voiceName.ToLower().Contains(mapData.name.ToLower()))
                            continue;

                        var localVoices = new List<BackendVoiceItem>(voices);

                        if (!this.Configuration.VoicesAllGenders)
                            localVoices = localVoices.FindAll(b => b.gender == mapData.gender || (mapData.gender == Gender.None));

                        if (!this.Configuration.VoicesAllRaces)
                            localVoices = localVoices.FindAll(b => b.race == mapData.race);

                        localVoices = localVoices.OrderBy(p => p.ToString()).ToList();
                        localVoices.Insert(0, new BackendVoiceItem() { voiceName = "Remove", race = NpcRaces.Default, gender = Gender.None });
                        var voicesDisplay = localVoices.Select(b => b.ToString()).ToArray();
                        var presetIndex = localVoices.FindIndex(p => p.ToString() == mapData.voiceItem.ToString());
                        if (ImGui.Combo($"{mapData.ToString(true)}##EKCBoxNPC{mapData.ToString(Configuration.VoicesAllRaces)}", ref presetIndex, voicesDisplay, voicesDisplay.Length))
                        {
                            var newVoiceItem = localVoices[presetIndex];

                            if (newVoiceItem != null && newVoiceItem.voiceName != "Remove")
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString(true)} from: {mapData.voiceItem} to: {newVoiceItem}", 0);

                                mapData.voiceItem = newVoiceItem;
                                this.Configuration.Save();
                            }
                            else if (newVoiceItem.voiceName == "Remove")
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Removing Configuration for Character: {mapData}", 0);
                                toBeRemoved = mapData;
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", 0);
                        }

                    }

                    if (toBeRemoved != null)
                    {
                        Configuration.MappedPlayers.Remove(toBeRemoved);
                        Configuration.Save();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", 0);
        }
    }

    private void DrawLogs()
    {
        try
        {
            if (ImGui.CollapsingHeader("Options:"))
            {
                var showInfoLog = this.Configuration.ShowInfoLog;
                if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                {
                    this.Configuration.ShowInfoLog = showInfoLog;
                    this.Configuration.Save();
                    LogHelper.RecreateLogList();
                }
                var showDebugLog = this.Configuration.ShowDebugLog;
                if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                {
                    this.Configuration.ShowDebugLog = showDebugLog;
                    this.Configuration.Save();
                    LogHelper.RecreateLogList();
                }
                var showErrorLog = this.Configuration.ShowErrorLog;
                if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                {
                    this.Configuration.ShowErrorLog = showErrorLog;
                    this.Configuration.Save();
                    LogHelper.RecreateLogList();
                }
                var jumpToBottom = this.Configuration.JumpToBottom;
                if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                {
                    this.Configuration.JumpToBottom = jumpToBottom;
                    this.Configuration.Save();
                }
            }
            if (ImGui.CollapsingHeader("Log:"))
            {
                List<LogMessage> logMessages = LogHelper.logList;

                if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                {
                    LogHelper.FilterLogList(logFilter);
                    resetLogFilter = false;
                }
                else if (!resetLogFilter && logFilter.Length == 0)
                {
                    LogHelper.RecreateLogList();
                }

                if (ImGui.BeginChild("LogsChild"))
                {
                    foreach (var logMessage in logMessages)
                    {
                        var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                        ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                        ImGui.TextUnformatted(text);
                        ImGui.PopStyleColor();
                    }

                    if (Configuration.JumpToBottom)
                    {
                        ImGui.SetScrollHereY();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", 0, false);
        }
    }
}
