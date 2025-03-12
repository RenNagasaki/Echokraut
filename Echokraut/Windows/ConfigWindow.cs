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
    private readonly Configuration? configuration;
    private readonly Echokraut? plugin;
    private readonly IDalamudPluginInterface? pluginInterface;
    private readonly FileDialogManager? fileDialogManager;
    private readonly IClientState? clientState;
    private string testConnectionRes = "";
    private unsafe Camera* camera;
    private IPlayerCharacter? localPlayer;
    #region Voice Selection
    private List<NpcMapData> filteredNpcs = [];
    private static bool _updateDataNpcs;
    public static bool UpdateDataNpcs {
        set => _updateDataNpcs = value;
    }
    private bool resetDataNpcs;
    private string filterGenderNpcs = "";
    private string filterRaceNpcs = "";
    private string filterNameNpcs = "";
    private string filterVoiceNpcs = "";
    private List<NpcMapData> filteredPlayers = [];
    public static bool UpdateDataPlayers { get; set; }
    private bool resetDataPlayers;
    private string filterGenderPlayers = "";
    private string filterRacePlayers = "";
    private string filterNamePlayers = "";
    private string filterVoicePlayers = "";
    private List<NpcMapData> filteredBubbles = [];
    public static bool UpdateDataBubbles { get; set; }
    private bool resetDataBubbles;
    private string filterGenderBubbles = "";
    private string filterRaceBubbles = "";
    private string filterNameBubbles = "";
    private string filterVoiceBubbles = "";
    private List<EchokrautVoice> filteredVoices = [];
    public static bool UpdateDataVoices { get; set; }
    private bool resetDataVoices;
    private string filterGenderVoices = "";
    private string filterRaceVoices = "";
    private string filterNameVoices = "";
    #endregion
    #region Logs
    private List<LogMessage> filteredLogsGeneral = [];
    private string filterLogsGeneralMethod = "";
    private string filterLogsGeneralMessage = "";
    private string filterLogsGeneralId = "";
    public static bool UpdateLogGeneralFilter = true;
    private bool resetLogGeneralFilter = true;
    private List<LogMessage> filteredLogsTalk = [];
    private string filterLogsTalkMethod = "";
    private string filterLogsTalkMessage = "";
    private string filterLogsTalkId = "";
    public static bool UpdateLogTalkFilter = true;
    private bool resetLogTalkFilter = true;
    private List<LogMessage> filteredLogsBattleTalk = [];
    private string filterLogsBattleTalkMethod = "";
    private string filterLogsBattleTalkMessage = "";
    private string filterLogsBattleTalkId = "";
    public static bool UpdateLogBattleTalkFilter = true;
    private bool resetLogBattleTalkFilter = true;
    private List<LogMessage> filteredLogsBubbles = [];
    private string filterLogsBubblesMethod = "";
    private string filterLogsBubblesMessage = "";
    private string filterLogsBubblesId = "";
    public static bool UpdateLogBubblesFilter = true;
    private bool resetLogBubblesFilter = true;
    private List<LogMessage> filteredLogsChat = [];
    private string filterLogsChatMethod = "";
    private string filterLogsChatMessage = "";
    private string filterLogsChatId = "";
    public static bool UpdateLogChatFilter = true;
    private bool resetLogChatFilter = true;
    private List<LogMessage> filteredLogsCutsceneSelectString = [];
    private string filterLogsCutsceneSelectStringMethod = "";
    private string filterLogsCutsceneSelectStringMessage = "";
    private string filterLogsCutsceneSelectStringId = "";
    public static bool UpdateLogCutsceneSelectStringFilter = true;
    private bool resetLogCutsceneSelectStringFilter = true;
    private List<LogMessage> filteredLogsSelectString = [];
    private string filterLogsSelectStringMethod = "";
    private string filterLogsSelectStringMessage = "";
    private string filterLogsSelectStringId = "";
    public static bool UpdateLogSelectStringFilter = true;
    private bool resetLogSelectStringFilter = true;
    #endregion
    #region Phonetic Corrections
    private List<PhoneticCorrection> filteredPhon = [];
    private string filterPhonOriginal = "";
    private string filterPhonCorrected = "";
    private bool updatePhonData = true;
    private bool resetPhonFilter = true;
    private string originalText = "";
    private string correctedText = "";
    #endregion

    #region DeleteConfirmation
    private bool deleteMappedNpcs;
    private bool deleteMappedPlayers;
    private bool deleteMappedBubbles;
    private DateTime lastDeleteClick = DateTime.MinValue;
    private bool deleteSingleAudioData;
    private bool deleteSingleMappingData;
    private DateTime lastSingleDeleteClick = DateTime.MinValue;
    private NpcMapData? toBeDeleted;
    #endregion

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Echokraut plugin, Configuration configuration, IClientState clientState, IDalamudPluginInterface pluginInterface) : base($"Echokraut configuration###EKSettings")
    {
        this.plugin = plugin;
        this.clientState = clientState;
        this.pluginInterface = pluginInterface;
        fileDialogManager = new FileDialogManager();

        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar & ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.configuration = configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (configuration!.IsConfigWindowMovable)
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
            if (lastDeleteClick.AddSeconds(5) <= DateTime.Now && (deleteMappedNpcs || deleteMappedPlayers || deleteMappedBubbles))
            {
                deleteMappedNpcs = false;
                deleteMappedPlayers = false;
                deleteMappedBubbles = false;
            }

            if (lastSingleDeleteClick.AddSeconds(5) <= DateTime.Now && (deleteSingleAudioData ||  deleteSingleMappingData))
            {
                deleteSingleAudioData = false;
                deleteSingleMappingData = false;
                toBeDeleted = null;
            }

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
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
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

                    if (deleteMappedNpcs)
                    {
                        if (ImGui.Button("Click again to confirm!##clearnpc"))
                        {
                            deleteMappedNpcs = false;
                            foreach (NpcMapData npcMapData in configuration!.MappedNpcs.FindAll(p => !p.Name.StartsWith("BB") && !p.DoNotDelete))
                            {
                                FileHelper.RemoveSavedNpcFiles(configuration.LocalSaveLocation, npcMapData.Name);
                                configuration.MappedNpcs.Remove(npcMapData);
                            }
                            UpdateDataNpcs = true;
                            configuration.Save();
                        }
                    }
                    else if (ImGui.Button("Clear mapped npcs##clearnpc")  && !deleteMappedNpcs)
                    {
                        lastDeleteClick = DateTime.Now;
                        deleteMappedNpcs = true;
                    }
                    ImGui.SameLine();
                    if (deleteMappedPlayers)
                    {
                        if (ImGui.Button("Click again to confirm!##clearplayers"))
                        {
                            deleteMappedPlayers = false;
                            foreach (NpcMapData playerMapData in configuration!.MappedPlayers.FindAll(p => !p.DoNotDelete))
                            {
                                FileHelper.RemoveSavedNpcFiles(configuration.LocalSaveLocation, playerMapData.Name);
                                configuration.MappedPlayers.Remove(playerMapData);
                            }
                            UpdateDataPlayers = true;
                            configuration.Save();
                        }
                    }
                    else if (ImGui.Button("Clear mapped players##clearplayers")  && !deleteMappedPlayers)
                    {
                        lastDeleteClick = DateTime.Now;
                        deleteMappedPlayers = true;
                    }
                    ImGui.SameLine();
                    if (deleteMappedBubbles)
                    {
                        if (ImGui.Button("Click again to confirm!##clearbubblenpc"))
                        {
                            deleteMappedBubbles = false;
                            foreach (NpcMapData npcMapData in configuration!.MappedNpcs.FindAll(p => p.Name.StartsWith("BB") && !p.DoNotDelete))
                            {
                                FileHelper.RemoveSavedNpcFiles(configuration.LocalSaveLocation, npcMapData.Name);
                                configuration.MappedNpcs.Remove(npcMapData);
                            }
                            UpdateDataBubbles = true;
                            configuration.Save();
                        }
                    }
                    else if (ImGui.Button("Clear mapped bubbles##clearbubblenpc"))
                    {
                        lastDeleteClick = DateTime.Now;
                        deleteMappedBubbles = true;
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

                using (ImRaii.Disabled(!configuration!.Enabled))
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
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawGeneralSettings()
    {
        var enabled = configuration!.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            this.configuration.Enabled = enabled;
            this.configuration.Save();
        }

        using (ImRaii.Disabled(!enabled))
        {
            var removeStutters = this.configuration.RemoveStutters;
            if (ImGui.Checkbox("Remove stutters", ref removeStutters))
            {
                this.configuration.RemoveStutters = removeStutters;
                this.configuration.Save();
            }

            var hideUiInCutscenes = this.configuration.HideUiInCutscenes;
            if (ImGui.Checkbox("Hide UI in Cutscenes", ref hideUiInCutscenes))
            {
                this.configuration.HideUiInCutscenes = hideUiInCutscenes;
                this.configuration.Save();
                pluginInterface!.UiBuilder.DisableCutsceneUiHide = !hideUiInCutscenes;
            }
        }
    }

    private void DrawDialogueSettings()
    {
        var voiceDialog = configuration!.VoiceDialogue;
        if (ImGui.Checkbox("Voice dialog", ref voiceDialog))
        {
            this.configuration.VoiceDialogue = voiceDialog;
            this.configuration.Save();
        }

        var voicePlayerChoicesCutscene = this.configuration.VoicePlayerChoicesCutscene;
        if (ImGui.Checkbox("Voice player choices in cutscene", ref voicePlayerChoicesCutscene))
        {
            this.configuration.VoicePlayerChoicesCutscene = voicePlayerChoicesCutscene;
            this.configuration.Save();
        }

        var voicePlayerChoices = this.configuration.VoicePlayerChoices;
        if (ImGui.Checkbox("Voice player choices outside of cutscene", ref voicePlayerChoices))
        {
            this.configuration.VoicePlayerChoices = voicePlayerChoices;
            this.configuration.Save();
        }

        var cancelAdvance = this.configuration.CancelSpeechOnTextAdvance;
        if (ImGui.Checkbox("Cancel voice on text advance", ref cancelAdvance))
        {
            this.configuration.CancelSpeechOnTextAdvance = cancelAdvance;
            this.configuration.Save();
        }

        var autoAdvanceOnSpeechCompletion = this.configuration.AutoAdvanceTextAfterSpeechCompleted;
        if (ImGui.Checkbox("Click dialogue window after speech completion", ref autoAdvanceOnSpeechCompletion))
        {
            this.configuration.AutoAdvanceTextAfterSpeechCompleted = autoAdvanceOnSpeechCompletion;
            this.configuration.Save();
        }
    }

    private void DrawBattleDialogueSettings()
    {
        var voiceBattleDialog = configuration!.VoiceBattleDialogue;
        if (ImGui.Checkbox("Voice battle dialog", ref voiceBattleDialog))
        {
            this.configuration.VoiceBattleDialogue = voiceBattleDialog;
            this.configuration.Save();
        }

        using (ImRaii.Disabled(!voiceBattleDialog))
        {
            var voiceBattleDialogQueued = this.configuration.VoiceBattleDialogQueued;
            if (ImGui.Checkbox("Voice battle dialog in a queue", ref voiceBattleDialogQueued))
            {
                this.configuration.VoiceBattleDialogQueued = voiceBattleDialogQueued;
                this.configuration.Save();
            }
        }
    }

    private void DrawBackendSettings()
    {
        var backends = Enum.GetValues<TTSBackends>().ToArray();
        var backendsDisplay = backends.Select(b => b.ToString()).ToArray();
        var presetIndex = Enum.GetValues<TTSBackends>().ToList().IndexOf(configuration!.BackendSelection);
        if (ImGui.Combo($"Select Backend##EKCBoxBackend", ref presetIndex, backendsDisplay, backendsDisplay.Length))
        {
            var backendSelection = backends[presetIndex];
            configuration.BackendSelection = backendSelection;
            configuration.Save();
            BackendHelper.SetBackendType(backendSelection);

            LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}", new EKEventId(0, TextSource.None));
        }

        if (configuration.BackendSelection == TTSBackends.Alltalk)
        {
            if (ImGui.InputText($"Base Url##EKBaseUrl", ref configuration.Alltalk.BaseUrl, 80))
                configuration.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Test Connection##EKTestConnection"))
            {
                BackendCheckReady(new EKEventId(0, TextSource.None));
            }

            if (ImGui.InputText($"Model to reload##EKBaseUrl", ref configuration.Alltalk.ReloadModel, 40))
                configuration.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Restart Service##EKRestartService"))
            {
                BackendReloadService(configuration.Alltalk.ReloadModel);
            }

            if (ImGui.Button($"Reload Voices##EKLoadVoices"))
            {
                BackendGetVoices();
            }

            if (!string.IsNullOrWhiteSpace(testConnectionRes))
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
        }
    }

    private void DrawSaveSettings()
    {
        var loadLocalFirst = configuration!.LoadFromLocalFirst;
        if (ImGui.Checkbox("Search audio locally first before generating", ref loadLocalFirst))
        {
            configuration.LoadFromLocalFirst = loadLocalFirst;
            configuration.Save();
        }
        var saveLocally = configuration.SaveToLocal;
        if (ImGui.Checkbox("Save generated audio locally", ref saveLocally))
        {
            configuration.SaveToLocal = saveLocally;
            configuration.Save();
        }

        using (ImRaii.Disabled(!saveLocally))
        {
            var createMissingLocalSave = configuration.CreateMissingLocalSaveLocation;
            if (ImGui.Checkbox("Create directory if not existing", ref createMissingLocalSave))
            {
                configuration.CreateMissingLocalSaveLocation = createMissingLocalSave;
                configuration.Save();
            }
        }

        using (ImRaii.Disabled(!saveLocally && !loadLocalFirst))
        {
            var localSaveLocation = configuration.LocalSaveLocation;
            if (ImGui.InputText($"##EKSavePath", ref localSaveLocation, 40))
            {
                configuration.LocalSaveLocation = localSaveLocation;
                configuration.Save();
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##import", new Vector2(25, 25),
                    "Select a directory via dialog.", false, true))
            {
                var startDir = configuration.LocalSaveLocation.Length > 0 && Directory.Exists(configuration.LocalSaveLocation)
                ? configuration.LocalSaveLocation
                    : null;

                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Connection test result: {startDir}", new EKEventId(0, TextSource.None));
                fileDialogManager!.OpenFolderDialog("Choose audio files directory", (b, s) =>
                {
                    if (!b)
                        return;

                    configuration.LocalSaveLocation = s;
                    configuration.Save();
                }, startDir);
            }

            if (fileDialogManager is null)
            {
                fileDialogManager!.Draw();
            }
        }
    }

    private unsafe void DrawBubbleSettings()
    {
        var voiceBubbles = configuration!.VoiceBubble;
        if (ImGui.Checkbox("Voice NPC Bubbles", ref voiceBubbles))
        {
            configuration.VoiceBubble = voiceBubbles;
            configuration.Save();
        }

        using (ImRaii.Disabled(!voiceBubbles))
        {
            var voiceBubblesInCity = configuration.VoiceBubblesInCity;
            if (ImGui.Checkbox("Voice NPC Bubbles in City", ref voiceBubblesInCity))
            {
                configuration.VoiceBubblesInCity = voiceBubblesInCity;
                configuration.Save();
            }

            var voiceSourceCam = configuration.VoiceSourceCam;
            if (ImGui.Checkbox("Voice Bubbles with camera as center", ref voiceSourceCam))
            {
                configuration.VoiceSourceCam = voiceSourceCam;
                configuration.Save();
            }

            var voiceBubbleAudibleRange = configuration.VoiceBubbleAudibleRange;
            if (ImGui.SliderFloat("3D Space audible range (shared with chat)", ref voiceBubbleAudibleRange, 0f, 2f))
            {
                configuration.VoiceBubbleAudibleRange = voiceBubbleAudibleRange;
                configuration.Save();

                plugin!.addonBubbleHelper.Update3DFactors(voiceBubbleAudibleRange);
            }


            if (camera == null && CameraManager.Instance() != null)
                camera = CameraManager.Instance()->GetActiveCamera();

            localPlayer = clientState!.LocalPlayer!;

            var position = localPlayer.Position;
            if (configuration.VoiceSourceCam)
                position = camera->CameraBase.SceneCamera.Position;

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
        var voiceChat = configuration!.VoiceChat;
        if (ImGui.Checkbox("Voice Chat", ref voiceChat))
        {
            this.configuration.VoiceChat = voiceChat;
            this.configuration.Save();
        }

        using (ImRaii.Disabled(!voiceChat))
        {
            if (voiceChat)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.TextUnformatted("Detect Language is the service used for automatically detecting the language of the written chat, it's not perfect but works well. To register for free visit: ");
                ImGui.SameLine();
                if (ImGui.Button("DetectLanguage.com"))
                    System.Diagnostics.Process.Start("https://detectlanguage.com/");
            }
            var voiceChatLanguageApiKey = configuration.VoiceChatLanguageAPIKey;
            if (ImGui.InputText("Detect Language API Key", ref voiceChatLanguageApiKey, 32))
            {
                configuration.VoiceChatLanguageAPIKey = voiceChatLanguageApiKey;
                configuration.Save();
            }

            var voiceChatWithout3D = configuration.VoiceChatWithout3D;
            if (ImGui.Checkbox("Voice Chat without 3D Space", ref voiceChatWithout3D))
            {
                configuration.VoiceChatWithout3D = voiceChatWithout3D;
                configuration.Save();
            }

            var voiceChatPlayer = configuration.VoiceChatPlayer;
            if (ImGui.Checkbox("Voice your own Chat", ref voiceChatPlayer))
            {
                configuration.VoiceChatPlayer = voiceChatPlayer;
                configuration.Save();
            }

            var voiceChatSay = configuration.VoiceChatSay;
            if (ImGui.Checkbox("Voice say Chat", ref voiceChatSay))
            {
                configuration.VoiceChatSay = voiceChatSay;
                configuration.Save();
            }

            var voiceChatYell = configuration.VoiceChatYell;
            if (ImGui.Checkbox("Voice yell Chat", ref voiceChatYell))
            {
                configuration.VoiceChatYell = voiceChatYell;
                configuration.Save();
            }

            var voiceChatShout = configuration.VoiceChatShout;
            if (ImGui.Checkbox("Voice shout Chat", ref voiceChatShout))
            {
                configuration.VoiceChatShout = voiceChatShout;
                configuration.Save();
            }

            var voiceChatFreeCompany = configuration.VoiceChatFreeCompany;
            if (ImGui.Checkbox("Voice free company Chat", ref voiceChatFreeCompany))
            {
                configuration.VoiceChatFreeCompany = voiceChatFreeCompany;
                configuration.Save();
            }

            var voiceChatTell = configuration.VoiceChatTell;
            if (ImGui.Checkbox("Voice tell Chat", ref voiceChatTell))
            {
                configuration.VoiceChatTell = voiceChatTell;
                configuration.Save();
            }

            var voiceChatParty = configuration.VoiceChatParty;
            if (ImGui.Checkbox("Voice party Chat", ref voiceChatParty))
            {
                configuration.VoiceChatParty = voiceChatParty;
                configuration.Save();
            }

            var voiceChatAlliance = configuration.VoiceChatAlliance;
            if (ImGui.Checkbox("Voice alliance Chat", ref voiceChatAlliance))
            {
                configuration.VoiceChatAlliance = voiceChatAlliance;
                configuration.Save();
            }

            var voiceChatNoviceNetwork = configuration.VoiceChatNoviceNetwork;
            if (ImGui.Checkbox("Voice novice network Chat", ref voiceChatNoviceNetwork))
            {
                configuration.VoiceChatNoviceNetwork = voiceChatNoviceNetwork;
                configuration.Save();
            }

            var voiceChatLinkshell = configuration.VoiceChatLinkshell;
            if (ImGui.Checkbox("Voice Linkshells", ref voiceChatLinkshell))
            {
                configuration.VoiceChatLinkshell = voiceChatLinkshell;
                configuration.Save();
            }

            var voiceChatCrossLinkshell = configuration.VoiceChatCrossLinkshell;
            if (ImGui.Checkbox("Voice Cross Linkshells", ref voiceChatCrossLinkshell))
            {
                configuration.VoiceChatCrossLinkshell = voiceChatCrossLinkshell;
                configuration.Save();
            }

            var voiceBubbleAudibleRange = configuration.VoiceBubbleAudibleRange;
            if (ImGui.SliderFloat("3D Space audible range (shared with chat)", ref voiceBubbleAudibleRange, 0f, 2f))
            {
                configuration.VoiceBubbleAudibleRange = voiceBubbleAudibleRange;
                configuration.Save();

                plugin!.addonBubbleHelper.Update3DFactors(voiceBubbleAudibleRange);
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
                    DrawVoiceSelectionTable("NPCs", configuration!.MappedNpcs, ref filteredNpcs, ref _updateDataNpcs, ref resetDataNpcs, ref filterGenderNpcs, ref filterRaceNpcs, ref filterNameNpcs, ref filterVoiceNpcs);

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Players"))
                {
                    DrawVoiceSelectionTable("Players", configuration!.MappedPlayers, ref filteredPlayers, ref _updateDataNpcs, ref resetDataPlayers, ref filterGenderPlayers, ref filterRacePlayers, ref filterNamePlayers, ref filterVoicePlayers);

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Bubbles"))
                {
                    DrawVoiceSelectionTable("Bubbles", configuration!.MappedNpcs, ref filteredBubbles, ref _updateDataNpcs, ref resetDataBubbles, ref filterGenderBubbles, ref filterRaceBubbles, ref filterNameBubbles, ref filterVoiceBubbles, true);

                    ImGui.EndTabItem();
                }

                DrawVoicesTab();

                ImGui.EndTabBar();
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawVoicesTab()
    {

        if (ImGui.BeginTabItem("Voices"))
        {
            var voiceArr = configuration!.EchokrautVoices.ConvertAll(p => p.ToString()).ToArray();
            var defaultVoiceIndexOld = configuration.EchokrautVoices.FindIndex(p => p.IsDefault);
            var defaultVoiceIndex = defaultVoiceIndexOld;
            if (ImGui.Combo($"Default Voice:##EKDefaultVoice", ref defaultVoiceIndex, voiceArr, voiceArr.Length))
            {
                configuration.EchokrautVoices[defaultVoiceIndexOld].IsDefault = false;
                configuration.EchokrautVoices[defaultVoiceIndex].IsDefault = true;
                this.configuration.Save();
            }

            UpdateDataVoices = filteredVoices.Count == 0;

            if (UpdateDataVoices || (resetDataVoices && (filterGenderVoices.Length == 0 || filterRaceVoices.Length == 0 || filterNameVoices.Length == 0)))
            {
                filteredVoices = configuration.EchokrautVoices;
                UpdateDataVoices = true;
                resetDataVoices = false;
            }

            if (ImGui.BeginChild("VoicesChild"))
            {
                if (ImGui.BeginTable("Voice Table##VoiceTable", 8, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY))
                {
                    ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                    ImGui.TableSetupColumn("##Play", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
                    ImGui.TableSetupColumn("##Stop", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
                    ImGui.TableSetupColumn("Use##Enabled", ImGuiTableColumnFlags.WidthFixed, 35);
                    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200);
                    ImGui.TableSetupColumn("Options##Enabled", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Genders", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Races", ImGuiTableColumnFlags.WidthFixed, 320);
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
                        var foundRaceIndex = Constants.RACELIST.FindIndex(p => p.ToString().Contains(filterRaceVoices, StringComparison.OrdinalIgnoreCase));
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
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(b.IsEnabled.ToString(), a.IsEnabled.ToString()));
                                else
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(a.IsEnabled.ToString(), b.IsEnabled.ToString()));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(a.VoiceName, b.VoiceName));
                                else
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(b.VoiceName, a.VoiceName));
                                break;
                            case 4:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(b.UseAsRandom.ToString(), a.UseAsRandom.ToString()));
                                else
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(a.UseAsRandom.ToString(), b.UseAsRandom.ToString()));
                                break;
                            case 5:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(string.Join( ",", a.AllowedGenders.OrderBy(p => p.ToString()).ToArray()), string.Join( ",", b.AllowedGenders.OrderBy(p => p.ToString()).ToArray())));
                                else
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(string.Join( ",", b.AllowedGenders.OrderBy(p => p.ToString()).ToArray()), string.Join( ",", a.AllowedGenders.OrderBy(p => p.ToString()).ToArray())));
                                break;
                            case 6:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(string.Join( ",", a.AllowedRaces.OrderBy(p => p.ToString()).ToArray()), string.Join( ",", b.AllowedRaces.OrderBy(p => p.ToString()).ToArray())));
                                else
                                    filteredVoices.Sort((a, b) => string.CompareOrdinal(string.Join( ",", b.AllowedRaces.OrderBy(p => p.ToString()).ToArray()), string.Join( ",", a.AllowedRaces.OrderBy(p => p.ToString()).ToArray())));
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
                        if (ImGui.Checkbox($"##EKVoiceIsEnabled{voice}", ref isEnabled))
                        {
                            voice.IsEnabled = isEnabled;
                            this.configuration.Save();
                        }
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.TextUnformatted(voice.VoiceName);
                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var headerText = voice.UseAsRandom ? "Random - " : "";
                        headerText += voice.IsChildVoice ? "Child - " : "";
                        headerText = headerText.Length >= 3 ? headerText.Substring(0, headerText.Length - 3) : "None";
                        if (ImGui.CollapsingHeader($"{headerText}##EKVoiceOptions{voice}"))
                        {
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.BeginTable($"##OptionsTable{voice}", 3))
                            {
                                ImGui.TableSetupColumn($"##{voice}options", ImGuiTableColumnFlags.WidthFixed, 120);
                                ImGui.TableNextColumn();
                                var useAsRandom = voice.UseAsRandom;
                                if (ImGui.Checkbox($"Random NPC##EKVoiceUseAsRandom{voice}", ref useAsRandom))
                                {
                                    voice.UseAsRandom = useAsRandom;
                                    this.configuration.Save();
                                }
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                var isChildVoice = voice.IsChildVoice;
                                if (ImGui.Checkbox($"Child Voice##EKVoiceIsChildVoice{voice}", ref isChildVoice))
                                {
                                    voice.IsChildVoice = isChildVoice;
                                    this.configuration.Save();
                                }
                            }
                            ImGui.EndTable();
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        headerText = "";
                        voice.AllowedGenders.OrderBy(p => p.ToString()).ToList().ForEach(p => headerText += $"{p} - ");
                        headerText = headerText.Length >= 3 ? headerText.Substring(0, headerText.Length - 3) : "None";
                        if (ImGui.CollapsingHeader($"{headerText}##EKVoiceAllowedGenders{voice}"))
                        {
                            if (ImGui.BeginTable($"##GendersTable{voice}", 1))
                            {
                                ImGui.TableSetupColumn($"##{voice}gender", ImGuiTableColumnFlags.WidthFixed, 100);
                                ImGui.TableNextColumn();
                                if (ImGui.Button($"Reset##EKVoiceAllowedGender{voice}Reset"))
                                {
                                    NpcDataHelper.ReSetVoiceGenders(voice);
                                }
                                ImGui.TableNextRow();
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();

                                foreach (var gender in Constants.GENDERLIST)
                                {
                                    var isAllowed = voice.AllowedGenders.Contains(gender);
                                    if (ImGui.Checkbox($"{gender}##EKVoiceAllowedGender{voice}{gender}", ref isAllowed))
                                    {
                                        if (isAllowed && !voice.AllowedGenders.Contains(gender))
                                            voice.AllowedGenders.Add(gender);
                                        else if (!isAllowed && voice.AllowedGenders.Contains(gender))
                                            voice.AllowedGenders.Remove(gender);

                                        NpcDataHelper.RefreshSelectables(configuration.EchokrautVoices);
                                        this.configuration.Save();
                                    }
                                    ImGui.TableNextRow();
                                    ImGui.TableNextColumn();
                                }
                            }
                            ImGui.EndTable();
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        headerText = "";
                        voice.AllowedRaces.OrderBy(p => p.ToString()).ToList().ForEach(p => headerText += $"{p} - ");
                        if (headerText.Length >= 3)
                            headerText = headerText.Substring(0, headerText.Length - 3);
                        else
                            headerText = "None";
                        if (ImGui.CollapsingHeader($"{headerText}##EKVoiceAllowedRaces{voice}"))
                        {
                            if (ImGui.BeginTable($"##Racestable{voice}", 3))
                            {
                                ImGui.TableSetupColumn($"##{voice}race1", ImGuiTableColumnFlags.WidthFixed, 100);
                                ImGui.TableSetupColumn($"##{voice}race2", ImGuiTableColumnFlags.WidthFixed, 100);
                                ImGui.TableSetupColumn($"##{voice}race3", ImGuiTableColumnFlags.WidthFixed, 100);
                                ImGui.TableNextColumn();

                                var allRaces = voice.AllowedRaces.Count == Constants.RACELIST.Count;
                                if (ImGui.Checkbox($"All##EKVoiceAllowedRace{voice}All", ref allRaces))
                                {
                                    if (allRaces)
                                    {
                                        foreach (var race in Constants.RACELIST)
                                            if (!voice.AllowedRaces.Contains(race))
                                                voice.AllowedRaces.Add(race);
                                    }
                                    else
                                        foreach (var race in Constants.RACELIST)
                                            if (voice.AllowedRaces.Contains(race))
                                                voice.AllowedRaces.Remove(race);

                                    NpcDataHelper.RefreshSelectables(configuration.EchokrautVoices);
                                    this.configuration.Save();
                                }
                                ImGui.TableNextColumn();
                                ImGui.TableNextColumn();
                                if (ImGui.Button($"Reset##EKVoiceAllowedRace{voice}Reset"))
                                {
                                    NpcDataHelper.ReSetVoiceRaces(voice);
                                }
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.TableNextRow();

                                int i = 0;
                                foreach (var race in Constants.RACELIST)
                                {
                                    if (i >= 0 && i < 3)
                                        ImGui.TableNextColumn();

                                    ImGui.SetNextItemWidth(100f);
                                    var isAllowed = voice.AllowedRaces.Contains(race);
                                    if (ImGui.Checkbox($"{race}##EKVoiceAllowedRace{voice}{race}", ref isAllowed))
                                    {
                                        if (isAllowed && !voice.AllowedRaces.Contains(race))
                                            voice.AllowedRaces.Add(race);
                                        else if (!isAllowed && voice.AllowedRaces.Contains(race))
                                            voice.AllowedRaces.Remove(race);

                                        NpcDataHelper.RefreshSelectables(configuration.EchokrautVoices);
                                        this.configuration.Save();
                                    }

                                    i++;
                                    if (i == 3)
                                    {
                                        ImGui.TableNextRow();
                                        i = 0;
                                    }
                                }
                            }
                            ImGui.EndTable();
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        var voiceVolume = voice.Volume;
                        if (ImGui.SliderFloat($"##EKVoiceVolumeSlider{voice}", ref voiceVolume, 0f, 2f))
                        {
                            voice.Volume = voiceVolume;
                            this.configuration.Save();
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
        if (filteredData.Count == 0)
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
                    this.configuration.Save();
                }
                ImGui.TableNextColumn();
                var isEnabled = isBubble ? mapData.IsEnabledBubble : mapData.IsEnabled;
                if (ImGui.Checkbox($"##EKNpcEnabled{mapData.ToString()}", ref isEnabled))
                {
                    if (isBubble)
                        mapData.IsEnabledBubble = isEnabled;
                    else
                        mapData.IsEnabled = isEnabled;
                    this.configuration.Save();
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
                            mapData.RefreshSelectable();
                            mapData.DoNotDelete = true;
                            updateData = true;
                            this.configuration.Save();
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
                            mapData.RefreshSelectable();
                            mapData.DoNotDelete = true;
                            updateData = true;
                            this.configuration.Save();
                        }
                    }
                    else
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Couldnt update Race for {dataType}: {mapData}", new EKEventId(0, TextSource.None));
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mapData.Name);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                if (mapData.VoicesSelectable.Draw(mapData.Voice?.VoiceName ?? "", out var selectedIndexVoice))
                {
                    var newVoiceItem = configuration!.EchokrautVoices.FindAll(f => f.IsSelectable(mapData.Name, mapData.Gender, mapData.Race, mapData.IsChild))[selectedIndexVoice];

                    mapData.Voice = newVoiceItem;
                    mapData.DoNotDelete = true;
                    mapData.RefreshSelectable();
                    updateData = true;
                    this.configuration.Save();
                    LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, $"Updated Voice for {dataType}: {mapData.ToString()} from: {mapData.Voice} to: {newVoiceItem}", new EKEventId(0, TextSource.None));
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
                    this.configuration.Save();
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (deleteSingleAudioData && toBeDeleted == mapData)
                {
                    if (ImGuiUtil.DrawDisabledButton(
                            $"✅##del{dataType}saves{mapData.ToString()}",
                            new Vector2(25, 25), "Click again to confirm deletion!",
                            false,
                            true
                            )
                       )
                    {
                        deleteSingleAudioData = false;
                        toBeDeleted = null;
                        FileHelper.RemoveSavedNpcFiles(configuration.LocalSaveLocation, mapData.Name);
                    }
                }
                else if (ImGuiUtil.DrawDisabledButton(
                             $"{FontAwesomeIcon.TrashAlt.ToIconString()}##del{dataType}saves{mapData.ToString()}",
                             new Vector2(25, 25), "Will remove all local saved audio files for this character",
                             false,
                             true) && !deleteSingleAudioData
                         )
                {
                    lastSingleDeleteClick = DateTime.Now;
                    deleteSingleAudioData = true;
                    toBeDeleted = mapData;
                }
                ImGui.TableNextColumn();
                if (deleteSingleMappingData && toBeDeleted == mapData)
                {
                    if (ImGuiUtil.DrawDisabledButton(
                            $"✅##del{dataType}{mapData.ToString()}",
                            new Vector2(25, 25), "Click again to confirm deletion!",
                            false,
                            true
                        )
                       )
                    {
                        deleteSingleMappingData = false;
                        toBeDeleted = null;
                        toBeRemoved = mapData;
                    }
                }
                else if (!mapData.DoNotDelete)
                {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGuiUtil.DrawDisabledButton(
                            $"{FontAwesomeIcon.SquareXmark.ToIconString()}##del{dataType}{mapData.ToString()}",
                            new Vector2(25, 25),
                            $"Will remove {dataType} mapping and all local saved audio files for this character",
                            false,
                            true) && !deleteSingleMappingData
                        )
                    {
                        lastSingleDeleteClick = DateTime.Now;
                        deleteSingleMappingData = true;
                        toBeDeleted = mapData;
                    }
                }
            }

            if (toBeRemoved != null)
            {
                FileHelper.RemoveSavedNpcFiles(configuration.LocalSaveLocation, toBeRemoved.Name);
                realData.Remove(toBeRemoved);
                updateData = true;
                configuration.Save();
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
            if (configuration.PhoneticCorrections.Count == 0)
            {
                configuration.PhoneticCorrections.Add(new PhoneticCorrection("C'ami", "Kami"));
                configuration.Save();
                updatePhonData = true;
            }

            if (filteredPhon == null)
            {
                updatePhonData = true;
            }

            if (updatePhonData || (resetPhonFilter && (filterPhonOriginal.Length == 0 || filterPhonCorrected.Length == 0)))
            {
                filteredPhon = configuration.PhoneticCorrections;
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
                        if (!configuration.PhoneticCorrections.Contains(newCorrection))
                        {
                            configuration.PhoneticCorrections.Add(newCorrection);
                            configuration.PhoneticCorrections.Sort();
                            configuration.Save();
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
                        configuration.Save();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##correctText{i}", ref phoneticCorrection.CorrectedText, 25))
                        configuration.Save();

                    i++;
                }

                if (toBeRemoved != null)
                {
                    configuration.PhoneticCorrections.Remove(toBeRemoved);
                    configuration.PhoneticCorrections.Sort();
                    configuration.Save();
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
                        var showDebugLog = this.configuration.logConfig.ShowGeneralDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowGeneralDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogGeneralFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowGeneralErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowGeneralErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogGeneralFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.GeneralJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.GeneralJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("General", TextSource.None, configuration.logConfig.GeneralJumpToBottom, ref filteredLogsGeneral, ref UpdateLogGeneralFilter, ref resetLogGeneralFilter, ref filterLogsGeneralMethod, ref filterLogsGeneralMessage, ref filterLogsGeneralId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.configuration.logConfig.ShowTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowTalkDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogTalkFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowTalkErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogTalkFilter = true;
                        }
                        var showId0 = this.configuration.logConfig.ShowTalkId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.configuration.logConfig.ShowTalkId0 = showId0;
                            this.configuration.Save();
                            UpdateLogTalkFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.TalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.TalkJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("Dialogue", TextSource.AddonTalk, configuration.logConfig.TalkJumpToBottom, ref filteredLogsTalk, ref UpdateLogTalkFilter, ref resetLogTalkFilter, ref filterLogsTalkMethod, ref filterLogsTalkMessage, ref filterLogsTalkId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Battle dialogue"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.configuration.logConfig.ShowBattleTalkDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowBattleTalkDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogBattleTalkFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowBattleTalkErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowBattleTalkErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogBattleTalkFilter = true;
                        }
                        var showId0 = this.configuration.logConfig.ShowBattleTalkId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.configuration.logConfig.ShowBattleTalkId0 = showId0;
                            this.configuration.Save();
                            UpdateLogBattleTalkFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.BattleTalkJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.BattleTalkJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("BattleDialogue", TextSource.AddonBattleTalk, configuration.logConfig.BattleTalkJumpToBottom, ref filteredLogsBattleTalk, ref UpdateLogBattleTalkFilter, ref resetLogBattleTalkFilter, ref filterLogsBattleTalkMethod, ref filterLogsBattleTalkMessage, ref filterLogsBattleTalkId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Chat"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.configuration.logConfig.ShowChatDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowChatDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogChatFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowChatErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowChatErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogChatFilter = true;
                        }
                        var showId0 = this.configuration.logConfig.ShowChatId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.configuration.logConfig.ShowChatId0 = showId0;
                            this.configuration.Save();
                            UpdateLogChatFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.ChatJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.ChatJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("Chat", TextSource.Chat, configuration.logConfig.ChatJumpToBottom, ref filteredLogsChat, ref UpdateLogChatFilter, ref resetLogChatFilter, ref filterLogsChatMethod, ref filterLogsChatMessage, ref filterLogsChatId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Bubbles"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.configuration.logConfig.ShowBubbleDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowBubbleDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogBubblesFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowBubbleErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowBubbleErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogBubblesFilter = true;
                        }
                        var showId0 = this.configuration.logConfig.ShowBubbleId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.configuration.logConfig.ShowBubbleId0 = showId0;
                            this.configuration.Save();
                            UpdateLogBubblesFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.BubbleJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.BubbleJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("Bubbles", TextSource.AddonBubble, configuration.logConfig.BubbleJumpToBottom, ref filteredLogsBubbles, ref UpdateLogBubblesFilter, ref resetLogBubblesFilter, ref filterLogsBubblesMethod, ref filterLogsBubblesMessage, ref filterLogsBubblesId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice in cutscenes"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.configuration.logConfig.ShowCutsceneSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowCutsceneSelectStringDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogCutsceneSelectStringFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowCutsceneSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowCutsceneSelectStringErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogCutsceneSelectStringFilter = true;
                        }
                        var showId0 = this.configuration.logConfig.ShowCutSceneSelectStringId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.configuration.logConfig.ShowCutSceneSelectStringId0 = showId0;
                            this.configuration.Save();
                            UpdateLogCutsceneSelectStringFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.CutSceneSelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.CutSceneSelectStringJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("PlayerChoiceCutscene", TextSource.AddonCutsceneSelectString, configuration.logConfig.CutSceneSelectStringJumpToBottom, ref filteredLogsCutsceneSelectString, ref UpdateLogCutsceneSelectStringFilter, ref resetLogCutsceneSelectStringFilter, ref filterLogsCutsceneSelectStringMethod, ref filterLogsCutsceneSelectStringMessage, ref filterLogsCutsceneSelectStringId);

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Player choice"))
                {
                    if (ImGui.CollapsingHeader("Options:"))
                    {
                        var showDebugLog = this.configuration.logConfig.ShowSelectStringDebugLog;
                        if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
                        {
                            this.configuration.logConfig.ShowSelectStringDebugLog = showDebugLog;
                            this.configuration.Save();
                            UpdateLogSelectStringFilter = true;
                        }
                        var showErrorLog = this.configuration.logConfig.ShowSelectStringErrorLog;
                        if (ImGui.Checkbox("Show error logs", ref showErrorLog))
                        {
                            this.configuration.logConfig.ShowSelectStringErrorLog = showErrorLog;
                            this.configuration.Save();
                            UpdateLogSelectStringFilter = true;
                        }
                        var showId0 = this.configuration.logConfig.ShowSelectStringId0;
                        if (ImGui.Checkbox("Show ID: 0", ref showId0))
                        {
                            this.configuration.logConfig.ShowSelectStringId0 = showId0;
                            this.configuration.Save();
                            UpdateLogSelectStringFilter = true;
                        }
                        var jumpToBottom = this.configuration.logConfig.SelectStringJumpToBottom;
                        if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
                        {
                            this.configuration.logConfig.SelectStringJumpToBottom = jumpToBottom;
                            this.configuration.Save();
                        }
                    }
                    DrawLogTable("PlayerChoice", TextSource.AddonSelectString, configuration.logConfig.SelectStringJumpToBottom, ref filteredLogsSelectString, ref UpdateLogSelectStringFilter, ref resetLogSelectStringFilter, ref filterLogsSelectStringMethod, ref filterLogsSelectStringMessage, ref filterLogsSelectStringId);

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
            if (this.configuration.BackendSelection == TTSBackends.Alltalk)
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
            if (this.configuration.BackendSelection == TTSBackends.Alltalk)
                BackendHelper.SetBackendType(this.configuration.BackendSelection);

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
