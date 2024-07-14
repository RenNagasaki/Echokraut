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

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Echokraut plugin;
    private string testConnectionRes = "";

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Echokraut plugin, Configuration configuration) : base("Echokraut configuration###EKSettings")
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

    private void DrawSettings()
    {
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

                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}");
            }

            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
            {
                if (ImGui.InputText($"Base Url##EKBaseUrl", ref this.Configuration.Alltalk.BaseUrl, 40))
                    this.Configuration.Save();

            }

            if (ImGui.Button($"Test Connection##EKTestConnection"))
            {
                BackendCheckReady();
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
            if (ImGui.InputText($"Local save path##EKSavePath", ref localSaveLocation, 40))
            {
                this.Configuration.LocalSaveLocation = localSaveLocation;
                this.Configuration.Save();
            }
        }
    }

    private async void BackendCheckReady()
    {
        try
        {
            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
                testConnectionRes = await BackendHelper.CheckReady();
            else
                testConnectionRes = "No backend selected";
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {testConnectionRes}");
        }
        catch (Exception ex)
        {
            testConnectionRes = ex.ToString();
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString());
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
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString());
        }
    }

    private void DrawNpcs()
    {
        var voices = BackendHelper.mappedVoices;
        var voicesDisplay = voices.Select(b => b.ToString()).ToArray();
        NpcMapData toBeRemoved = null;
        foreach (NpcMapData mapData in Configuration.MappedNpcs)
        {
            var presetIndex = voices.IndexOf(mapData.voiceItem);
            if (ImGui.Combo($"{mapData.ToString(true)}##EKCBoxNPC{mapData}", ref presetIndex, voicesDisplay, voicesDisplay.Length))
            {
                var newVoiceItem = voices[presetIndex];

                if (newVoiceItem != null && newVoiceItem.voiceName != "Remove")
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData} from: {mapData.voiceItem} to: {newVoiceItem}");

                    mapData.voiceItem = newVoiceItem;
                    this.Configuration.Save();
                }
                else if (newVoiceItem.voiceName == "Remove")
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Removing Configuration for Character: {mapData}");
                    toBeRemoved = mapData;
                }
                else
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}");
            }

        }

        if (toBeRemoved != null)
        {
            Configuration.MappedNpcs.Remove(toBeRemoved);
            Configuration.Save();
        }
    }

    private void DrawLogs()
    {
        if (ImGui.CollapsingHeader("Options"))
        {
            var showInfoLog = this.Configuration.ShowInfoLog;
            if (ImGui.Checkbox("Show info logs", ref showInfoLog))
            {
                LogHelper.RecreateLogList();
                this.Configuration.ShowInfoLog = showInfoLog;
                this.Configuration.Save();
            }
            var showDebugLog = this.Configuration.ShowDebugLog;
            if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
            {
                LogHelper.RecreateLogList();
                this.Configuration.ShowDebugLog = showDebugLog;
                this.Configuration.Save();
            }
            var showErrorLog = this.Configuration.ShowErrorLog;
            if (ImGui.Checkbox("Show error logs", ref showErrorLog))
            {
                LogHelper.RecreateLogList();
                this.Configuration.ShowErrorLog = showErrorLog;
                this.Configuration.Save();
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
            Dictionary<DateTime, LogMessage> logMessages = LogHelper.logList;

            if (ImGui.BeginChild("LogsChild"))
            {
                foreach (var logMessage in logMessages)
                {
                    var text = $"{logMessage.Key.ToString("HH:mm:ss.fff")}: {logMessage.Value.message}";
                    ImGui.TextColored(logMessage.Value.color, text);
                }

                if (Configuration.JumpToBottom)
                {
                    ImGui.SetScrollHereY();
                }
            }
        }
    }
}
