using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using ImGuiNET;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using System.Reflection;
using System.IO;
using Dalamud.Interface.ImGuiFileDialog;
using OtterGui;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Echokraut echokraut;
    private IDalamudPluginInterface pluginInterface;
    private string testConnectionRes = "";
    private FileDialogManager fileDialogManager;
    #region Voice Selection
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
    private List<EchokrautVoice> filteredVoices;
    public static bool UpdateDataVoices = false;
    public bool resetDataVoices = false;
    private string filterGenderVoices = "";
    private string filterRaceVoices = "";
    private string filterNameVoices = "";
    private string filterVoices = "";
    #endregion
    #region Logs
    private List<LogMessage> filteredLogsGeneral;
    private string filterLogsGeneralMethod = "";
    private string filterLogsGeneralMessage = "";
    private string filterLogsGeneralId = "";
    public static bool UpdateLogGeneralFilter = true;
    private bool resetLogGeneralFilter = true;
    private List<LogMessage> filteredLogsTalk;
    private string filterLogsTalkMethod = "";
    private string filterLogsTalkMessage = "";
    private string filterLogsTalkId = "";
    public static bool UpdateLogTalkFilter = true;
    private bool resetLogTalkFilter = true;
    private List<LogMessage> filteredLogsBattleTalk;
    private string filterLogsBattleTalkMethod = "";
    private string filterLogsBattleTalkMessage = "";
    private string filterLogsBattleTalkId = "";
    public static bool UpdateLogBattleTalkFilter = true;
    private bool resetLogBattleTalkFilter = true;
    private List<LogMessage> filteredLogsBubbles;
    private string filterLogsBubblesMethod = "";
    private string filterLogsBubblesMessage = "";
    private string filterLogsBubblesId = "";
    public static bool UpdateLogBubblesFilter = true;
    private bool resetLogBubblesFilter = true;
    private List<LogMessage> filteredLogsChat;
    private string filterLogsChatMethod = "";
    private string filterLogsChatMessage = "";
    private string filterLogsChatId = "";
    public static bool UpdateLogChatFilter = true;
    private bool resetLogChatFilter = true;
    private List<LogMessage> filteredLogsCutsceneSelectString;
    private string filterLogsCutsceneSelectStringMethod = "";
    private string filterLogsCutsceneSelectStringMessage = "";
    private string filterLogsCutsceneSelectStringId = "";
    public static bool UpdateLogCutsceneSelectStringFilter = true;
    private bool resetLogCutsceneSelectStringFilter = true;
    private List<LogMessage> filteredLogsSelectString;
    private string filterLogsSelectStringMethod = "";
    private string filterLogsSelectStringMessage = "";
    private string filterLogsSelectStringId = "";
    public static bool UpdateLogSelectStringFilter = true;
    private bool resetLogSelectStringFilter = true;
    #endregion
    #region Phonetic Corrections
    private List<PhoneticCorrection> filteredPhon;
    private string filterPhonOriginal = "";
    private string filterPhonCorrected = "";
    private bool updatePhonData = true;
    private bool resetPhonFilter = true;
    private string originalText = "";
    private string correctedText = "";
    #endregion
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
                        foreach (NpcMapData npcMapData in this.Configuration.MappedNpcs.FindAll(p => !p.Name.StartsWith("BB") && !p.DoNotDelete))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, npcMapData.Name);
                            this.Configuration.MappedNpcs.Remove(npcMapData);
                        }
                        UpdateDataNpcs = true;
                        this.Configuration.Save();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear mapped players##clearplayers"))
                    {
                        foreach (NpcMapData playerMapData in this.Configuration.MappedPlayers.FindAll(p => !p.DoNotDelete))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, playerMapData.Name);
                            this.Configuration.MappedPlayers.Remove(playerMapData);
                        }
                        UpdateDataPlayers = true;
                        this.Configuration.Save();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Clear mapped bubbles##clearbubblenpc"))
                    {
                        foreach (NpcMapData npcMapData in this.Configuration.MappedNpcs.FindAll(p => p.Name.StartsWith("BB") && !p.DoNotDelete))
                        {
                            FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, npcMapData.Name);
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
                    foreach (var commandKey in CommandHelper.CommandKeys)
                    {
                        var command = CommandHelper.CommandManager.Commands[commandKey];
                        ImGui.TextUnformatted(commandKey);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(command.HelpMessage);
                    }
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
            if (ImGui.InputText($"Base Url##EKBaseUrl", ref this.Configuration.Alltalk.BaseUrl, 80))
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
            if (voiceChat)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.TextUnformatted("Detect Language is the service used for automatically detecting the language of the written chat, it's not perfect but works well. To register for free visit: ");
                ImGui.SameLine();
                if (ImGui.Button("DetectLanguage.com"))
                    System.Diagnostics.Process.Start("https://detectlanguage.com/");
            }
            var voiceChatLanguageAPIKey = this.Configuration.VoiceChatLanguageAPIKey;
            if (ImGui.InputText("Detect Language API Key", ref voiceChatLanguageAPIKey, 32))
            {
                this.Configuration.VoiceChatLanguageAPIKey = voiceChatLanguageAPIKey;
                this.Configuration.Save();
            }

            var voiceChatWithout3D = this.Configuration.VoiceChatWithout3D;
            if (ImGui.Checkbox("Voice Chat without 3D Space", ref voiceChatWithout3D))
            {
                this.Configuration.VoiceChatWithout3D = voiceChatWithout3D;
                this.Configuration.Save();
            }

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
                if (ImGui.BeginTabItem("NPCs"))
                {
                    DrawVoiceSelectionTable("NPCs", Configuration.MappedNpcs, ref filteredNpcs, ref UpdateDataNpcs, ref resetDataNpcs, ref filterGenderNpcs, ref filterRaceNpcs, ref filterNameNpcs, ref filterVoiceNpcs);

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Players"))
                {
                    DrawVoiceSelectionTable("Players", Configuration.MappedPlayers, ref filteredPlayers, ref UpdateDataPlayers, ref resetDataPlayers, ref filterGenderPlayers, ref filterRacePlayers, ref filterNamePlayers, ref filterVoicePlayers);

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Bubbles"))
                {
                    DrawVoiceSelectionTable("Bubbles", Configuration.MappedNpcs, ref filteredBubbles, ref UpdateDataBubbles, ref resetDataBubbles, ref filterGenderBubbles, ref filterRaceBubbles, ref filterNameBubbles, ref filterVoiceBubbles, true);

                    ImGui.EndTabItem();
                }

                DrawVoicesTab();

                ImGui.EndTabBar();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawVoicesTab()
    {

        if (ImGui.BeginTabItem("Voices"))
        {
            var voiceArr = Configuration.EchokrautVoices.ConvertAll(p => p.ToString()).ToArray();
            var defaultVoiceIndexOld = Configuration.EchokrautVoices.FindIndex(p => p.IsDefault);
            var defaultVoiceIndex = defaultVoiceIndexOld;
            if (ImGui.Combo($"Default Voice:##EKDefaultVoice", ref defaultVoiceIndex, voiceArr, voiceArr.Length))
            {
                Configuration.EchokrautVoices[defaultVoiceIndexOld].IsDefault = false;
                Configuration.EchokrautVoices[defaultVoiceIndex].IsDefault = true;
                this.Configuration.Save();
            }

            if (filteredVoices == null)
            {
                UpdateDataVoices = true;
            }

            if (UpdateDataVoices || (resetDataVoices && (filterGenderVoices.Length == 0 || filterRaceVoices.Length == 0 || filterNameVoices.Length == 0)))
            {
                filteredVoices = Configuration.EchokrautVoices;
                UpdateDataVoices = true;
                resetDataVoices = false;
            }

            if (ImGui.BeginChild("VoicesChild"))
            {
                if (ImGui.BeginTable("Voice Table##VoiceTable", 7, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                    ImGui.TableSetupColumn("##Play", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
                    ImGui.TableSetupColumn("##Stop", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
                    ImGui.TableSetupColumn("Use##Enabled", ImGuiTableColumnFlags.WidthFixed, 35);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Genders", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Races", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Volume", ImGuiTableColumnFlags.WidthStretch, 200);
                    ImGui.TableHeadersRow();
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcName", ref filterNameVoices, 40) || (filterNameVoices.Length > 0 && UpdateDataVoices))
                    {
                        filteredVoices = filteredVoices.FindAll(p => p.VoiceName.ToLower().Contains(filterNameVoices.ToLower()));
                        UpdateDataVoices = true;
                        resetDataVoices = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcGenders", ref filterGenderVoices, 40) || (filterGenderVoices.Length > 0 && UpdateDataVoices))
                    {
                        var foundGenderIndex = Constants.GENDERLIST.FindIndex(p => p.ToString().Contains(filterGenderVoices));
                        filteredVoices = foundGenderIndex >= 0 ? filteredVoices.FindAll(p => p.AllowedGenders.Contains(Constants.GENDERLIST[foundGenderIndex])): filteredVoices.FindAll(p => p.AllowedGenders.Count == 0);
                        UpdateDataVoices = true;
                        resetDataVoices = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilterNpcRaces", ref filterRaceVoices, 40) || (filterRaceVoices.Length > 0 && UpdateDataVoices))
                    {
                        var foundRaceIndex = Constants.RACELIST.FindIndex(p => p.ToString().Contains(filterRaceVoices));
                        filteredVoices = foundRaceIndex >= 0 ? filteredVoices.FindAll(p => p.AllowedRaces.Contains(Constants.RACELIST[foundRaceIndex])) : filteredVoices.FindAll(p => p.AllowedRaces.Count == 0);
                        UpdateDataVoices = true;
                        resetDataVoices = true;
                    }
                    var sortSpecs = ImGui.TableGetSortSpecs();
                    if (sortSpecs.SpecsDirty)
                    {
                        switch (sortSpecs.Specs.ColumnIndex)
                        {
                            case 2:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.Compare(b.IsEnabled.ToString(), a.IsEnabled.ToString()));
                                else
                                    filteredVoices.Sort((a, b) => string.Compare(a.IsEnabled.ToString(), b.IsEnabled.ToString()));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.Compare(a.VoiceName, b.VoiceName));
                                else
                                    filteredVoices.Sort((a, b) => string.Compare(b.VoiceName, a.VoiceName));
                                break;
                        }

                        UpdateDataVoices = false;
                        sortSpecs.SpecsDirty = false;
                    }
                    ImGui.TableNextColumn();

                    foreach (var voice in filteredVoices)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##testvoice{voice}", new Vector2(25, 25), "Test Voice", false, true))
                        {
                            BackendTestVoice(voice);
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Stop.ToIconString()}##stopvoice{voice}", new Vector2(25, 25), "Stop Voice", false, true))
                        {
                            BackendStopVoice();
                        }

                        ImGui.TableNextColumn();
                        var isEnabled = voice.IsEnabled;
                        if (ImGui.Checkbox($"##EKVoiceIsEnabled{voice.ToString()}", ref isEnabled))
                        {
                            voice.IsEnabled = isEnabled;
                            this.Configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.VoiceName);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.CollapsingHeader($"Details:##EKVoiceAllowedGenders{voice}"))
                        {
                            foreach (var gender in Constants.GENDERLIST)
                            {
                                var isAllowed = voice.AllowedGenders.Contains(gender);
                                if (ImGui.Checkbox($"{gender}##EKVoiceAllowedGender{voice}{gender}", ref isAllowed))
                                {
                                    if (isAllowed && !voice.AllowedGenders.Contains(gender))
                                        voice.AllowedGenders.Add(gender);
                                    else if (!isAllowed && voice.AllowedGenders.Contains(gender))
                                        voice.AllowedGenders.Remove(gender);

                                    NpcDataHelper.RefreshSelectables();
                                    this.Configuration.Save();
                                }
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGui.CollapsingHeader($"Details:##EKVoiceAllowedRaces{voice}"))
                        {
                            foreach (var race in Constants.RACELIST)
                            {
                                var isAllowed = voice.AllowedRaces.Contains(race);
                                if (ImGui.Checkbox($"{race}##EKVoiceAllowedRace{voice}{race}", ref isAllowed))
                                {
                                    if (isAllowed && !voice.AllowedRaces.Contains(race))
                                        voice.AllowedRaces.Add(race);
                                    else if (!isAllowed && voice.AllowedRaces.Contains(race))
                                        voice.AllowedRaces.Remove(race);

                                    NpcDataHelper.RefreshSelectables();
                                    this.Configuration.Save();
                                }
                            }
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var voiceVolume = voice.Volume;
                        if (ImGui.SliderFloat($"##EKVoiceVolumeSlider{voice}", ref voiceVolume, 0f, 2f))
                        {
                            voice.Volume = voiceVolume;
                            this.Configuration.Save();
                        }
                    }

                    ImGui.EndTable();
                }

                ImGui.EndChild();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawVoiceSelectionTable(string dataType, List<NpcMapData> realData, ref List<NpcMapData> filteredData, ref bool updateData, ref bool resetData, ref string filterGender, ref string filterRace, ref string filterName, ref string filterVoice, bool isBubble = false)
    {
        if (filteredData == null)
        {
            updateData = true;
        }

        if (updateData || (resetData && (filterGender.Length == 0 || filterRace.Length == 0 || filterName.Length == 0 || filterVoice.Length == 0)))
        {
            if (isBubble)
                filteredData = realData.FindAll(p => p.HasBubbles);
            else
                filteredData = realData.FindAll(p => !p.Name.StartsWith("BB-"));
            updateData = true;
            resetData = false;
        }

        if (ImGui.BeginTable($"{dataType} Table##{dataType}Table", 9, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
            ImGui.TableSetupColumn("Lock", ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 35f);
            ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, 125);
            ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.WidthFixed, 125);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.WidthStretch, 250);
            ImGui.TableSetupColumn("Volume", ImGuiTableColumnFlags.WidthStretch, 200f);
            ImGui.TableSetupColumn($"##{dataType}saves", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25f);
            ImGui.TableSetupColumn($"##{dataType}mapping", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25f);
            ImGui.TableHeadersRow();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText($"##EKFilterNpcGender", ref filterGender, 40) || (filterGender.Length > 0 && updateData))
            {
                var gender = filterGender;
                filteredData = filteredData.FindAll(p => p.Gender.ToString().ToLower().StartsWith(gender.ToLower()));
                updateData = true;
                resetData = true;
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText($"##EKFilterNpcRace", ref filterRace, 40) || (filterRace.Length > 0 && updateData))
            {
                var race = filterRace;
                filteredData = filteredData.FindAll(p => p.Race.ToString().ToLower().Contains(race.ToLower()));
                updateData = true;
                resetData = true;
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText($"##EKFilterNpcName", ref filterName, 40) || (filterName.Length > 0 && updateData))
            {
                var name = filterName;
                filteredData = filteredData.FindAll(p => p.Name.ToLower().Contains(name.ToLower()));
                updateData = true;
                resetData = true;
            }
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.InputText($"##EKFilterNpcVoice", ref filterVoice, 40) || (filterVoice.Length > 0 && updateData))
            {
                var voice = filterVoice;
                filteredData = filteredData.FindAll(p => p.Voice != null && p.Voice.ToString().ToLower().Contains(voice.ToLower()));
                updateData = true;
                resetData = true;
            }

            var sortSpecs = ImGui.TableGetSortSpecs();
            if (sortSpecs.SpecsDirty || updateData)
            {
                switch (sortSpecs.Specs.ColumnIndex)
                {
                    case 0:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(b.DoNotDelete.ToString(), a.DoNotDelete.ToString()));
                        else
                            filteredData.Sort((a, b) => string.Compare(a.DoNotDelete.ToString(), b.DoNotDelete.ToString()));
                        break;
                    case 1:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(isBubble ? b.IsEnabledBubble.ToString() : b.IsEnabled.ToString(), isBubble ? a.IsEnabledBubble.ToString() : a.IsEnabled.ToString()));
                        else
                            filteredData.Sort((a, b) => string.Compare(isBubble ? a.IsEnabledBubble.ToString() : a.IsEnabled.ToString(), isBubble ? b.IsEnabledBubble.ToString() : b.IsEnabled.ToString()));
                        break;
                    case 2:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(a.Gender.ToString(), b.Gender.ToString()));
                        else
                            filteredData.Sort((a, b) => string.Compare(b.Gender.ToString(), a.Gender.ToString()));
                        break;
                    case 3:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(a.Race.ToString(), b.Race.ToString()));
                        else
                            filteredData.Sort((a, b) => string.Compare(b.Race.ToString(), a.Race.ToString()));
                        break;
                    case 4:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(a.Name, b.Name));
                        else
                            filteredData.Sort((a, b) => string.Compare(b.Name, a.Name));
                        break;
                    case 5:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(a.Voice?.ToString(), b.Voice?.ToString()));
                        else
                            filteredData.Sort((a, b) => string.Compare(b.Voice?.ToString(), a.Voice?.ToString()));
                        break;
                    case 6:
                        if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                            filteredData.Sort((a, b) => string.Compare(isBubble ? b.VolumeBubble.ToString() : b.Volume.ToString(), isBubble ? a.VolumeBubble.ToString() : a.Volume.ToString()));
                        else
                            filteredData.Sort((a, b) => string.Compare(isBubble ? a.VolumeBubble.ToString() : a.Volume.ToString(), isBubble ? b.VolumeBubble.ToString() : b.Volume.ToString()));
                        break;
                }

                sortSpecs.SpecsDirty = false;
                updateData = false;
            }

            NpcMapData toBeRemoved = null;
            foreach (NpcMapData mapData in filteredData)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var doNotDelete = mapData.DoNotDelete;
                if (ImGui.Checkbox($"##EKNpcDoNotDelete{mapData.ToString()}", ref doNotDelete))
                {
                    mapData.DoNotDelete = doNotDelete;
                    this.Configuration.Save();
                }
                ImGui.TableNextColumn();
                var isEnabled = isBubble ? mapData.IsEnabledBubble : mapData.IsEnabled;
                if (ImGui.Checkbox($"##EKNpcEnabled{mapData.ToString()}", ref isEnabled))
                {
                    if (isBubble)
                        mapData.IsEnabledBubble = isEnabled;
                    else
                        mapData.IsEnabled = isEnabled;
                    this.Configuration.Save();
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                var presetIndexGender = Constants.GENDERLIST.FindIndex(p => p == mapData.Gender);
                if (ImGui.Combo($"##EKCBox{dataType}{mapData.ToString()}1", ref presetIndexGender, Constants.GENDERNAMESLIST, Constants.GENDERNAMESLIST.Length))
                {
                    var newGender = Constants.GENDERLIST[presetIndexGender];
                    if (newGender != mapData.Gender)
                    {
                        if (realData.Contains(new NpcMapData(mapData.ObjectKind) { Gender = newGender, Race = mapData.Race, Name = mapData.Name }))
                            toBeRemoved = mapData;
                        else
                        {
                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Gender for {dataType}: {mapData.ToString()} from: {mapData.Gender} to: {newGender}", new EKEventId(0, TextSource.None));

                            mapData.Gender = newGender;
                            mapData.DoNotDelete = true;
                            updateData = true;
                            this.Configuration.Save();
                        }
                    }
                    else
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Gender for {dataType}: {mapData}", new EKEventId(0, TextSource.None));
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                var presetIndexRace = Constants.RACELIST.FindIndex(p => p == mapData.Race);
                if (ImGui.Combo($"##EKCBoxPlayer{mapData.ToString()}2", ref presetIndexRace, Constants.RACENAMESLIST, Constants.RACENAMESLIST.Length))
                {
                    var newRace = Constants.RACELIST[presetIndexRace];
                    if (newRace != mapData.Race)
                    {
                        if (realData.Contains(new NpcMapData(mapData.ObjectKind) { Gender = mapData.Gender, Race = newRace, Name = mapData.Name }))
                            toBeRemoved = mapData;
                        else
                        {
                            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Race for {dataType}: {mapData.ToString()} from: {mapData.Race} to: {newRace}", new EKEventId(0, TextSource.None));

                            mapData.Race = newRace;
                            mapData.DoNotDelete = true;
                            updateData = true;
                            this.Configuration.Save();
                        }
                    }
                    else
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for {dataType}: {mapData}", new EKEventId(0, TextSource.None));
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mapData.Name);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                if (mapData.voicesSelectable.Draw(mapData.Voice?.ToString() ?? "", out var selectedIndexVoice))
                {
                    var newVoiceItem = Configuration.EchokrautVoices.FindAll(f => f.IsDefault || (f.IsEnabled && f.AllowedGenders.Contains(mapData.Gender) && f.AllowedRaces.Contains(mapData.Race)))[selectedIndexVoice];

                    if (newVoiceItem != null)
                    {
                        LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated Voice for {dataType}: {mapData.ToString()} from: {mapData.Voice} to: {newVoiceItem}", new EKEventId(0, TextSource.None));

                        mapData.Voice = newVoiceItem;
                        mapData.DoNotDelete = true;
                        updateData = true;
                        this.Configuration.Save();
                    }
                    else
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Voice for {dataType}: {mapData}", new EKEventId(0, TextSource.None));
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                var voiceVolume = 1f;
                if (isBubble)
                    voiceVolume = mapData.VolumeBubble;
                else
                    voiceVolume = mapData.Volume;
                if (ImGui.SliderFloat($"##EKNPCVolumeSlider{mapData.ToString()}", ref voiceVolume, 0f, 2f))
                {
                    if (isBubble)
                        mapData.VolumeBubble = voiceVolume;
                    else
                        mapData.Volume = voiceVolume;
                    mapData.DoNotDelete = true;
                    this.Configuration.Save();
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.TrashAlt.ToIconString()}##del{dataType}saves{mapData.ToString()}", new Vector2(25, 25), "Will remove all local saved audio files for this character", false, true))
                {
                    FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, mapData.Name);
                }
                ImGui.TableNextColumn();
                if (!mapData.DoNotDelete)
                {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.SquareXmark.ToIconString()}##del{dataType}{mapData.ToString()}", new Vector2(25, 25), $"Will remove {dataType} mapping and all local saved audio files for this character", false, true))
                    {
                        toBeRemoved = mapData;
                    }
                }
            }

            if (toBeRemoved != null)
            {
                FileHelper.RemoveSavedNpcFiles(Configuration.LocalSaveLocation, toBeRemoved.Name);
                realData.Remove(toBeRemoved);
                updateData = true;
                Configuration.Save();
            }

            ImGui.EndTable();
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
                Configuration.PhoneticCorrections.Add(new PhoneticCorrection("C'ami", "Kami"));
                Configuration.Save();
                updatePhonData = true;
            }

            if (filteredPhon == null)
            {
                updatePhonData = true;
            }

            if (updatePhonData || (resetPhonFilter && (filterPhonOriginal.Length == 0 || filterPhonCorrected.Length == 0)))
            {
                filteredPhon = Configuration.PhoneticCorrections;
                updatePhonData = true;
                resetPhonFilter = false;
            }

            if (ImGui.BeginTable("Phonetics Table##NPCTable", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupScrollFreeze(0, 3); // Make top row always visible
                ImGui.TableSetupColumn("##delete", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25f);
                ImGui.TableSetupColumn("Original", ImGuiTableColumnFlags.WidthStretch, 150);
                ImGui.TableSetupColumn("Corrected", ImGuiTableColumnFlags.WidthStretch, 150);
                ImGui.TableHeadersRow();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilterPhonOriginal", ref filterPhonOriginal, 40) || (filterPhonOriginal.Length > 0 && updatePhonData))
                {
                    filteredPhon = filteredPhon.FindAll(p => p.OriginalText.ToLower().Contains(filterPhonOriginal.ToLower()));
                    updatePhonData = true;
                    resetPhonFilter = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilterPhonCorrected", ref filterPhonCorrected, 40) || (filterPhonCorrected.Length > 0 && updatePhonData))
                {
                    filteredPhon = filteredPhon.FindAll(p => p.CorrectedText.ToLower().Contains(filterPhonCorrected.ToLower()));
                    updatePhonData = true;
                    resetPhonFilter = true;
                }
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty || updatePhonData)
                {
                    switch (sortSpecs.Specs.ColumnIndex)
                    {
                        case 1:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredPhon.Sort((a, b) => string.Compare(a.OriginalText, b.OriginalText));
                            else
                                filteredPhon.Sort((a, b) => string.Compare(b.OriginalText, a.OriginalText));
                            break;
                        case 2:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredPhon.Sort((a, b) => string.Compare(a.CorrectedText, b.CorrectedText));
                            else
                                filteredPhon.Sort((a, b) => string.Compare(b.CorrectedText, a.CorrectedText));
                            break;
                    }

                    sortSpecs.SpecsDirty = false;
                    updatePhonData = false;
                }
                ImGui.TableNextRow();
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
                            updatePhonData = true;
                        }
                    }
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputText("##origText", ref originalText, 25);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputText("##correctText", ref correctedText, 25);

                PhoneticCorrection toBeRemoved = null;
                int i = 0;
                foreach (PhoneticCorrection phoneticCorrection in filteredPhon)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Trash.ToIconString()}##delphoncorr{phoneticCorrection.ToString()}", new Vector2(25, 25), "Remove phonetic correction", false, true))
                    {
                        toBeRemoved = phoneticCorrection;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##origText{i}", ref phoneticCorrection.OriginalText, 25))
                        Configuration.Save();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##correctText{i}", ref phoneticCorrection.CorrectedText, 25))
                        Configuration.Save();

                    i++;
                }
                           
                if (toBeRemoved != null)
                {
                    Configuration.PhoneticCorrections.Remove(toBeRemoved);
                    Configuration.PhoneticCorrections.Sort();
                    Configuration.Save();
                    updatePhonData = true;
                }

                ImGui.EndTable();
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
                        var showDebugLog = this.Configuration.logConfig.ShowGeneralDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowGeneralDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogGeneralFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowGeneralErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowGeneralErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogGeneralFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.GeneralJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.GeneralJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("General", TextSource.None, Configuration.logConfig.GeneralJumpToBottom, ref filteredLogsGeneral, ref UpdateLogGeneralFilter, ref resetLogGeneralFilter, ref filterLogsGeneralMethod, ref filterLogsGeneralMessage, ref filterLogsGeneralId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.Configuration.logConfig.ShowTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowTalkDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogTalkFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowTalkErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogTalkFilter = true;
                        }
                        var showId0 = this.Configuration.logConfig.ShowTalkId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowTalkId0 = showId0;
                            this.Configuration.Save();
                            UpdateLogTalkFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.TalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.TalkJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("Dialogue", TextSource.AddonTalk, Configuration.logConfig.TalkJumpToBottom, ref filteredLogsTalk, ref UpdateLogTalkFilter, ref resetLogTalkFilter, ref filterLogsTalkMethod, ref filterLogsTalkMessage, ref filterLogsTalkId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Battle dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.Configuration.logConfig.ShowBattleTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogBattleTalkFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowBattleTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowBattleTalkErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogBattleTalkFilter = true;
                        }
                        var showId0 = this.Configuration.logConfig.ShowBattleTalkId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowBattleTalkId0 = showId0;
                            this.Configuration.Save();
                            UpdateLogBattleTalkFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.BattleTalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.BattleTalkJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("BattleDialogue", TextSource.AddonBattleTalk, Configuration.logConfig.BattleTalkJumpToBottom, ref filteredLogsBattleTalk, ref UpdateLogBattleTalkFilter, ref resetLogBattleTalkFilter, ref filterLogsBattleTalkMethod, ref filterLogsBattleTalkMessage, ref filterLogsBattleTalkId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Chat"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.Configuration.logConfig.ShowChatDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowChatDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogChatFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowChatErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowChatErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogChatFilter = true;
                        }
                        var showId0 = this.Configuration.logConfig.ShowChatId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowChatId0 = showId0;
                            this.Configuration.Save();
                            UpdateLogChatFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.ChatJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.ChatJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("Chat", TextSource.Chat, Configuration.logConfig.ChatJumpToBottom, ref filteredLogsChat, ref UpdateLogChatFilter, ref resetLogChatFilter, ref filterLogsChatMethod, ref filterLogsChatMessage, ref filterLogsChatId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Bubbles"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.Configuration.logConfig.ShowBubbleDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowBubbleDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogBubblesFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowBubbleErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowBubbleErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogBubblesFilter = true;
                        }
                        var showId0 = this.Configuration.logConfig.ShowBubbleId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowBubbleId0 = showId0;
                            this.Configuration.Save();
                            UpdateLogBubblesFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.BubbleJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.BubbleJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("Bubbles", TextSource.AddonBubble, Configuration.logConfig.BubbleJumpToBottom, ref filteredLogsBubbles, ref UpdateLogBubblesFilter, ref resetLogBubblesFilter, ref filterLogsBubblesMethod, ref filterLogsBubblesMessage, ref filterLogsBubblesId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice in cutscenes"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.Configuration.logConfig.ShowCutsceneSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowCutsceneSelectStringDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogCutsceneSelectStringFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowCutsceneSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowCutsceneSelectStringErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogCutsceneSelectStringFilter = true;
                        }
                        var showId0 = this.Configuration.logConfig.ShowCutSceneSelectStringId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowCutSceneSelectStringId0 = showId0;
                            this.Configuration.Save();
                            UpdateLogCutsceneSelectStringFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.CutSceneSelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.CutSceneSelectStringJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("PlayerChoiceCutscene", TextSource.AddonCutsceneSelectString, Configuration.logConfig.CutSceneSelectStringJumpToBottom, ref filteredLogsCutsceneSelectString, ref UpdateLogCutsceneSelectStringFilter, ref resetLogCutsceneSelectStringFilter, ref filterLogsCutsceneSelectStringMethod, ref filterLogsCutsceneSelectStringMessage, ref filterLogsCutsceneSelectStringId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.Configuration.logConfig.ShowSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringDebugLog = showDebugLog;
                            this.Configuration.Save();
                            UpdateLogSelectStringFilter = true;
                        }
                        var showErrorLog = this.Configuration.logConfig.ShowSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.Configuration.logConfig.ShowSelectStringErrorLog = showErrorLog;
                            this.Configuration.Save();
                            UpdateLogSelectStringFilter = true;
                        }
                        var showId0 = this.Configuration.logConfig.ShowSelectStringId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.Configuration.logConfig.ShowSelectStringId0 = showId0;
                            this.Configuration.Save();
                            UpdateLogSelectStringFilter = true;
                        }
                        var jumpToBottom = this.Configuration.logConfig.SelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.Configuration.logConfig.SelectStringJumpToBottom = jumpToBottom;
                            this.Configuration.Save();
                        }
                    }
                    DrawLogTable("PlayerChoice", TextSource.AddonSelectString, Configuration.logConfig.SelectStringJumpToBottom, ref filteredLogsSelectString, ref UpdateLogSelectStringFilter, ref resetLogSelectStringFilter, ref filterLogsSelectStringMethod, ref filterLogsSelectStringMessage, ref filterLogsSelectStringId);

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

    private void DrawLogTable(string logType, TextSource source, bool scrollToBottom, ref List<LogMessage> filteredLogs, ref bool updateLogs, ref bool resetLogs, ref string filterMethod, ref string filterMessage, ref string filterId)
    {
        var newData = false;
        if (ImGui.CollapsingHeader("Log:"))
        {
            if (filteredLogs == null)
            {
                updateLogs = true;
            }

            if (updateLogs || (resetLogs && (filterMethod.Length == 0 || filterMessage.Length == 0 || filterId.Length == 0)))
            {
                filteredLogs = LogHelper.RecreateLogList(source);
                updateLogs = true;
                resetLogs = false;
                newData = true;
            }
            if (ImGui.BeginTable($"Log Table##{logType}LogTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                ImGui.TableSetupColumn("Timestamp", ImGuiTableColumnFlags.WidthFixed, 75f);
                ImGui.TableSetupColumn("Method", ImGuiTableColumnFlags.WidthFixed, 150f);
                ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.None, 500f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableHeadersRow();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilter{logType}LogMethod", ref filterMethod, 40) || (filterMethod.Length > 0 && updateLogs))
                {
                    var method = filterMethod;
                    filteredLogs = filteredLogs.FindAll(p => p.method.ToLower().Contains(method.ToLower()));
                    updateLogs = true;
                    resetLogs = true;
                    newData = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilter{logType}LogMessage", ref filterMessage, 80) || (filterMessage.Length > 0 && updateLogs))
                {
                    var message = filterMessage;
                    filteredLogs = filteredLogs.FindAll(p => p.message.ToLower().Contains(message.ToLower()));
                    updateLogs = true;
                    resetLogs = true;
                    newData = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilter{logType}LogId", ref filterId, 40) || (filterId.Length > 0 && updateLogs))
                {
                    var id = filterId;
                    filteredLogs = filteredLogs.FindAll(p => p.eventId.Id.ToString().ToLower().Contains(id.ToLower()));
                    updateLogs = true;
                    resetLogs = true;
                    newData = true;
                }
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty || updateLogs)
                {
                    switch (sortSpecs.Specs.ColumnIndex)
                    {
                        case 0:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => DateTime.Compare(a.timeStamp, b.timeStamp));
                            else
                                filteredLogs.Sort((a, b) => DateTime.Compare(b.timeStamp, a.timeStamp));
                            break;
                        case 1:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => string.Compare(a.method, b.method));
                            else
                                filteredLogs.Sort((a, b) => string.Compare(b.method, a.method));
                            break;
                        case 2:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => string.Compare(a.message, b.message));
                            else
                                filteredLogs.Sort((a, b) => string.Compare(b.message, a.message));
                            break;
                        case 3:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => string.Compare(a.eventId.Id.ToString(), b.eventId.Id.ToString()));
                            else
                                filteredLogs.Sort((a, b) => string.Compare(b.eventId.Id.ToString(), a.eventId.Id.ToString()));
                            break;
                    }

                    updateLogs = false;
                    sortSpecs.SpecsDirty = false;
                }
                foreach (var logMessage in filteredLogs)
                {
                    ImGui.TableNextRow();
                    ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                    ImGui.PushTextWrapPos();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.timeStamp.ToString("HH:mm:ss.fff"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.method);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.message);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.eventId.Id.ToString());
                    ImGui.PopStyleColor();
                }

                if (scrollToBottom && newData)
                {
                    ImGui.SetScrollHereY();
                }

                ImGui.EndTable();
            }
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

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, testConnectionRes, new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private async void BackendTestVoice(EchokrautVoice voice)
    {
        BackendStopVoice();
        var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Testing voice: {voice.ToString()}", eventId);
        // Say the thing
        var voiceMessage = new VoiceMessage
        {
            pActor = null,
            Source = TextSource.VoiceTest,
            Speaker = new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None)
            {
                Gender = voice.AllowedGenders[0],
                Race = voice.AllowedRaces[0],
                Name = voice.VoiceName,
                Voice = voice
            },
            Text = Constants.TESTMESSAGEDE,
            Language = this.clientState.ClientLanguage,
            eventId = eventId
        };
        var volume = VolumeHelper.GetVoiceVolume(eventId) * voice.Volume;

        if (volume > 0)
            BackendHelper.OnSay(voiceMessage, volume) ;
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
        }
    }

    private async void BackendStopVoice()
    {
        BackendHelper.OnCancel(new EKEventId(0, TextSource.AddonTalk));
        LogHelper.End(MethodBase.GetCurrentMethod().Name, new EKEventId(0, TextSource.AddonTalk));
    }

    private void ReloadRemoteMappings()
    {
        JsonLoaderHelper.Setup(this.clientState.ClientLanguage);
    }
    #endregion
}
