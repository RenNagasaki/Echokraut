using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using ImGuiNET;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using Echokraut.Helper;
using System.Reflection;
using System.IO;
using Dalamud.Interface.ImGuiFileDialog;
using OtterGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using static System.Net.Mime.MediaTypeNames;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Xml.Linq;
using OtterGui.Widgets;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Anamnesis.GameData;
using Dalamud.Plugin;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Echokraut echokraut;
    private IDalamudPluginInterface pluginInterface;
    private string testConnectionRes = "";
    private FileDialogManager fileDialogManager;
    private List<NpcMapData> filteredNpcs;
    public static bool UpdateDataNpcs = false;
    public bool resetDataNpcs = false;
    private string filterGenderNpcs = "";
    private string filterRaceNpcs = "";
    private string filterNameNpcs = "";
    private string filterVoiceNpcs = "";
    private List<NpcMapData> filteredPlayers;
    public static bool UpdateDataPlayers = false;
    public bool resetDataPlayers = false;
    private string filterGenderPlayers = "";
    private string filterRacePlayers = "";
    private string filterNamePlayers = "";
    private string filterVoicePlayers = "";
    private List<NpcMapData> filteredBubbles;
    public static bool UpdateDataBubbles = false;
    public bool resetDataBubbles = false;
    private string filterGenderBubbles = "";
    private string filterRaceBubbles = "";
    private string filterNameBubbles = "";
    private string filterVoiceBubbles = "";
    private List<BackendVoiceItem> filteredVoices;
    public static bool UpdateDataVoices = false;
    public bool resetDataVoices = false;
    private string filterGenderVoices = "";
    private string filterRaceVoices = "";
    private string filterNameVoices = "";
    private string filterVoices = "";
    private string logFilter = "";
    private bool resetLogFilter = true;
    private string originalText = "";
    private string correctedText = "";
    private IClientState clientState;
    private unsafe Camera* camera;
    private unsafe IPlayerCharacter localPlayer;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Echokraut plugin, Configuration configuration, IClientState clientState, IDalamudPluginInterface pluginInterface) : base($"Echokraut configuration###EKSettings")
    {
        this.echokraut = plugin;
        this.clientState = clientState;
        this.pluginInterface = pluginInterface;

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

                if (ImGui.BeginTabItem("Voice selection"))
                {
                    DrawVoiceSelection();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Phonetic corrections"))
                {
                    DrawPhoneticCorrections();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Logs"))
                {
                    DrawLogs();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    #region Settings
    private void DrawSettings()
    {
        try
        {
            if (ImGui.BeginTabBar($"Settings##EKSettingsTab"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    DrawGeneralSettings();
                    ImGui.EndTabItem();

                    if (ImGui.Button("Clear mapped npcs##clearnpc"))
                    {
                        foreach (NpcMapData npcMapData in this.Configuration.MappedNpcs.FindAll(p => !p.name.StartsWith("BB") && !p.doNotDelete))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, npcMapData.name);
                            this.Configuration.MappedNpcs.Remove(npcMapData);
                        }
                        UpdateDataNpcs = true;
                        this.Configuration.Save();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear mapped players##clearplayers"))
                    {
                        foreach (NpcMapData playerMapData in this.Configuration.MappedPlayers.FindAll(p => !p.doNotDelete))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, playerMapData.name);
                            this.Configuration.MappedPlayers.Remove(playerMapData);
                        }
                        UpdateDataPlayers = true;
                        this.Configuration.Save();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear mapped bubbles##clearbubblenpc"))
                    {
                        foreach (NpcMapData npcMapData in this.Configuration.MappedNpcs.FindAll(p => p.name.StartsWith("BB") && !p.doNotDelete))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, npcMapData.name);
                            this.Configuration.MappedNpcs.Remove(npcMapData);
                        }
                        UpdateDataBubbles = true;
                        this.Configuration.Save();
                    }


                    if (ImGui.Button("Reload remote mappings##reloadremote"))
                    {
                        ReloadRemoteMappings();
                    }

                    ImGui.NewLine();
                    ImGui.TextUnformatted("Available commands:");
                    ImGui.TextUnformatted("/ekt");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles Echokraut");
                    ImGui.TextUnformatted("/ekttalk");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles dialogue voicing");
                    ImGui.TextUnformatted("/ektbtalk");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles battle dialogue voicing");
                    ImGui.TextUnformatted("/ektbubble");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles bubble voicing");
                    ImGui.TextUnformatted("/ektchat");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles chat voicing");
                    ImGui.TextUnformatted("/ektcutschoice");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles cutscene choice voicing");
                    ImGui.TextUnformatted("/ektchoice");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Toggles choice voicing");
                    ImGui.TextUnformatted("/ek");
                    ImGui.SameLine();
                    ImGui.TextUnformatted("Opens the configuration window");
                }

                using (var disabled = ImRaii.Disabled(!Configuration.Enabled))
                {
                    if (ImGui.BeginTabItem("Dialogue"))
                    {
                        DrawDialogueSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Battle dialogue"))
                    {
                        DrawBattleDialogueSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Chat"))
                    {
                        DrawChatSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Bubbles"))
                    {
                        DrawBubbleSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Save/Load"))
                    {
                        DrawSaveSettings();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Backend"))
                    {
                        DrawBackendSettings();
                        ImGui.EndTabItem();
                    }
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawGeneralSettings()
    {
        var enabled = this.Configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            this.Configuration.Enabled = enabled;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!enabled))
        {
            var removeStutters = this.Configuration.RemoveStutters;
            if (ImGui.Checkbox("Remove stutters", ref removeStutters))
            {
                this.Configuration.RemoveStutters = removeStutters;
                this.Configuration.Save();
            }


            var hideUiInCutscenes = this.Configuration.HideUiInCutscenes;
            if (ImGui.Checkbox("Hide UI in Cutscenes", ref hideUiInCutscenes))
            {
                this.Configuration.HideUiInCutscenes = hideUiInCutscenes;
                this.Configuration.Save();
                pluginInterface.UiBuilder.DisableCutsceneUiHide = !hideUiInCutscenes;
            }
        }
    }

    private void DrawDialogueSettings()
    {
        var voiceDialog = this.Configuration.VoiceDialogue;
        if (ImGui.Checkbox("Voice dialog", ref voiceDialog))
        {
            this.Configuration.VoiceDialogue = voiceDialog;
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
        if (ImGui.Checkbox("Click dialogue window after speech completion", ref autoAdvanceOnSpeechCompletion))
        {
            this.Configuration.AutoAdvanceTextAfterSpeechCompleted = autoAdvanceOnSpeechCompletion;
            this.Configuration.Save();
        }
    }

    private void DrawBattleDialogueSettings()
    {
        var voiceBattleDialog = this.Configuration.VoiceBattleDialogue;
        if (ImGui.Checkbox("Voice battle dialog", ref voiceBattleDialog))
        {
            this.Configuration.VoiceBattleDialogue = voiceBattleDialog;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!voiceBattleDialog))
        {
            var voiceBattleDialogQueued = this.Configuration.VoiceBattleDialogQueued;
            if (ImGui.Checkbox("Voice battle dialog in a queue", ref voiceBattleDialogQueued))
            {
                this.Configuration.VoiceBattleDialogQueued = voiceBattleDialogQueued;
                this.Configuration.Save();
            }
        }
    }

    private void DrawBackendSettings()
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

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}", new EKEventId(0, TextSource.None));
        }

        if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
        {
            if (ImGui.InputText($"Base Url##EKBaseUrl", ref this.Configuration.Alltalk.BaseUrl, 40))
                this.Configuration.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Test Connection##EKTestConnection"))
            {
                BackendCheckReady(new EKEventId(0, TextSource.None));
            }

            if (ImGui.InputText($"Model to reload##EKBaseUrl", ref this.Configuration.Alltalk.ReloadModel, 40))
                this.Configuration.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Restart Service##EKRestartService"))
            {
                BackendReloadService(this.Configuration.Alltalk.ReloadModel);
            }

            if (ImGui.Button($"Reload Voices##EKLoadVoices"))
            {
                BackendGetVoices();
            }

            if (!string.IsNullOrWhiteSpace(testConnectionRes))
                ImGui.TextColored(new(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
        }
    }

    private void DrawSaveSettings()
    {
        var loadLocalFirst = this.Configuration.LoadFromLocalFirst;
        if (ImGui.Checkbox("Search audio locally first before generating", ref loadLocalFirst))
        {
            this.Configuration.LoadFromLocalFirst = loadLocalFirst;
            this.Configuration.Save();
        }
        var saveLocally = this.Configuration.SaveToLocal;
        if (ImGui.Checkbox("Save generated audio locally", ref saveLocally))
        {
            this.Configuration.SaveToLocal = saveLocally;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!saveLocally))
        {
            var createMissingLocalSave = this.Configuration.CreateMissingLocalSaveLocation;
            if (ImGui.Checkbox("Create directory if not existing", ref createMissingLocalSave))
            {
                this.Configuration.CreateMissingLocalSaveLocation = createMissingLocalSave;
                this.Configuration.Save();
            }
        }

        using (var disabled = ImRaii.Disabled(!saveLocally && !loadLocalFirst))
        {
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

                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {startDir}", new EKEventId(0, TextSource.None));
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
    }

    private unsafe void DrawBubbleSettings()
    {
        var voiceBubbles = this.Configuration.VoiceBubble;
        if (ImGui.Checkbox("Voice NPC Bubbles", ref voiceBubbles))
        {
            this.Configuration.VoiceBubble = voiceBubbles;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!voiceBubbles))
        {
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

            var voiceBubbleAudibleRange = this.Configuration.VoiceBubbleAudibleRange;
            if (ImGui.SliderFloat("3D Space audible range (shared with chat)", ref voiceBubbleAudibleRange, 0f, 2f))
            {
                this.Configuration.VoiceBubbleAudibleRange = voiceBubbleAudibleRange;
                this.Configuration.Save();

                echokraut.addonBubbleHelper.Update3DFactors(voiceBubbleAudibleRange);
            }


            if (camera == null && CameraManager.Instance() != null)
                camera = CameraManager.Instance()->GetActiveCamera();

            localPlayer = clientState.LocalPlayer!;

            var position = new Vector3();
            if (Configuration.VoiceSourceCam)
                position = camera->CameraBase.SceneCamera.Position;
            else
                position = localPlayer.Position;

            if (ImGui.CollapsingHeader("3D space debug info##3DSpaceDebug"))
            {
                ImGui.TextUnformatted($"Pos-X: {position.X}");
                ImGui.TextUnformatted($"Pos-Y: {position.Y}");
                ImGui.TextUnformatted($"Pos-Z: {position.Z}");

                if (camera != null)
                {
                    var matrix = camera->CameraBase.SceneCamera.ViewMatrix;
                    ImGui.TextUnformatted($"Rot-X: {matrix[2]}");
                    ImGui.TextUnformatted($"Rot-Y: {matrix[1]}");
                    ImGui.TextUnformatted($"Rot-Z: {matrix[0]}");
                }
            }
        }
    }

    private void DrawChatSettings()
    {
        var voiceChat = this.Configuration.VoiceChat;
        if (ImGui.Checkbox("Voice Chat", ref voiceChat))
        {
            this.Configuration.VoiceChat = voiceChat;
            this.Configuration.Save();
        }

        using (var disabled = ImRaii.Disabled(!voiceChat))
        {
            var voiceChatPlayer = this.Configuration.VoiceChatPlayer;
            if (ImGui.Checkbox("Voice your own Chat", ref voiceChatPlayer))
            {
                this.Configuration.VoiceChatPlayer = voiceChatPlayer;
                this.Configuration.Save();
            }

            var voiceChatSay = this.Configuration.VoiceChatSay;
            if (ImGui.Checkbox("Voice say Chat", ref voiceChatSay))
            {
                this.Configuration.VoiceChatSay = voiceChatSay;
                this.Configuration.Save();
            }

            var voiceChatYell = this.Configuration.VoiceChatYell;
            if (ImGui.Checkbox("Voice yell Chat", ref voiceChatYell))
            {
                this.Configuration.VoiceChatYell = voiceChatYell;
                this.Configuration.Save();
            }

            var voiceChatShout = this.Configuration.VoiceChatShout;
            if (ImGui.Checkbox("Voice shout Chat", ref voiceChatShout))
            {
                this.Configuration.VoiceChatShout = voiceChatShout;
                this.Configuration.Save();
            }

            var voiceChatFreeCompany = this.Configuration.VoiceChatFreeCompany;
            if (ImGui.Checkbox("Voice free company Chat", ref voiceChatFreeCompany))
            {
                this.Configuration.VoiceChatFreeCompany = voiceChatFreeCompany;
                this.Configuration.Save();
            }

            var voiceChatTell = this.Configuration.VoiceChatTell;
            if (ImGui.Checkbox("Voice tell Chat", ref voiceChatTell))
            {
                this.Configuration.VoiceChatTell = voiceChatTell;
                this.Configuration.Save();
            }

            var voiceChatParty = this.Configuration.VoiceChatParty;
            if (ImGui.Checkbox("Voice party Chat", ref voiceChatParty))
            {
                this.Configuration.VoiceChatParty = voiceChatParty;
                this.Configuration.Save();
            }

            var voiceChatAlliance = this.Configuration.VoiceChatAlliance;
            if (ImGui.Checkbox("Voice alliance Chat", ref voiceChatAlliance))
            {
                this.Configuration.VoiceChatAlliance = voiceChatAlliance;
                this.Configuration.Save();
            }

            var voiceChatNoviceNetwork = this.Configuration.VoiceChatNoviceNetwork;
            if (ImGui.Checkbox("Voice novice network Chat", ref voiceChatNoviceNetwork))
            {
                this.Configuration.VoiceChatNoviceNetwork = voiceChatNoviceNetwork;
                this.Configuration.Save();
            }

            var voiceChatLinkshell = this.Configuration.VoiceChatLinkshell;
            if (ImGui.Checkbox("Voice Linkshells", ref voiceChatLinkshell))
            {
                this.Configuration.VoiceChatLinkshell = voiceChatLinkshell;
                this.Configuration.Save();
            }

            var voiceChatCrossLinkshell = this.Configuration.VoiceChatCrossLinkshell;
            if (ImGui.Checkbox("Voice Cross Linkshells", ref voiceChatCrossLinkshell))
            {
                this.Configuration.VoiceChatCrossLinkshell = voiceChatCrossLinkshell;
                this.Configuration.Save();
            }

            var voiceBubbleAudibleRange = this.Configuration.VoiceBubbleAudibleRange;
            if (ImGui.SliderFloat("3D Space audible range (shared with chat)", ref voiceBubbleAudibleRange, 0f, 2f))
            {
                this.Configuration.VoiceBubbleAudibleRange = voiceBubbleAudibleRange;
                this.Configuration.Save();

                echokraut.addonBubbleHelper.Update3DFactors(voiceBubbleAudibleRange);
            }
        }
    }
    #endregion

    #region Voice selection
    private void DrawVoiceSelection()
    {
        try
        {
            if (ImGui.BeginTabBar($"Voices##EKVoicesTab"))
            {
                DrawNpcTab();

                DrawPlayerTab();

                DrawBubblesTab();

                DrawVoicesTab();

                ImGui.EndTabBar();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawNpcTab()
    {
        if (ImGui.BeginTabItem("NPCs"))
        {
            if (filteredNpcs == null)
            {
                UpdateDataNpcs = true;
            }

            if (UpdateDataNpcs || (resetDataNpcs && (filterGenderNpcs.Length == 0 || filterRaceNpcs.Length == 0 || filterNameNpcs.Length == 0)))
            {
                filteredNpcs = Configuration.MappedNpcs.FindAll(p => !p.name.StartsWith("BB"));
                UpdateDataNpcs = true;
                resetDataNpcs = false;
            }

            if (ImGui.BeginChild("NpcsChild"))
            {
                if (ImGui.BeginTable("NPC Table##NPCTable", 8, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                    ImGui.TableSetupColumn("Lock", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.None, 250);
                    ImGui.TableSetupColumn("Mute", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("##npcsaves", ImGuiTableColumnFlags.None, 25f);
                    ImGui.TableSetupColumn("##npcmapping", ImGuiTableColumnFlags.None, 25f);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcGender", ref filterGenderNpcs, 40) || (filterGenderNpcs.Length > 0 && UpdateDataNpcs))
                    {
                        filteredNpcs = filteredNpcs.FindAll(p => !p.name.StartsWith("BB") && p.gender.ToString().ToLower().StartsWith(filterGenderNpcs.ToLower()));
                        UpdateDataNpcs = true;
                        resetDataNpcs = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcRace", ref filterRaceNpcs, 40) || (filterRaceNpcs.Length > 0 && UpdateDataNpcs))
                    {
                        filteredNpcs = filteredNpcs.FindAll(p => !p.name.StartsWith("BB") && p.race.ToString().ToLower().Contains(filterRaceNpcs.ToLower()));
                        UpdateDataNpcs = true;
                        resetDataNpcs = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcName", ref filterNameNpcs, 40) || (filterNameNpcs.Length > 0 && UpdateDataNpcs))
                    {
                        filteredNpcs = filteredNpcs.FindAll(p => !p.name.StartsWith("BB") && p.name.ToLower().Contains(filterNameNpcs.ToLower()));
                        UpdateDataNpcs = true;
                        resetDataNpcs = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcVoice", ref filterVoiceNpcs, 40) || (filterVoiceNpcs.Length > 0 && UpdateDataNpcs))
                    {
                        filteredNpcs = filteredNpcs.FindAll(p => !p.name.StartsWith("BB") && p.voiceItem != null && p.voiceItem.ToString().ToLower().Contains(filterVoiceNpcs.ToLower()));
                        UpdateDataNpcs = true;
                        resetDataNpcs = true;
                    }
                    ImGui.TableNextRow();
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsDirty || UpdateDataNpcs)
                    {
                        switch (sortSpecs.Specs.ColumnIndex)
                        {
                            case 0:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredNpcs.Sort((a, b) => string.Compare(b.doNotDelete.ToString(), a.doNotDelete.ToString()));
                                else
                                    filteredNpcs.Sort((a, b) => string.Compare(a.doNotDelete.ToString(), b.doNotDelete.ToString()));
                                break;
                            case 1:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredNpcs.Sort((a, b) => string.Compare(a.gender.ToString(), b.gender.ToString()));
                                else
                                    filteredNpcs.Sort((a, b) => string.Compare(b.gender.ToString(), a.gender.ToString()));
                                break;
                            case 2:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredNpcs.Sort((a, b) => string.Compare(a.race.ToString(), b.race.ToString()));
                                else
                                    filteredNpcs.Sort((a, b) => string.Compare(b.race.ToString(), a.race.ToString()));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredNpcs.Sort((a, b) => string.Compare(a.name, b.name));
                                else
                                    filteredNpcs.Sort((a, b) => string.Compare(b.name, a.name));
                                break;
                            case 4:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredNpcs.Sort((a, b) => string.Compare(a.voiceItem?.ToString(), b.voiceItem?.ToString()));
                                else
                                    filteredNpcs.Sort((a, b) => string.Compare(b.voiceItem?.ToString(), a.voiceItem?.ToString()));
                                break;
                            case 5:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredNpcs.Sort((a, b) => string.Compare(b.mutedBubble.ToString(), a.mutedBubble.ToString()));
                                else
                                    filteredNpcs.Sort((a, b) => string.Compare(a.mutedBubble.ToString(), b.mutedBubble.ToString()));
                                break;
                        }

                        UpdateDataNpcs = false;
                        sortSpecs.SpecsDirty = false;
                    }
                    ImGui.TableNextRow();

                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredNpcs)
                    {
                        ImGui.TableNextColumn();
                        var doNotDelete = mapData.doNotDelete;
                        if (ImGui.Checkbox($"##EKNpcDoNotDelete{mapData.ToString()}", ref doNotDelete))
                        {
                            mapData.doNotDelete = doNotDelete;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexGender = BackendVoiceHelper.GenderDisplay.IndexOf(p => p.Contains(mapData.gender.ToString()));
                        if (ImGui.Combo($"##EKCBoxNPC{mapData.ToString()}1", ref presetIndexGender, BackendVoiceHelper.GenderDisplay, BackendVoiceHelper.GenderDisplay.Length))
                        {
                            var newGender = BackendVoiceHelper.GenderArr[presetIndexGender];
                            if (newGender != mapData.gender)
                            {
                                if (Configuration.MappedNpcs.Contains(new NpcMapData(mapData.objectKind) { gender = newGender, race = mapData.race, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Gender for Character: {mapData.ToString()} from: {mapData.gender} to: {newGender}", new EKEventId(0, TextSource.None));

                                    mapData.gender = newGender;
                                    UpdateDataNpcs = true;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Gender for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexRace = BackendVoiceHelper.RaceDisplay.IndexOf(p => p.Contains(mapData.race.ToString()));
                        if (ImGui.Combo($"##EKCBoxNPC{mapData.ToString()}2", ref presetIndexRace, BackendVoiceHelper.RaceDisplay, BackendVoiceHelper.RaceDisplay.Length))
                        {
                            var newRace = BackendVoiceHelper.RaceArr[presetIndexRace];
                            if (newRace != mapData.race)
                            {
                                if (Configuration.MappedNpcs.Contains(new NpcMapData(mapData.objectKind) { gender = mapData.gender, race = newRace, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Race for Character: {mapData.ToString()} from: {mapData.race} to: {newRace}", new EKEventId(0, TextSource.None));

                                    mapData.race = newRace;
                                    UpdateDataNpcs = true;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(mapData.name);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                        if (mapData.voicesSelectable.Draw(mapData.voiceItem?.ToString() ?? "", out var selectedIndexVoice))
                        {
                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Selected: {selectedIndexVoice}", new EKEventId(0, TextSource.None));
                            var newVoiceItem = BackendVoiceHelper.Voices[selectedIndexVoice];

                            if (newVoiceItem != null)
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString()} from: {mapData.voiceItem} to: {newVoiceItem}", new EKEventId(0, TextSource.None));

                                mapData.voiceItem = newVoiceItem;
                                UpdateDataNpcs = true;
                                this.Configuration.Save();
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        var muted = mapData.muted;
                        if (ImGui.Checkbox($"##EKNpcMute{mapData.ToString()}", ref muted))
                        {
                            mapData.muted = muted;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.TrashAlt.ToIconString()}##delnpcsaves{mapData.ToString()}", new Vector2(25, 25), "Remove local saved files.\r\nWill also clear bubble data.", false, true))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, mapData.name);
                        }
                        ImGui.TableNextColumn();
                        if (!mapData.doNotDelete)
                        {
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.SquareXmark.ToIconString()}##delnpc{mapData.ToString()}", new Vector2(25, 25), "Remove npc mapping and local saved files.\r\nWill also clear bubble data.", false, true))
                            {
                                toBeRemoved = mapData;
                            }
                        }

                        ImGui.TableNextRow();
                    }

                    if (toBeRemoved != null)
                    {
                        FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, toBeRemoved.name);
                        Configuration.MappedNpcs.Remove(toBeRemoved);
                        UpdateDataNpcs = true;
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawPlayerTab()
    {
        if (ImGui.BeginTabItem("Players"))
        {
            if (filteredPlayers == null)
            {
                UpdateDataPlayers = true;
            }

            if (UpdateDataPlayers || (resetDataPlayers && (filterGenderPlayers.Length == 0 || filterRacePlayers.Length == 0 || filterNamePlayers.Length == 0)))
            {
                filteredPlayers = Configuration.MappedPlayers;
                UpdateDataPlayers = true;
                resetDataPlayers = false;
            }

            if (ImGui.BeginChild("PlayerssChild"))
            {
                if (ImGui.BeginTable("Player Table##PlayerTable", 8, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                    ImGui.TableSetupColumn("Lock", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.None, 250);
                    ImGui.TableSetupColumn("Mute", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("##playersaves", ImGuiTableColumnFlags.None, 25f);
                    ImGui.TableSetupColumn("##playermapping", ImGuiTableColumnFlags.None, 25f);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcGender", ref filterGenderPlayers, 40) || (filterGenderPlayers.Length > 0 && UpdateDataPlayers))
                    {
                        filteredPlayers = filteredPlayers.FindAll(p => p.gender.ToString().ToLower().StartsWith(filterGenderPlayers.ToLower()));
                        UpdateDataPlayers = true;
                        resetDataPlayers = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcRace", ref filterRacePlayers, 40) || (filterRacePlayers.Length > 0 && UpdateDataPlayers))
                    {
                        filteredPlayers = filteredPlayers.FindAll(p => p.race.ToString().ToLower().Contains(filterRacePlayers.ToLower()));
                        UpdateDataPlayers = true;
                        resetDataPlayers = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcName", ref filterNamePlayers, 40) || (filterNamePlayers.Length > 0 && UpdateDataPlayers))
                    {
                        filteredPlayers = filteredPlayers.FindAll(p => p.name.ToLower().Contains(filterNamePlayers.ToLower()));
                        UpdateDataPlayers = true;
                        resetDataPlayers = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcVoice", ref filterVoicePlayers, 40) || (filterVoicePlayers.Length > 0 && UpdateDataPlayers))
                    {
                        filteredPlayers = filteredPlayers.FindAll(p => p.voiceItem != null && p.voiceItem.ToString().ToLower().Contains(filterVoicePlayers.ToLower()));
                        UpdateDataPlayers = true;
                        resetDataPlayers = true;
                    }
                    ImGui.TableNextRow();
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsDirty || UpdateDataPlayers)
                    {
                        switch (sortSpecs.Specs.ColumnIndex)
                        {
                            case 0:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredPlayers.Sort((a, b) => string.Compare(b.doNotDelete.ToString(), a.doNotDelete.ToString()));
                                else
                                    filteredPlayers.Sort((a, b) => string.Compare(a.doNotDelete.ToString(), b.doNotDelete.ToString()));
                                break;
                            case 1:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredPlayers.Sort((a, b) => string.Compare(a.gender.ToString(), b.gender.ToString()));
                                else
                                    filteredPlayers.Sort((a, b) => string.Compare(b.gender.ToString(), a.gender.ToString()));
                                break;
                            case 2:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredPlayers.Sort((a, b) => string.Compare(a.race.ToString(), b.race.ToString()));
                                else
                                    filteredPlayers.Sort((a, b) => string.Compare(b.race.ToString(), a.race.ToString()));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredPlayers.Sort((a, b) => string.Compare(a.name, b.name));
                                else
                                    filteredPlayers.Sort((a, b) => string.Compare(b.name, a.name));
                                break;
                            case 4:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredPlayers.Sort((a, b) => string.Compare(a.voiceItem?.ToString(), b.voiceItem?.ToString()));
                                else
                                    filteredPlayers.Sort((a, b) => string.Compare(b.voiceItem?.ToString(), a.voiceItem?.ToString()));
                                break;
                            case 5:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredPlayers.Sort((a, b) => string.Compare(b.mutedBubble.ToString(), a.mutedBubble.ToString()));
                                else
                                    filteredPlayers.Sort((a, b) => string.Compare(a.mutedBubble.ToString(), b.mutedBubble.ToString()));
                                break;
                        }

                        sortSpecs.SpecsDirty = false;
                        UpdateDataPlayers = false;
                    }
                    ImGui.TableNextRow();

                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredPlayers)
                    {
                        ImGui.TableNextColumn();
                        var doNotDelete = mapData.doNotDelete;
                        if (ImGui.Checkbox($"##EKNpcDoNotDelete{mapData.ToString()}", ref doNotDelete))
                        {
                            mapData.doNotDelete = doNotDelete;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexGender = BackendVoiceHelper.GenderDisplay.IndexOf(p => p.Contains(mapData.gender.ToString()));
                        if (ImGui.Combo($"##EKCBoxPlayer{mapData.ToString()}1", ref presetIndexGender, BackendVoiceHelper.GenderDisplay, BackendVoiceHelper.GenderDisplay.Length))
                        {
                            var newGender = BackendVoiceHelper.GenderArr[presetIndexGender];
                            if (newGender != mapData.gender)
                            {
                                if (Configuration.MappedPlayers.Contains(new NpcMapData(mapData.objectKind) { gender = newGender, race = mapData.race, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Gender for Character: {mapData.ToString()} from: {mapData.gender} to: {newGender}", new EKEventId(0, TextSource.None));

                                    mapData.gender = newGender;
                                    UpdateDataPlayers = true;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Gender for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexRace = BackendVoiceHelper.RaceDisplay.IndexOf(p => p.Contains(mapData.race.ToString()));
                        if (ImGui.Combo($"##EKCBoxPlayer{mapData.ToString()}2", ref presetIndexRace, BackendVoiceHelper.RaceDisplay, BackendVoiceHelper.RaceDisplay.Length))
                        {
                            var newRace = BackendVoiceHelper.RaceArr[presetIndexRace];
                            if (newRace != mapData.race)
                            {
                                if (Configuration.MappedPlayers.Contains(new NpcMapData(mapData.objectKind) { gender = mapData.gender, race = newRace, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Race for Character: {mapData.ToString()} from: {mapData.race} to: {newRace}", new EKEventId(0, TextSource.None));

                                    mapData.race = newRace;
                                    UpdateDataPlayers = true;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(mapData.name);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                        if (mapData.voicesSelectable.Draw(mapData.voiceItem?.ToString() ?? "", out var selectedIndexVoice))
                        {
                            var newVoiceItem = BackendVoiceHelper.Voices[selectedIndexVoice];

                            if (newVoiceItem != null)
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString()} from: {mapData.voiceItem} to: {newVoiceItem}", new EKEventId(0, TextSource.None));

                                mapData.voiceItem = newVoiceItem;
                                UpdateDataPlayers = true;
                                this.Configuration.Save();
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        var muted = mapData.muted;
                        if (ImGui.Checkbox($"##EKPlayerMute{mapData.ToString()}", ref muted))
                        {
                            mapData.muted = muted;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.TrashAlt.ToIconString()}##delplayersaves{mapData.ToString()}", new Vector2(25, 25), "Remove local saved files", false, true))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, mapData.name);
                        }
                        ImGui.TableNextColumn();
                        if (!mapData.doNotDelete)
                        {
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.SquareXmark.ToIconString()}##delplayer{mapData.ToString()}", new Vector2(25, 25), "Remove player mapping and local saved files", false, true))
                            {
                                toBeRemoved = mapData;
                            }
                        }

                        ImGui.TableNextRow();
                    }

                    if (toBeRemoved != null)
                    {
                        FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, toBeRemoved.name);
                        Configuration.MappedPlayers.Remove(toBeRemoved);
                        UpdateDataPlayers = true;
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawBubblesTab()
    {
        if (ImGui.BeginTabItem("Bubbles"))
        {
            if (filteredBubbles == null)
            {
                UpdateDataBubbles = true;
            }

            if (UpdateDataBubbles || (resetDataBubbles && (filterGenderBubbles.Length == 0 || filterRaceBubbles.Length == 0 || filterNameBubbles.Length == 0)))
            {
                filteredBubbles = Configuration.MappedNpcs.FindAll(p => p.hasBubbles);
                UpdateDataBubbles = true;
                resetDataBubbles = false;
            }

            if (ImGui.BeginChild("BubblesChild"))
            {
                if (ImGui.BeginTable("Bubble Table##BubbleTable", 8, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                    ImGui.TableSetupColumn("Lock", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.None, 250);
                    ImGui.TableSetupColumn("Mute", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("##bubblesaves", ImGuiTableColumnFlags.None, 25f);
                    ImGui.TableSetupColumn("##bubblemapping", ImGuiTableColumnFlags.None, 25f);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcGender", ref filterGenderBubbles, 40) || (filterGenderBubbles.Length > 0 && UpdateDataBubbles))
                    {
                        filteredBubbles = filteredBubbles.FindAll(p => p.gender.ToString().ToLower().StartsWith(filterGenderBubbles.ToLower()));
                        UpdateDataBubbles = true;
                        resetDataBubbles = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcRace", ref filterRaceBubbles, 40) || (filterRaceBubbles.Length > 0 && UpdateDataBubbles))
                    {
                        filteredBubbles = filteredBubbles.FindAll(p => p.race.ToString().ToLower().Contains(filterRaceBubbles.ToLower()));
                        UpdateDataBubbles = true;
                        resetDataBubbles = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcName", ref filterNameBubbles, 40) || (filterNameBubbles.Length > 0 && UpdateDataBubbles))
                    {
                        filteredBubbles = filteredBubbles.FindAll(p => p.name.ToLower().Contains(filterNameBubbles.ToLower()));
                        UpdateDataBubbles = true;
                        resetDataBubbles = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcVoice", ref filterVoiceBubbles, 40) || (filterVoiceBubbles.Length > 0 && UpdateDataBubbles))
                    {
                        filteredBubbles = filteredBubbles.FindAll(p => p.voiceItem != null && p.voiceItem.ToString().ToLower().Contains(filterVoiceBubbles.ToLower()));
                        UpdateDataBubbles = true;
                        resetDataBubbles = true;
                    }
                    ImGui.TableNextRow();
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsDirty || UpdateDataBubbles)
                    {
                        switch (sortSpecs.Specs.ColumnIndex)
                        {
                            case 0:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredBubbles.Sort((a, b) => string.Compare(b.doNotDelete.ToString(), a.doNotDelete.ToString()));
                                else
                                    filteredBubbles.Sort((a, b) => string.Compare(a.doNotDelete.ToString(), b.doNotDelete.ToString()));
                                break;
                            case 1:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredBubbles.Sort((a, b) => string.Compare(a.gender.ToString(), b.gender.ToString()));
                                else
                                    filteredBubbles.Sort((a, b) => string.Compare(b.gender.ToString(), a.gender.ToString()));
                                break;
                            case 2:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredBubbles.Sort((a, b) => string.Compare(a.race.ToString(), b.race.ToString()));
                                else
                                    filteredBubbles.Sort((a, b) => string.Compare(b.race.ToString(), a.race.ToString()));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredBubbles.Sort((a, b) => string.Compare(a.name, b.name));
                                else
                                    filteredBubbles.Sort((a, b) => string.Compare(b.name, a.name));
                                break;
                            case 4:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredBubbles.Sort((a, b) => string.Compare(a.voiceItem?.ToString(), b.voiceItem?.ToString()));
                                else
                                    filteredBubbles.Sort((a, b) => string.Compare(b.voiceItem?.ToString(), a.voiceItem?.ToString()));
                                break;
                            case 5:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredBubbles.Sort((a, b) => string.Compare(b.mutedBubble.ToString(), a.mutedBubble.ToString()));
                                else
                                    filteredBubbles.Sort((a, b) => string.Compare(a.mutedBubble.ToString(), b.mutedBubble.ToString()));
                                break;
                        }

                        UpdateDataBubbles = false;
                        sortSpecs.SpecsDirty = false;
                    }

                    NpcMapData toBeRemoved = null;
                    foreach (NpcMapData mapData in filteredBubbles)
                    {
                        ImGui.TableNextColumn();
                        var doNotDelete = mapData.doNotDelete;
                        if (ImGui.Checkbox($"##EKNpcDoNotDelete{mapData.ToString()}", ref doNotDelete))
                        {
                            mapData.doNotDelete = doNotDelete;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexGender = BackendVoiceHelper.GenderDisplay.IndexOf(p => p.Contains(mapData.gender.ToString()));
                        if (ImGui.Combo($"##EKCBoxNPC{mapData.ToString()}1", ref presetIndexGender, BackendVoiceHelper.GenderDisplay, BackendVoiceHelper.GenderDisplay.Length))
                        {
                            var newGender = BackendVoiceHelper.GenderArr[presetIndexGender];
                            if (newGender != mapData.gender)
                            {
                                if (Configuration.MappedNpcs.Contains(new NpcMapData(mapData.objectKind) { gender = newGender, race = mapData.race, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Gender for Character: {mapData.ToString()} from: {mapData.gender} to: {newGender}", new EKEventId(0, TextSource.None));

                                    mapData.gender = newGender;
                                    UpdateDataBubbles = true;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Gender for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var presetIndexRace = BackendVoiceHelper.RaceDisplay.IndexOf(p => p.Contains(mapData.race.ToString()));
                        if (ImGui.Combo($"##EKCBoxBubble{mapData.ToString()}2", ref presetIndexRace, BackendVoiceHelper.RaceDisplay, BackendVoiceHelper.RaceDisplay.Length))
                        {
                            var newRace = BackendVoiceHelper.RaceArr[presetIndexRace];
                            if (newRace != mapData.race)
                            {
                                if (Configuration.MappedNpcs.Contains(new NpcMapData(mapData.objectKind) { gender = mapData.gender, race = newRace, name = mapData.name }))
                                    toBeRemoved = mapData;
                                else
                                {
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Race for Character: {mapData.ToString()} from: {mapData.race} to: {newRace}", new EKEventId(0, TextSource.None));

                                    mapData.race = newRace;
                                    UpdateDataBubbles = true;
                                    this.Configuration.Save();
                                }
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(mapData.name);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                        if (mapData.voicesSelectable.Draw(mapData.voiceItem?.ToString() ?? "", out var selectedIndexVoice))
                        {
                            var newVoiceItem = BackendVoiceHelper.Voices[selectedIndexVoice];

                            if (newVoiceItem != null)
                            {
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for Character: {mapData.ToString()} from: {mapData.voiceItem} to: {newVoiceItem}", new EKEventId(0, TextSource.None));

                                mapData.voiceItem = newVoiceItem;
                                UpdateDataBubbles = true;
                                this.Configuration.Save();
                            }
                            else
                                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for Character: {mapData}", new EKEventId(0, TextSource.None));
                        }
                        ImGui.TableNextColumn();
                        var mutedBubble = mapData.mutedBubble;
                        if (ImGui.Checkbox($"##EKBubbleMute{mapData.ToString()}", ref mutedBubble))
                        {
                            mapData.mutedBubble = mutedBubble;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.TrashAlt.ToIconString()}##delbubblesaves{mapData.ToString()}", new Vector2(25, 25), "Remove local saved files.\r\nWill also clear normal npc data.", false, true))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, mapData.name);
                        }
                        ImGui.TableNextColumn();
                        if (!mapData.doNotDelete)
                        {
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.SquareXmark.ToIconString()}##delbubble{mapData.ToString()}", new Vector2(25, 25), "Remove bubble mapping and local saved files.\r\nWill also clear normal npc data.", false, true))
                            {
                                toBeRemoved = mapData;
                            }
                        }

                        ImGui.TableNextRow();
                    }

                    if (toBeRemoved != null)
                    {
                        FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, toBeRemoved.name);
                        Configuration.MappedNpcs.Remove(toBeRemoved);
                        UpdateDataBubbles = true;
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawVoicesTab()
    {
        if (ImGui.BeginTabItem("Voices"))
        {
            if (filteredVoices == null)
            {
                UpdateDataVoices = true;
            }

            if (UpdateDataVoices || (resetDataVoices && (filterGenderVoices.Length == 0 || filterRaceVoices.Length == 0 || filterNameVoices.Length == 0)))
            {
                filteredVoices = BackendVoiceHelper.Voices.FindAll(p => !(p.race == NpcRaces.Unknown && p.voiceName.Contains("NPC")));
                UpdateDataVoices = true;
                resetDataVoices = false;
            }

            if (ImGui.BeginChild("VoicesChild"))
            {
                if (ImGui.BeginTable("Voice Table##VoiceTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                    ImGui.TableSetupColumn("Test", ImGuiTableColumnFlags.None, 70f);
                    ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.None, 125);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcGender", ref filterGenderVoices, 40) || (filterGenderVoices.Length > 0 && UpdateDataVoices))
                    {
                        filteredVoices = filteredVoices.FindAll(p => p.gender.ToString().ToLower().StartsWith(filterGenderVoices.ToLower()));
                        UpdateDataVoices = true;
                        resetDataVoices = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcRace", ref filterRaceVoices, 40) || (filterRaceVoices.Length > 0 && UpdateDataVoices))
                    {
                        filteredVoices = filteredVoices.FindAll(p => p.race.ToString().ToLower().Contains(filterRaceVoices.ToLower()));
                        UpdateDataVoices = true;
                        resetDataVoices = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcName", ref filterNameVoices, 40) || (filterNameVoices.Length > 0 && UpdateDataVoices))
                    {
                        filteredVoices = filteredVoices.FindAll(p => p.voiceName.ToLower().Contains(filterNameVoices.ToLower()));
                        UpdateDataVoices = true;
                        resetDataVoices = true;
                    }
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsDirty)
                    {
                        switch (sortSpecs.Specs.ColumnIndex)
                        {
                            case 1:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.Compare(a.gender.ToString(), b.gender.ToString()));
                                else
                                    filteredVoices.Sort((a, b) => string.Compare(b.gender.ToString(), a.gender.ToString()));
                                break;
                            case 2:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.Compare(a.race.ToString(), b.race.ToString()));
                                else
                                    filteredVoices.Sort((a, b) => string.Compare(b.race.ToString(), a.race.ToString()));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.Compare(a.voiceName, b.voiceName));
                                else
                                    filteredVoices.Sort((a, b) => string.Compare(b.voiceName, a.voiceName));
                                break;
                        }

                        sortSpecs.SpecsDirty = false;
                    }
                    ImGui.TableNextRow();

                    foreach (var voice in filteredVoices)
                    {
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.ThumbsUp.ToIconString()}##testvoice{voice.ToString()}", new Vector2(25, 25), "Test Voice", false, true))
                        {
                            BackendTestVoice(voice);
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.gender.ToString());
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.race.ToString());
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.voiceName);
                        ImGui.TableNextRow();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }
    #endregion

    #region Phonetic corrections
    private void DrawPhoneticCorrections()
    {
        try
        {
            if (Configuration.PhoneticCorrections.Count == 0)
            {
                Configuration.PhoneticCorrections.Add(new PhoneticCorrection("C'mi", "Kami"));
                Configuration.PhoneticCorrections.Add(new PhoneticCorrection("/", "Schrägstrich"));
                Configuration.PhoneticCorrections.Sort();
                Configuration.Save();
            }


            if (ImGui.BeginChild("NpcsChild"))
            {
                if (ImGui.BeginTable("NPC Table##NPCTable", 5, ImGuiTableFlags.Sortable | ImGuiTableFlags.NoKeepColumnsVisible))
                {
                    ImGui.TableSetupScrollFreeze(0, 1); // Make top row always visible
                    ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.None, 35f);
                    ImGui.TableSetupColumn("Original", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableSetupColumn("Corrected", ImGuiTableColumnFlags.None, 150);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextRow();
                    PhoneticCorrection toBeRemoved = null;
                    foreach (PhoneticCorrection phoneticCorrection in Configuration.PhoneticCorrections)
                    {
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delphoncorr{phoneticCorrection.ToString()}", new Vector2(25, 25), "Remove phonetic correction", false, true))
                        {
                            toBeRemoved = phoneticCorrection;
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.InputText($"##origText{phoneticCorrection.ToString()}", ref phoneticCorrection.OriginalText, 25);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.InputText($"##correctText{phoneticCorrection.ToString()}", ref phoneticCorrection.CorrectedText, 25);

                        ImGui.TableNextRow();
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Plus.ToIconString()}##addphoncorr", new Vector2(25, 25), "Add phonetic correction", false, true))
                    {
                        if (!string.IsNullOrWhiteSpace(originalText) && !string.IsNullOrWhiteSpace(correctedText))
                        {
                            PhoneticCorrection newCorrection = new PhoneticCorrection(originalText, correctedText);
                            if (!Configuration.PhoneticCorrections.Contains(newCorrection))
                            {
                                Configuration.PhoneticCorrections.Add(newCorrection);
                                Configuration.PhoneticCorrections.Sort();
                                Configuration.Save();
                                originalText = "";
                                correctedText = "";
                            }
                        }
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.InputText("##origText", ref originalText, 25);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.InputText("##correctText", ref correctedText, 25);
                    ImGui.TableNextRow();

                    if (toBeRemoved != null)
                    {
                        Configuration.PhoneticCorrections.Remove(toBeRemoved);
                        Configuration.PhoneticCorrections.Sort();
                        Configuration.Save();
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }
    #endregion

    #region Logs
    private void DrawLogs()
    {
        try
        {
            if (ImGui.BeginTabBar($"Logs##EKLogsTab"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowGeneralInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowGeneralInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.None);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowGeneralDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowGeneralDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.None);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowGeneralErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowGeneralErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.None);
                        }
                        var jumpToBottom = this.Configuration.logConfig.GeneralJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.GeneralJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.GeneralLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.None, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.None);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.GeneralJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowTalkInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowTalkInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowTalkDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowTalkErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var showId0 = this.Configuration.logConfig.ShowTalkId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowTalkId0 = showId0;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonTalk);
                        }
                        var jumpToBottom = this.Configuration.logConfig.TalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.TalkJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.TalkLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonTalk, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonTalk);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.TalkJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Battle dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowBattleTalkInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowBattleTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowBattleTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var showId0 = this.Configuration.logConfig.ShowBattleTalkId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowBattleTalkId0 = showId0;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                        }
                        var jumpToBottom = this.Configuration.logConfig.BattleTalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.BattleTalkJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.BattleTalkLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonBattleTalk, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonBattleTalk);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.BattleTalkJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Chat"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowChatInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowChatInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowChatDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowChatDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowChatErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowChatErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var showId0 = this.Configuration.logConfig.ShowChatId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowChatId0 = showId0;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.Chat);
                        }
                        var jumpToBottom = this.Configuration.logConfig.ChatJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.ChatJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.ChatLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.Chat, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.Chat);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.ChatJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Bubbles"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowBubbleInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowBubbleInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowBubbleDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowBubbleDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowBubbleErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowBubbleErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var showId0 = this.Configuration.logConfig.ShowBubbleId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowBubbleId0 = showId0;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonBubble);
                        }
                        var jumpToBottom = this.Configuration.logConfig.BubbleJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.BubbleJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.BubbleLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonBubble, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonBubble);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.BubbleJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice in cutscenes"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowCutSceneSelectStringInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowCutSceneSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowCutSceneSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var showId0 = this.Configuration.logConfig.ShowCutSceneSelectStringId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringId0 = showId0;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                        }
                        var jumpToBottom = this.Configuration.logConfig.CutSceneSelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.CutSceneSelectStringJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.CutSceneSelectStringLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonCutSceneSelectString, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonCutSceneSelectString);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.CutSceneSelectStringJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showInfoLog = this.Configuration.logConfig.ShowSelectStringInfoLog;
                        if (ImGui.Checkbox("Show info logs", ref showInfoLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringInfoLog = showInfoLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var showDebugLog = this.Configuration.logConfig.ShowSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringDebugLog = showDebugLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringErrorLog = showErrorLog;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var showId0 = this.Configuration.logConfig.ShowSelectStringId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowSelectStringId0 = showId0;
                            this.Configuration.Save();
                            LogHelper.RecreateLogList(TextSource.AddonSelectString);
                        }
                        var jumpToBottom = this.Configuration.logConfig.SelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.SelectStringJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    if (ImGui.CollapsingHeader("Log:"))
                    {
                        List<LogMessage> logMessages = LogHelper.SelectStringLogsFiltered;

                        if (ImGui.InputText($"Filter by event-id##EKFilterNpcId", ref logFilter, 40))
                        {
                            logMessages = LogHelper.FilterLogList(TextSource.AddonSelectString, logFilter);
                            resetLogFilter = false;
                        }
                        else if (!resetLogFilter && logFilter.Length == 0)
                        {
                            logMessages = LogHelper.RecreateLogList(TextSource.AddonSelectString);
                            resetLogFilter = true;
                        }

                        if (ImGui.BeginChild("LogsChild"))
                        {
                            foreach (var logMessage in logMessages)
                            {
                                var text = $"{logMessage.timeStamp.ToShortDateString()} - {logMessage.timeStamp.ToString("HH:mm:ss.fff")}: {logMessage.message}";
                                ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                                ImGui.PushTextWrapPos();
                                ImGui.TextUnformatted(text);
                                ImGui.PopStyleColor();
                            }

                            if (Configuration.logConfig.SelectStringJumpToBottom)
                            {
                                ImGui.SetScrollHereY();
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None), false);
        }
    }
    #endregion

    #region Helper Functions
    private async void BackendCheckReady(EKEventId eventId)
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

            UpdateDataBubbles = true;
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private async void BackendReloadService(string reloadModel)
    {
        try
        {
            if (BackendHelper.ReloadService(reloadModel, new EKEventId(0, TextSource.None)))
                testConnectionRes = "Successfully started service reload. Please wait for up to 30 seconds before using.";
            else
                testConnectionRes = "Error while service reload. Please check logs.";

            LogHelper.Important(MethodBase.GetCurrentMethod().Name, testConnectionRes, new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private async void BackendTestVoice(BackendVoiceItem voice)
    {
        var eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Testing voice: {voice.ToString()}", eventId);
        // Say the thing
        var voiceMessage = new VoiceMessage
        {
            pActor = null,
            Source = TextSource.VoiceTest,
            Speaker = new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None)
            {
                gender = voice.gender,
                race = voice.race,
                name = voice.voiceName,
                voiceItem = voice
            },
            Text = Constants.TESTMESSAGEDE,
            Language = this.clientState.ClientLanguage,
            eventId = eventId
        };
        var volume = VolumeHelper.GetVoiceVolume(eventId);

        if (volume > 0)
            BackendHelper.OnSay(voiceMessage, volume);
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
        }
    }

    private void ReloadRemoteMappings()
    {
        VoiceMapHelper.Setup(this.clientState.ClientLanguage);
        NpcGenderRacesHelper.Setup();
    }
    #endregion
}
