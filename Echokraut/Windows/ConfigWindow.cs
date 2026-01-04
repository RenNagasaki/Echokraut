using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using System.Reflection;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using OtterGui;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly FileDialogManager? fileDialogManager;
    private unsafe Camera* camera;
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
    private string filterNoteVoices = "";
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
    public static List<LogMessage> FilteredLogsBackend = [];
    public static string FilterLogsBackendMethod = "";
    public static string FilterLogsBackendMessage = "";
    public static string FilterLogsBackendId = "";
    public static bool UpdateLogBackendFilter = true;
    public static bool ResetLogBackendFilter = true;
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
    public ConfigWindow() : base($"Echokraut Plugin.Configuration###EKSettings")
    {
        fileDialogManager = new FileDialogManager();

        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar & ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Plugin.Configuration!.IsConfigWindowMovable)
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
            Plugin.Framework.RunOnFrameworkThread(() => {LogHelper.UpdateLogList(); });
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

            using var tabBar = ImRaii.TabBar("All Settings##EKAllSettingsTabBar");
            if (tabBar)
            {
                using (var tabItemSettings = ImRaii.TabItem("Settings"))
                {
                    if (tabItemSettings)
                        DrawSettings();
                }

                using (var tabItemVoiceSel = ImRaii.TabItem("Voice selection"))
                {
                    if (tabItemVoiceSel)
                        DrawVoiceSelection();
                }

                using (var tabItemPhonCor = ImRaii.TabItem("Phonetic corrections"))
                {
                    if (tabItemPhonCor)
                        DrawPhoneticCorrections();
                }

                using (var tabItemLogs = ImRaii.TabItem("Logs"))
                {
                    if (tabItemLogs)
                        DrawLogs();
                }
            }
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
            using var tabBar = ImRaii.TabBar("Settings##EKSettingsTab");
            if (tabBar)
            {
                using (var tabItemGeneral = ImRaii.TabItem("General"))
                {
                    if (tabItemGeneral)
                    {
                        DrawGeneralSettings();
                        DrawExternalLinkButtons(ImGui.GetContentRegionAvail(), new Vector2(0, 60));

                        if (ImGui.CollapsingHeader("Unrecoverable actions:"))
                        {
                            if (deleteMappedNpcs)
                            {
                                if (ImGui.Button("Click again to confirm!##clearnpc"))
                                {
                                    deleteMappedNpcs = false;
                                    foreach (NpcMapData npcMapData in
                                             Plugin.Configuration!.MappedNpcs.FindAll(p => !p.Name.StartsWith("BB") &&
                                                 !p.DoNotDelete))
                                    {
                                        AudioFileHelper.RemoveSavedNpcFiles(Plugin.Configuration.LocalSaveLocation,
                                                                            npcMapData.Name);
                                        Plugin.Configuration.MappedNpcs.Remove(npcMapData);
                                    }

                                    UpdateDataNpcs = true;
                                    Plugin.Configuration.Save();
                                }
                            }
                            else if (ImGui.Button("Clear mapped npcs##clearnpc") && !deleteMappedNpcs)
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
                                    foreach (NpcMapData playerMapData in
                                             Plugin.Configuration!.MappedPlayers.FindAll(p => !p.DoNotDelete))
                                    {
                                        AudioFileHelper.RemoveSavedNpcFiles(Plugin.Configuration.LocalSaveLocation,
                                                                            playerMapData.Name);
                                        Plugin.Configuration.MappedPlayers.Remove(playerMapData);
                                    }

                                    UpdateDataPlayers = true;
                                    Plugin.Configuration.Save();
                                }
                            }
                            else if (ImGui.Button("Clear mapped players##clearplayers") && !deleteMappedPlayers)
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
                                    foreach (NpcMapData npcMapData in
                                             Plugin.Configuration!.MappedNpcs.FindAll(p => p.Name.StartsWith("BB") &&
                                                 !p.DoNotDelete))
                                    {
                                        AudioFileHelper.RemoveSavedNpcFiles(Plugin.Configuration.LocalSaveLocation,
                                                                            npcMapData.Name);
                                        Plugin.Configuration.MappedNpcs.Remove(npcMapData);
                                    }

                                    UpdateDataBubbles = true;
                                    Plugin.Configuration.Save();
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

                        }
                        ImGui.NewLine();
                        ImGui.TextUnformatted("Available commands:");
                        foreach (var commandKey in CommandHelper.CommandKeys)
                        {
                            var command = Plugin.CommandManager.Commands[commandKey];
                            ImGui.TextUnformatted(commandKey);
                            ImGui.SameLine();
                            ImGui.TextUnformatted(command.HelpMessage);
                        }
                    }
                }

                using (ImRaii.Disabled(!Plugin.Configuration!.Enabled))
                {
                    using (var tabItemDialogue = ImRaii.TabItem("Dialogue"))
                    {
                        if (tabItemDialogue)
                            DrawDialogueSettings();
                    }

                    using (var tabItemBDialogue = ImRaii.TabItem("Battle dialogue"))
                    {
                        if (tabItemBDialogue)
                            DrawBattleDialogueSettings();
                    }

                    using (var tabItemChat = ImRaii.TabItem("Chat"))
                    {
                        if (tabItemChat)
                            DrawChatSettings();
                    }

                    using (var tabItemBubbles = ImRaii.TabItem("Bubbles"))
                    {
                        if (tabItemBubbles)
                            DrawBubbleSettings();
                    }

                    using (var tabItemSaveLoad = ImRaii.TabItem("Save/Load"))
                    {
                        if (tabItemSaveLoad)
                            DrawSaveSettings();
                    }

                    using (var tabItemBackend = ImRaii.TabItem("Backend"))
                    {
                        if (tabItemBackend)
                            DrawBackendSettings();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawGeneralSettings()
    {
        var enabled = Plugin.Configuration!.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            Plugin.Configuration.Enabled = enabled;
            Plugin.Configuration.Save();
        }

        using (ImRaii.Disabled(!enabled))
        {
            var generateBySentence = Plugin.Configuration!.GenerateBySentence;
            if (ImGui.Checkbox("Generate text per sentence instead of all at once(shorter sentences may sound weird, only needed if using CPU for inference)", ref generateBySentence))
            {
                Plugin.Configuration.GenerateBySentence = generateBySentence;
                Plugin.Configuration.Save();
            }
            
            var removeStutters = Plugin.Configuration.RemoveStutters;
            if (ImGui.Checkbox("Remove stutters", ref removeStutters))
            {
                Plugin.Configuration.RemoveStutters = removeStutters;
                Plugin.Configuration.Save();
            }

            var hideUiInCutscenes = Plugin.Configuration.HideUiInCutscenes;
            if (ImGui.Checkbox("Hide UI in Cutscenes", ref hideUiInCutscenes))
            {
                Plugin.Configuration.HideUiInCutscenes = hideUiInCutscenes;
                Plugin.Configuration.Save();
                Plugin.PluginInterface.UiBuilder.DisableCutsceneUiHide = !hideUiInCutscenes;
            }
            ImGui.NewLine();
            if (ImGui.CollapsingHeader("Experimental options:"))
            {
                var showExtraOptionsInDialogue = Plugin.Configuration!.ShowExtraOptionsInDialogue;
                if (ImGui.Checkbox("Shows Pause/Resume, Stop/Play and Mute buttons below the text in dialogues",
                                   ref showExtraOptionsInDialogue))
                {
                    Plugin.Configuration.ShowExtraOptionsInDialogue = showExtraOptionsInDialogue;
                    Plugin.Configuration.Save();
                }

                using (ImRaii.Disabled(!showExtraOptionsInDialogue))
                {
                    var showExtraExtraOptionsInDialogue = Plugin.Configuration!.ShowExtraExtraOptionsInDialogue;
                    if (ImGui.Checkbox("Shows even more options below the text in dialogues",
                                       ref showExtraExtraOptionsInDialogue))
                    {
                        Plugin.Configuration.ShowExtraExtraOptionsInDialogue = showExtraExtraOptionsInDialogue;
                        Plugin.Configuration.Save();
                    }
                }
            
                var removePunctuation = Plugin.Configuration.RemovePunctuation;
                if (ImGui.Checkbox("Remove punctuation from the text (Experimental – may reduce end-of-speech hallucinations)", ref removePunctuation))
                {
                    Plugin.Configuration.RemovePunctuation = removePunctuation;
                    Plugin.Configuration.Save();
                }
            }
        }
    }

    private void DrawDialogueSettings()
    {
        var voiceDialog = Plugin.Configuration!.VoiceDialogue;
        if (ImGui.Checkbox("Voice dialog", ref voiceDialog))
        {
            Plugin.Configuration.VoiceDialogue = voiceDialog;
            Plugin.Configuration.Save();
        }

        using (ImRaii.Disabled(!voiceDialog))
        {
            var voiceDialogueIn3D = Plugin.Configuration.VoiceDialogueIn3D;
            if (ImGui.Checkbox("Voice dialogue in 3D Space", ref voiceDialogueIn3D))
            {
                Plugin.Configuration.VoiceDialogueIn3D = voiceDialogueIn3D;
                Plugin.Configuration.Save();
            }

            if (voiceDialogueIn3D)
            {
                var voiceBubbleAudibleRange = Plugin.Configuration.Voice3DAudibleRange;
                if (ImGui.SliderFloat("3D Space audible dropoff (shared setting), higher = lesser range, 0 = on player", ref voiceBubbleAudibleRange, 0f, 1f))
                {
                    Plugin.Configuration.Voice3DAudibleRange = voiceBubbleAudibleRange;
                    Plugin.Configuration.Save();

                    PlayingHelper.Update3DFactors(voiceBubbleAudibleRange);
                }
            }
        }

        var voicePlayerChoicesCutscene = Plugin.Configuration.VoicePlayerChoicesCutscene;
        if (ImGui.Checkbox("Voice player choices in cutscene", ref voicePlayerChoicesCutscene))
        {
            Plugin.Configuration.VoicePlayerChoicesCutscene = voicePlayerChoicesCutscene;
            Plugin.Configuration.Save();
        }

        var voicePlayerChoices = Plugin.Configuration.VoicePlayerChoices;
        if (ImGui.Checkbox("Voice player choices outside of cutscene", ref voicePlayerChoices))
        {
            Plugin.Configuration.VoicePlayerChoices = voicePlayerChoices;
            Plugin.Configuration.Save();
        }

        var cancelAdvance = Plugin.Configuration.CancelSpeechOnTextAdvance;
        if (ImGui.Checkbox("Cancel voice on text advance", ref cancelAdvance))
        {
            Plugin.Configuration.CancelSpeechOnTextAdvance = cancelAdvance;
            Plugin.Configuration.Save();
        }

        var autoAdvanceOnSpeechCompletion = Plugin.Configuration.AutoAdvanceTextAfterSpeechCompleted;
        if (ImGui.Checkbox("Click dialogue window after speech completion", ref autoAdvanceOnSpeechCompletion))
        {
            Plugin.Configuration.AutoAdvanceTextAfterSpeechCompleted = autoAdvanceOnSpeechCompletion;
            Plugin.Configuration.Save();
        }

        var voiceRetainers = Plugin.Configuration.VoiceRetainers;
        if (ImGui.Checkbox("Voice retainer dialogues", ref voiceRetainers))
        {
            Plugin.Configuration.VoiceRetainers = voiceRetainers;
            Plugin.Configuration.Save();
        }
    }

    private void DrawBattleDialogueSettings()
    {
        var voiceBattleDialog = Plugin.Configuration!.VoiceBattleDialogue;
        if (ImGui.Checkbox("Voice battle dialog", ref voiceBattleDialog))
        {
            Plugin.Configuration.VoiceBattleDialogue = voiceBattleDialog;
            Plugin.Configuration.Save();
        }

        using (ImRaii.Disabled(!voiceBattleDialog))
        {
            var voiceBattleDialogQueued = Plugin.Configuration.VoiceBattleDialogQueued;
            if (ImGui.Checkbox("Voice battle dialog in a queue", ref voiceBattleDialogQueued))
            {
                Plugin.Configuration.VoiceBattleDialogQueued = voiceBattleDialogQueued;
                Plugin.Configuration.Save();
            }
        }
    }

    private void DrawBackendSettings()
    {
        var backends = Enum.GetValues<TTSBackends>().ToArray();
        var backendsDisplay = backends.Select(b => b.ToString()).ToArray();
        var presetIndex = Enum.GetValues<TTSBackends>().ToList().IndexOf(Plugin.Configuration!.BackendSelection);
        if (ImGui.Combo($"Select Backend##EKCBoxBackend", ref presetIndex, backendsDisplay, backendsDisplay.Length))
        {
            var backendSelection = backends[presetIndex];
            Plugin.Configuration.BackendSelection = backendSelection;
            Plugin.Configuration.Save();
            BackendHelper.SetBackendType(backendSelection);

            LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}", new EKEventId(0, TextSource.None));
        }

        if (Plugin.Configuration.BackendSelection == TTSBackends.Alltalk)
            AlltalkInstanceWindow.DrawAlltalk(false);
    }

    private void DrawSaveSettings()
    {
        var loadLocalFirst = Plugin.Configuration!.LoadFromLocalFirst;
        if (ImGui.Checkbox("Search audio locally first before generating", ref loadLocalFirst))
        {
            Plugin.Configuration.LoadFromLocalFirst = loadLocalFirst;
            Plugin.Configuration.Save();
        }
        var saveLocally = Plugin.Configuration.SaveToLocal;
        if (ImGui.Checkbox("Save generated audio locally", ref saveLocally))
        {
            Plugin.Configuration.SaveToLocal = saveLocally;
            Plugin.Configuration.Save();
        }

        using (ImRaii.Disabled(!saveLocally))
        {
            var createMissingLocalSave = Plugin.Configuration.CreateMissingLocalSaveLocation;
            if (ImGui.Checkbox("Create directory if not existing", ref createMissingLocalSave))
            {
                Plugin.Configuration.CreateMissingLocalSaveLocation = createMissingLocalSave;
                Plugin.Configuration.Save();
            }
        }

        using (ImRaii.Disabled(!saveLocally && !loadLocalFirst))
        {
            var localSaveLocation = Plugin.Configuration.LocalSaveLocation;
            if (ImGui.InputText($"##EKSavePath", ref localSaveLocation, 40))
            {
                Plugin.Configuration.LocalSaveLocation = localSaveLocation;
                Plugin.Configuration.Save();
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##import", new Vector2(25, 25),
                    "Select a directory via dialog.", false, true))
            {
                var startDir = Plugin.Configuration.LocalSaveLocation.Length > 0 && Directory.Exists(Plugin.Configuration.LocalSaveLocation)
                ? Plugin.Configuration.LocalSaveLocation
                    : null;

                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Connection test result: {startDir}", new EKEventId(0, TextSource.None));
                fileDialogManager!.OpenFolderDialog("Choose audio files directory", (b, s) =>
                {
                    if (!b)
                        return;

                    Plugin.Configuration.LocalSaveLocation = s;
                    Plugin.Configuration.Save();
                }, startDir);
            }

            fileDialogManager!.Draw();
        }

        ImGui.NewLine();

        if (ImGui.CollapsingHeader("Google Drive:"))
        {
            var googleDriveRequestVoiceLine = Plugin.Configuration.GoogleDriveRequestVoiceLine;
            ImGui.LabelText("", "This setting may tremendously help in the future. Please consider helping out.");
            if (ImGui.Checkbox("Send any dialogue line to my (Ren Nagasaki's) Share for a full database of needed voice lines.", ref googleDriveRequestVoiceLine))
            {
                Plugin.Configuration.GoogleDriveRequestVoiceLine = googleDriveRequestVoiceLine;
                Plugin.Configuration.Save();

                if (googleDriveRequestVoiceLine)
                    GoogleDriveHelper.CreateDriveServicePkceAsync();
            }
            ImGui.NewLine();
            
            using (ImRaii.Disabled(!saveLocally))
            {
                var googleDriveUpload = Plugin.Configuration.GoogleDriveUpload;
                if (ImGui.Checkbox("Upload to Google Drive (requires 'Save generated audio locally')", ref googleDriveUpload))
                {
                    Plugin.Configuration.GoogleDriveUpload = googleDriveUpload;
                    Plugin.Configuration.Save();
                }
            }

            var googleDriveDownload = Plugin.Configuration.GoogleDriveDownload;
            if (ImGui.Checkbox("Download from Google Drive Share", ref googleDriveDownload))
            {
                Plugin.Configuration.GoogleDriveDownload = googleDriveDownload;
                Plugin.Configuration.Save();
            }

            using (ImRaii.Disabled(!googleDriveDownload))
            {
                var googleDriveDownloadPeriodically = Plugin.Configuration.GoogleDriveDownloadPeriodically;
                if (ImGui.Checkbox("Download periodically (every 60 minutes, only updating/downloading new files)",
                                   ref googleDriveDownloadPeriodically))
                {
                    Plugin.Configuration.GoogleDriveDownloadPeriodically = googleDriveDownloadPeriodically;
                    Plugin.Configuration.Save();
                }

                ImGui.LabelText("", "Google Drive share link");
                var googleDriveShareLink = Plugin.Configuration.GoogleDriveShareLink;
                if (ImGui.InputText($"##EKGDShareLink", ref googleDriveShareLink, 100))
                {
                    Plugin.Configuration.GoogleDriveShareLink = googleDriveShareLink;
                    Plugin.Configuration.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button("Download now##EKGDDownloadNow"))
                {
                    GoogleDriveHelper.DownloadFolder(Plugin.Configuration.LocalSaveLocation, Plugin.Configuration.GoogleDriveShareLink);
                }
            }
        }
    }

    private unsafe void DrawBubbleSettings()
    {
        var voiceBubbles = Plugin.Configuration!.VoiceBubble;
        if (ImGui.Checkbox("Voice NPC Bubbles", ref voiceBubbles))
        {
            Plugin.Configuration.VoiceBubble = voiceBubbles;
            Plugin.Configuration.Save();
        }

        using (ImRaii.Disabled(!voiceBubbles))
        {
            var voiceBubblesInCity = Plugin.Configuration.VoiceBubblesInCity;
            if (ImGui.Checkbox("Voice NPC Bubbles in City", ref voiceBubblesInCity))
            {
                Plugin.Configuration.VoiceBubblesInCity = voiceBubblesInCity;
                Plugin.Configuration.Save();
            }

            var voiceSourceCam = Plugin.Configuration.VoiceSourceCam;
            if (ImGui.Checkbox("Voice Bubbles with camera as center", ref voiceSourceCam))
            {
                Plugin.Configuration.VoiceSourceCam = voiceSourceCam;
                Plugin.Configuration.Save();
            }

            var voiceBubbleAudibleRange = Plugin.Configuration.Voice3DAudibleRange;
            if (ImGui.SliderFloat("3D Space audible dropoff (shared setting), higher = lesser range, 0 = on player", ref voiceBubbleAudibleRange, 0f, 1f))
            {
                Plugin.Configuration.Voice3DAudibleRange = voiceBubbleAudibleRange;
                Plugin.Configuration.Save();

                PlayingHelper.Update3DFactors(voiceBubbleAudibleRange);
            }


            if (camera == null && CameraManager.Instance() != null)
                camera = CameraManager.Instance()->GetActiveCamera();

            var position = DalamudHelper.LocalPlayer.Position;
            if (Plugin.Configuration.VoiceSourceCam)
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
        var voiceChat = Plugin.Configuration!.VoiceChat;
        if (ImGui.Checkbox("Voice Chat", ref voiceChat))
        {
            Plugin.Configuration.VoiceChat = voiceChat;
            Plugin.Configuration.Save();
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
            var voiceChatLanguageApiKey = Plugin.Configuration.VoiceChatLanguageAPIKey;
            if (ImGui.InputText("Detect Language API Key", ref voiceChatLanguageApiKey, 32))
            {
                Plugin.Configuration.VoiceChatLanguageAPIKey = voiceChatLanguageApiKey;
                Plugin.Configuration.Save();
            }

            var voiceChatIn3D = Plugin.Configuration.VoiceChatIn3D;
            if (ImGui.Checkbox("Voice Chat in 3D Space", ref voiceChatIn3D))
            {
                Plugin.Configuration.VoiceChatIn3D = voiceChatIn3D;
                Plugin.Configuration.Save();
            }

            if (voiceChatIn3D)
            {
                var voiceBubbleAudibleRange = Plugin.Configuration.Voice3DAudibleRange;
                if (ImGui.SliderFloat("3D Space audible dropoff (shared setting), higher = lesser range, 0 = on player", ref voiceBubbleAudibleRange, 0f, 1f))
                {
                    Plugin.Configuration.Voice3DAudibleRange = voiceBubbleAudibleRange;
                    Plugin.Configuration.Save();

                    PlayingHelper.Update3DFactors(voiceBubbleAudibleRange);
                }
            }

            var voiceChatPlayer = Plugin.Configuration.VoiceChatPlayer;
            if (ImGui.Checkbox("Voice your own Chat", ref voiceChatPlayer))
            {
                Plugin.Configuration.VoiceChatPlayer = voiceChatPlayer;
                Plugin.Configuration.Save();
            }

            var voiceChatSay = Plugin.Configuration.VoiceChatSay;
            if (ImGui.Checkbox("Voice say Chat", ref voiceChatSay))
            {
                Plugin.Configuration.VoiceChatSay = voiceChatSay;
                Plugin.Configuration.Save();
            }

            var voiceChatYell = Plugin.Configuration.VoiceChatYell;
            if (ImGui.Checkbox("Voice yell Chat", ref voiceChatYell))
            {
                Plugin.Configuration.VoiceChatYell = voiceChatYell;
                Plugin.Configuration.Save();
            }

            var voiceChatShout = Plugin.Configuration.VoiceChatShout;
            if (ImGui.Checkbox("Voice shout Chat", ref voiceChatShout))
            {
                Plugin.Configuration.VoiceChatShout = voiceChatShout;
                Plugin.Configuration.Save();
            }

            var voiceChatFreeCompany = Plugin.Configuration.VoiceChatFreeCompany;
            if (ImGui.Checkbox("Voice free company Chat", ref voiceChatFreeCompany))
            {
                Plugin.Configuration.VoiceChatFreeCompany = voiceChatFreeCompany;
                Plugin.Configuration.Save();
            }

            var voiceChatTell = Plugin.Configuration.VoiceChatTell;
            if (ImGui.Checkbox("Voice tell Chat", ref voiceChatTell))
            {
                Plugin.Configuration.VoiceChatTell = voiceChatTell;
                Plugin.Configuration.Save();
            }

            var voiceChatParty = Plugin.Configuration.VoiceChatParty;
            if (ImGui.Checkbox("Voice party Chat", ref voiceChatParty))
            {
                Plugin.Configuration.VoiceChatParty = voiceChatParty;
                Plugin.Configuration.Save();
            }

            var voiceChatAlliance = Plugin.Configuration.VoiceChatAlliance;
            if (ImGui.Checkbox("Voice alliance Chat", ref voiceChatAlliance))
            {
                Plugin.Configuration.VoiceChatAlliance = voiceChatAlliance;
                Plugin.Configuration.Save();
            }

            var voiceChatNoviceNetwork = Plugin.Configuration.VoiceChatNoviceNetwork;
            if (ImGui.Checkbox("Voice novice network Chat", ref voiceChatNoviceNetwork))
            {
                Plugin.Configuration.VoiceChatNoviceNetwork = voiceChatNoviceNetwork;
                Plugin.Configuration.Save();
            }

            var voiceChatLinkshell = Plugin.Configuration.VoiceChatLinkshell;
            if (ImGui.Checkbox("Voice Linkshells", ref voiceChatLinkshell))
            {
                Plugin.Configuration.VoiceChatLinkshell = voiceChatLinkshell;
                Plugin.Configuration.Save();
            }

            var voiceChatCrossLinkshell = Plugin.Configuration.VoiceChatCrossLinkshell;
            if (ImGui.Checkbox("Voice Cross Linkshells", ref voiceChatCrossLinkshell))
            {
                Plugin.Configuration.VoiceChatCrossLinkshell = voiceChatCrossLinkshell;
                Plugin.Configuration.Save();
            }
        }
    }
    #endregion

    #region Voice selection
    private void DrawVoiceSelection()
    {
        try
        {
            using var tabBar = ImRaii.TabBar("Voices##EKVoicesTab");
            if (tabBar)
            {
                using (var tabItemNPCs = ImRaii.TabItem("NPCs"))
                {
                    if (tabItemNPCs)
                    {
                        DrawVoiceSelectionTable("NPCs", Plugin.Configuration!.MappedNpcs, ref filteredNpcs,
                                                ref _updateDataNpcs, ref resetDataNpcs, ref filterGenderNpcs,
                                                ref filterRaceNpcs, ref filterNameNpcs, ref filterVoiceNpcs);
                    }
                }

                using (var tabItemPlayers = ImRaii.TabItem("Players"))
                {
                    if (tabItemPlayers)
                    {
                        DrawVoiceSelectionTable("Players", Plugin.Configuration!.MappedPlayers, ref filteredPlayers,
                                                ref _updateDataNpcs, ref resetDataPlayers, ref filterGenderPlayers,
                                                ref filterRacePlayers, ref filterNamePlayers, ref filterVoicePlayers);
                    }
                }

                using (var tabItemBubbles = ImRaii.TabItem("Bubbles"))
                {
                    if (tabItemBubbles)
                    {
                        DrawVoiceSelectionTable("Bubbles", Plugin.Configuration!.MappedNpcs, ref filteredBubbles,
                                                ref _updateDataNpcs, ref resetDataBubbles, ref filterGenderBubbles,
                                                ref filterRaceBubbles, ref filterNameBubbles, ref filterVoiceBubbles,
                                                true);
                    }
                }

                using (var tabItemVoices = ImRaii.TabItem("Voices"))
                {
                    if (tabItemVoices)
                    {
                        DrawVoices();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawVoices()
    {
        var voiceArr = Plugin.Configuration!.EchokrautVoices.ConvertAll(p => p.ToString()).ToArray();
        var defaultVoiceIndex = Plugin.Configuration.EchokrautVoices.FindIndex(p => p.IsDefault);
        if (ImGui.Combo($"Default Voice:##EKDefaultVoice", ref defaultVoiceIndex, voiceArr, voiceArr.Length))
        {
            // Clear all defaults
            foreach (var voice in Plugin.Configuration.EchokrautVoices)
                voice.IsDefault = false;

            Plugin.Configuration.EchokrautVoices[defaultVoiceIndex].IsDefault = true;
            Plugin.Configuration.Save();
        }

        UpdateDataVoices = filteredVoices.Count == 0;

        if (UpdateDataVoices || (resetDataVoices && (filterGenderVoices.Length == 0 || filterRaceVoices.Length == 0 || filterNameVoices.Length == 0)))
        {
            filteredVoices = Plugin.Configuration.EchokrautVoices;
            UpdateDataVoices = true;
            resetDataVoices = false;
        }

        using var child = ImRaii.Child("VoicesChild");
        if (child)
        {
            using var table = ImRaii.Table("Voice Table##VoiceTable", 9,
                                           ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg |
                                           ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX |
                                           ImGuiTableFlags.ScrollY);
            if (table)
            {
                ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                ImGui.TableSetupColumn("##Play", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
                ImGui.TableSetupColumn("##Stop", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
                ImGui.TableSetupColumn("Use##Enabled", ImGuiTableColumnFlags.WidthFixed, 35);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200);
                ImGui.TableSetupColumn("Note", ImGuiTableColumnFlags.WidthStretch, 300);
                ImGui.TableSetupColumn("Options##Enabled", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Genders", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Races", ImGuiTableColumnFlags.WidthFixed, 320);
                ImGui.TableSetupColumn("Volume", ImGuiTableColumnFlags.WidthFixed, 75);
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
                if (ImGui.InputText($"##EKFilterNpcNote", ref filterNoteVoices, 40) || (filterNoteVoices.Length > 0 && UpdateDataVoices))
                {
                    filteredVoices = filteredVoices.FindAll(p => p.Note.ToLower().Contains(filterNoteVoices.ToLower()));
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
                                filteredVoices.Sort((a, b) => string.CompareOrdinal(a.Note, b.Note));
                            else
                                filteredVoices.Sort((a, b) => string.CompareOrdinal(b.Note, a.Note));
                            break;
                        case 5:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredVoices.Sort((a, b) => string.CompareOrdinal(b.UseAsRandom.ToString(), a.UseAsRandom.ToString()));
                            else
                                filteredVoices.Sort((a, b) => string.CompareOrdinal(a.UseAsRandom.ToString(), b.UseAsRandom.ToString()));
                            break;
                        case 6:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredVoices.Sort((a, b) => string.CompareOrdinal(string.Join( ",", a.AllowedGenders.OrderBy(p => p.ToString()).ToArray()), string.Join( ",", b.AllowedGenders.OrderBy(p => p.ToString()).ToArray())));
                            else
                                filteredVoices.Sort((a, b) => string.CompareOrdinal(string.Join( ",", b.AllowedGenders.OrderBy(p => p.ToString()).ToArray()), string.Join( ",", a.AllowedGenders.OrderBy(p => p.ToString()).ToArray())));
                            break;
                        case 7:
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
                        Plugin.Configuration.Save();
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.TextUnformatted(voice.VoiceName);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKVoiceNote{voice}", ref voice.Note, 80))
                    {
                        Plugin.Configuration.Save();
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    var headerText = voice.UseAsRandom ? "Random - " : "";
                    headerText += voice.IsChildVoice ? "Child - " : "";
                    headerText = headerText.Length >= 3 ? headerText.Substring(0, headerText.Length - 3) : "None";
                    if (ImGui.CollapsingHeader($"{headerText}##EKVoiceOptions{voice}"))
                    {
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        using (var optionTable = ImRaii.Table($"##OptionsTable{voice}", 3))
                        {
                            if (optionTable)
                            {
                                ImGui.TableSetupColumn($"##{voice}options", ImGuiTableColumnFlags.WidthFixed, 120);
                                ImGui.TableNextColumn();
                                var useAsRandom = voice.UseAsRandom;
                                if (ImGui.Checkbox($"Random NPC##EKVoiceUseAsRandom{voice}", ref useAsRandom))
                                {
                                    voice.UseAsRandom = useAsRandom;
                                    Plugin.Configuration.Save();
                                }

                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                var isChildVoice = voice.IsChildVoice;
                                if (ImGui.Checkbox($"Child Voice##EKVoiceIsChildVoice{voice}", ref isChildVoice))
                                {
                                    voice.IsChildVoice = isChildVoice;
                                    Plugin.Configuration.Save();
                                }
                            }
                        }
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    headerText = "";
                    voice.AllowedGenders.OrderBy(p => p.ToString()).ToList().ForEach(p => headerText += $"{p} - ");
                    headerText = headerText.Length >= 3 ? headerText.Substring(0, headerText.Length - 3) : "None";
                    if (ImGui.CollapsingHeader($"{headerText}##EKVoiceAllowedGenders{voice}"))
                    {
                        using (var tableGenders = ImRaii.Table($"##GendersTable{voice}", 1))
                        {
                            if (tableGenders)
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

                                        NpcDataHelper.RefreshSelectables(Plugin.Configuration.EchokrautVoices);
                                        Plugin.Configuration.Save();
                                    }

                                    ImGui.TableNextRow();
                                    ImGui.TableNextColumn();
                                }
                            }
                        }
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
                        using (var tableRaces = ImRaii.Table($"##Racestable{voice}", 3))
                        {
                            if (tableRaces)
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

                                    NpcDataHelper.RefreshSelectables(Plugin.Configuration.EchokrautVoices);
                                    Plugin.Configuration.Save();
                                }

                                ImGui.TableNextColumn();
                                ImGui.TableNextColumn();
                                if (ImGui.Button($"Reset##EKVoiceAllowedRace{voice}Reset"))
                                {
                                    NpcDataHelper.ReSetVoiceRaces(voice);
                                }

                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.TableNextRow(); int i = 0;
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

                                        NpcDataHelper.RefreshSelectables(Plugin.Configuration.EchokrautVoices);
                                        Plugin.Configuration.Save();
                                    }

                                    i++;
                                    if (i == 3)
                                    {
                                        ImGui.TableNextRow();
                                        i = 0;
                                    }
                                }
                            }
                        }
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    var voiceVolume = voice.Volume;
                    if (ImGui.SliderFloat($"##EKVoiceVolumeSlider{voice}", ref voiceVolume, 0f, 2f))
                    {
                        voice.Volume = voiceVolume;
                        Plugin.Configuration.Save();
                    }
                }
            }
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

        using var table = ImRaii.Table($"{dataType} Table##{dataType}Table", 11, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY);
        if (table)
        {
            ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
            ImGui.TableSetupColumn("##Play", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
            ImGui.TableSetupColumn("##Stop", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25);
            ImGui.TableSetupColumn("Lock", ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 35f);
            ImGui.TableSetupColumn("Gender", ImGuiTableColumnFlags.WidthFixed, 125);
            ImGui.TableSetupColumn("Race", ImGuiTableColumnFlags.WidthFixed, 125);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Voice", ImGuiTableColumnFlags.WidthStretch, 250);
            ImGui.TableSetupColumn("Volume", ImGuiTableColumnFlags.WidthFixed, 200f);
            ImGui.TableSetupColumn($"##{dataType}saves", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25f);
            ImGui.TableSetupColumn($"##{dataType}mapping", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 25f);
            ImGui.TableHeadersRow();
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
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
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##testvoice{mapData}", new Vector2(25, 25), "Test Voice", false, true))
                {
                    BackendTestVoice(mapData.Voice);
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Stop.ToIconString()}##stopvoice{mapData}", new Vector2(25, 25), "Stop Voice", false, true))
                {
                    BackendStopVoice();
                }
                ImGui.TableNextColumn();
                var doNotDelete = mapData.DoNotDelete;
                if (ImGui.Checkbox($"##EKNpcDoNotDelete{mapData.ToString()}", ref doNotDelete))
                {
                    mapData.DoNotDelete = doNotDelete;
                    Plugin.Configuration.Save();
                }
                ImGui.TableNextColumn();
                var isEnabled = isBubble ? mapData.IsEnabledBubble : mapData.IsEnabled;
                if (ImGui.Checkbox($"##EKNpcEnabled{mapData.ToString()}", ref isEnabled))
                {
                    if (isBubble)
                        mapData.IsEnabledBubble = isEnabled;
                    else
                        mapData.IsEnabled = isEnabled;
                    Plugin.Configuration.Save();
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
                            Plugin.Configuration.Save();
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
                            Plugin.Configuration.Save();
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
                    var newVoiceItem = Plugin.Configuration!.EchokrautVoices.FindAll(f => f.IsSelectable(mapData.Name, mapData.Gender, mapData.Race, mapData.IsChild))[selectedIndexVoice];

                    mapData.Voice = newVoiceItem;
                    mapData.DoNotDelete = true;
                    mapData.RefreshSelectable();
                    updateData = true;
                    Plugin.Configuration.Save();
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
                    Plugin.Configuration.Save();
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
                        AudioFileHelper.RemoveSavedNpcFiles(Plugin.Configuration.LocalSaveLocation, mapData.Name);
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
                AudioFileHelper.RemoveSavedNpcFiles(Plugin.Configuration.LocalSaveLocation, toBeRemoved.Name);
                realData.Remove(toBeRemoved);
                updateData = true;
                Plugin.Configuration.Save();
            }
        }
    }
    #endregion

    #region Phonetic corrections
    private void DrawPhoneticCorrections()
    {
        try
        {
            if (Plugin.Configuration.PhoneticCorrections.Count == 0)
            {
                Plugin.Configuration.PhoneticCorrections.Add(new PhoneticCorrection("C'ami", "Kami"));
                Plugin.Configuration.Save();
                updatePhonData = true;
            }

            if (filteredPhon == null)
            {
                updatePhonData = true;
            }

            if (updatePhonData || (resetPhonFilter && (filterPhonOriginal.Length == 0 || filterPhonCorrected.Length == 0)))
            {
                filteredPhon = Plugin.Configuration.PhoneticCorrections;
                updatePhonData = true;
                resetPhonFilter = false;
            }
            using var table = ImRaii.Table("Phonetics Table##NPCTable", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY);
            if (table)
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
                        if (!Plugin.Configuration.PhoneticCorrections.Contains(newCorrection))
                        {
                            Plugin.Configuration.PhoneticCorrections.Add(newCorrection);
                            Plugin.Configuration.PhoneticCorrections.Sort();
                            Plugin.Configuration.Save();
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
                        Plugin.Configuration.Save();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##correctText{i}", ref phoneticCorrection.CorrectedText, 25))
                        Plugin.Configuration.Save();

                    i++;
                }

                if (toBeRemoved != null)
                {
                    Plugin.Configuration.PhoneticCorrections.Remove(toBeRemoved);
                    Plugin.Configuration.PhoneticCorrections.Sort();
                    Plugin.Configuration.Save();
                    updatePhonData = true;
                }
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
            using var tabBar = ImRaii.TabBar($"Logs##EKLogsTab");
            if (tabBar)
            {
                using (var tabItemGeneral = ImRaii.TabItem("General"))
                {
                    if (tabItemGeneral)
                    {
                        DrawLogTable("General",
                                     TextSource.None,
                                     Plugin.Configuration.logConfig.GeneralJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowGeneralDebugLog,
                                     Plugin.Configuration.logConfig.ShowGeneralErrorLog,
                                     true,
                                     ref filteredLogsGeneral,
                                     ref UpdateLogGeneralFilter,
                                     ref resetLogGeneralFilter,
                                     ref filterLogsGeneralMethod,
                                     ref filterLogsGeneralMessage,
                                     ref filterLogsGeneralId);
                    }
                }

                using (var tabItemDialogue = ImRaii.TabItem("Dialogue"))
                {
                    if (tabItemDialogue)
                    {
                        DrawLogTable("Dialogue",
                                     TextSource.AddonTalk,
                                     Plugin.Configuration.logConfig.TalkJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowTalkDebugLog,
                                     Plugin.Configuration.logConfig.ShowTalkErrorLog,
                                     Plugin.Configuration.logConfig.ShowTalkId0,
                                     ref filteredLogsTalk,
                                     ref UpdateLogTalkFilter,
                                     ref resetLogTalkFilter,
                                     ref filterLogsTalkMethod,
                                     ref filterLogsTalkMessage,
                                     ref filterLogsTalkId);
                    }
                }

                using (var tabItemBDialogue = ImRaii.TabItem("Battle Dialogue"))
                {
                    if (tabItemBDialogue)
                    {
                        DrawLogTable("BattleDialogue",
                                     TextSource.AddonBattleTalk,
                                     Plugin.Configuration.logConfig.BattleTalkJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowBattleTalkDebugLog,
                                     Plugin.Configuration.logConfig.ShowBattleTalkErrorLog,
                                     Plugin.Configuration.logConfig.ShowBattleTalkId0,
                                     ref filteredLogsBattleTalk,
                                     ref UpdateLogBattleTalkFilter,
                                     ref resetLogBattleTalkFilter,
                                     ref filterLogsBattleTalkMethod,
                                     ref filterLogsBattleTalkMessage,
                                     ref filterLogsBattleTalkId);
                    }
                }

                using (var tabItemChat = ImRaii.TabItem("Chat"))
                {
                    if (tabItemChat)
                    {
                        DrawLogTable("Chat",
                                     TextSource.Chat,
                                     Plugin.Configuration.logConfig.ChatJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowChatDebugLog,
                                     Plugin.Configuration.logConfig.ShowChatErrorLog,
                                     Plugin.Configuration.logConfig.ShowChatId0,
                                     ref filteredLogsChat,
                                     ref UpdateLogChatFilter,
                                     ref resetLogChatFilter,
                                     ref filterLogsChatMethod,
                                     ref filterLogsChatMessage,
                                     ref filterLogsChatId);
                    }
                }

                using (var tabItemBubbles = ImRaii.TabItem("Bubbles"))
                {
                    if (tabItemBubbles)
                    {
                        DrawLogTable("Bubbles",
                                     TextSource.AddonBubble,
                                     Plugin.Configuration.logConfig.BubbleJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowBubbleDebugLog,
                                     Plugin.Configuration.logConfig.ShowBubbleErrorLog,
                                     Plugin.Configuration.logConfig.ShowBubbleId0,
                                     ref filteredLogsBubbles,
                                     ref UpdateLogBubblesFilter,
                                     ref resetLogBubblesFilter,
                                     ref filterLogsBubblesMethod,
                                     ref filterLogsBubblesMessage,
                                     ref filterLogsBubblesId);
                    }
                }

                using (var tabItemCutChoice = ImRaii.TabItem("Player choice in cutscenes"))
                {
                    if (tabItemCutChoice)
                    {
                        DrawLogTable("PlayerChoiceCutscene",
                                     TextSource.AddonCutsceneSelectString,
                                     Plugin.Configuration.logConfig.CutsceneSelectStringJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowCutsceneSelectStringDebugLog,
                                     Plugin.Configuration.logConfig.ShowCutsceneSelectStringErrorLog,
                                     Plugin.Configuration.logConfig.ShowCutsceneSelectStringId0,
                                     ref filteredLogsCutsceneSelectString,
                                     ref UpdateLogCutsceneSelectStringFilter,
                                     ref resetLogCutsceneSelectStringFilter,
                                     ref filterLogsCutsceneSelectStringMethod,
                                     ref filterLogsCutsceneSelectStringMessage,
                                     ref filterLogsCutsceneSelectStringId);
                    }
                }

                using (var tabItemChoice = ImRaii.TabItem("Player choice"))
                {
                    if (tabItemChoice)
                    {
                        DrawLogTable("PlayerChoice",
                                     TextSource.AddonSelectString,
                                     Plugin.Configuration.logConfig.SelectStringJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowSelectStringDebugLog,
                                     Plugin.Configuration.logConfig.ShowSelectStringErrorLog,
                                     Plugin.Configuration.logConfig.ShowSelectStringId0,
                                     ref filteredLogsSelectString,
                                     ref UpdateLogSelectStringFilter,
                                     ref resetLogSelectStringFilter,
                                     ref filterLogsSelectStringMethod,
                                     ref filterLogsSelectStringMessage,
                                     ref filterLogsSelectStringId);
                    }
                }

                using (var tabItemChoice = ImRaii.TabItem("Backend"))
                {
                    if (tabItemChoice)
                    {
                        DrawLogTable("Backend",
                                     TextSource.Backend,
                                     Plugin.Configuration.logConfig.BackendJumpToBottom,
                                     Plugin.Configuration.logConfig.ShowBackendDebugLog,
                                     Plugin.Configuration.logConfig.ShowBackendErrorLog,
                                     Plugin.Configuration.logConfig.ShowBackendId0,
                                     ref FilteredLogsBackend,
                                     ref UpdateLogBackendFilter,
                                     ref ResetLogBackendFilter,
                                     ref FilterLogsBackendMethod,
                                     ref FilterLogsBackendMessage,
                                     ref FilterLogsBackendId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.None), false);
        }
    }

    internal void DrawLogTableSettings(TextSource textSource, bool configJumpToBottom, bool configShowDebugLog, bool configShowErrorLog, bool configShowId0, ref bool updateLogs){
        if (ImGui.CollapsingHeader("Options:"))
        {
            using (ImRaii.Disabled(LogHelper.Updating))
            {
                if (ImGui.Checkbox("Show debug logs", ref configShowDebugLog))
                {
                    switch (textSource)
                    {
                        case TextSource.None:
                            Plugin.Configuration.logConfig.ShowGeneralDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonBubble:
                            Plugin.Configuration.logConfig.ShowBubbleDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonTalk:
                            Plugin.Configuration.logConfig.ShowTalkDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonBattleTalk:
                            Plugin.Configuration.logConfig.ShowBattleTalkDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonSelectString:
                            Plugin.Configuration.logConfig.ShowSelectStringDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            Plugin.Configuration.logConfig.ShowCutsceneSelectStringDebugLog = configShowDebugLog;
                            break;
                        case TextSource.Chat:
                            Plugin.Configuration.logConfig.ShowChatDebugLog = configShowDebugLog;
                            break;
                        case TextSource.Backend:
                            Plugin.Configuration.logConfig.ShowBackendDebugLog = configShowDebugLog;
                            break;
                    }

                    Plugin.Configuration.Save();
                    updateLogs = true;
                }

                if (ImGui.Checkbox("Show error logs", ref configShowErrorLog))
                {
                    switch (textSource)
                    {
                        case TextSource.None:
                            Plugin.Configuration.logConfig.ShowGeneralErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonBubble:
                            Plugin.Configuration.logConfig.ShowBubbleErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonTalk:
                            Plugin.Configuration.logConfig.ShowTalkErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonBattleTalk:
                            Plugin.Configuration.logConfig.ShowBattleTalkErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonSelectString:
                            Plugin.Configuration.logConfig.ShowSelectStringErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            Plugin.Configuration.logConfig.ShowCutsceneSelectStringErrorLog = configShowErrorLog;
                            break;
                        case TextSource.Chat:
                            Plugin.Configuration.logConfig.ShowChatErrorLog = configShowErrorLog;
                            break;
                        case TextSource.Backend:
                            Plugin.Configuration.logConfig.ShowBackendErrorLog = configShowErrorLog;
                            break;
                    }

                    Plugin.Configuration.Save();
                    updateLogs = true;
                }

                if (ImGui.Checkbox("Show ID: 0", ref configShowId0))
                {
                    switch (textSource)
                    {
                        case TextSource.AddonBubble:
                            Plugin.Configuration.logConfig.ShowBubbleId0 = configShowId0;
                            break;
                        case TextSource.AddonTalk:
                            Plugin.Configuration.logConfig.ShowTalkId0 = configShowId0;
                            break;
                        case TextSource.AddonBattleTalk:
                            Plugin.Configuration.logConfig.ShowBattleTalkId0 = configShowId0;
                            break;
                        case TextSource.AddonSelectString:
                            Plugin.Configuration.logConfig.ShowSelectStringId0 = configShowId0;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            Plugin.Configuration.logConfig.ShowCutsceneSelectStringId0 = configShowId0;
                            break;
                        case TextSource.Chat:
                            Plugin.Configuration.logConfig.ShowChatId0 = configShowId0;
                            break;
                        case TextSource.Backend:
                            Plugin.Configuration.logConfig.ShowBackendId0 = configShowId0;
                            break;
                    }

                    Plugin.Configuration.Save();
                    updateLogs = true;
                }

                if (ImGui.Checkbox("Always jump to bottom", ref configJumpToBottom))
                {
                    switch (textSource)
                    {
                        case TextSource.None:
                            Plugin.Configuration.logConfig.GeneralJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonBubble:
                            Plugin.Configuration.logConfig.BubbleJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonTalk:
                            Plugin.Configuration.logConfig.TalkJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonBattleTalk:
                            Plugin.Configuration.logConfig.BattleTalkJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonSelectString:
                            Plugin.Configuration.logConfig.SelectStringJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            Plugin.Configuration.logConfig.CutsceneSelectStringJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.Chat:
                            Plugin.Configuration.logConfig.ChatJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.Backend:
                            Plugin.Configuration.logConfig.BackendJumpToBottom = configJumpToBottom;
                            break;
                    }

                    Plugin.Configuration.Save();
                }
            }
        }
    }

    internal void DrawLogTable(string logType, TextSource source, bool jumpToBottom, bool showDebugLog, bool showErrorLog, bool showId0, ref List<LogMessage> filteredLogs, ref bool updateLogs, ref bool resetLogs, ref string filterMethod, ref string filterMessage, ref string filterId)
    {
        var newData = false;
        if (ImGui.CollapsingHeader("Log:"))
        {
            DrawLogTableSettings(source, jumpToBottom, showDebugLog, showErrorLog, showId0, ref updateLogs);
            if (filteredLogs.Count == 0)
                updateLogs = true;

            if (updateLogs || (resetLogs && (filterMethod.Length == 0 || filterMessage.Length == 0 || filterId.Length == 0)))
            {
                if (!LogHelper.Updating)
                {
                    filteredLogs = LogHelper.RecreateLogList(source);
                    updateLogs = true;
                    resetLogs = false;
                    newData = true;
                }
            }

            using (var table = ImRaii.Table($"Log Table##{logType}LogTable", 4,
                                            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg |
                                            ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
            {
                if (table)
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
                    if (ImGui.InputText($"##EKFilter{logType}LogMethod", ref filterMethod, 40) ||
                        (filterMethod.Length > 0 && updateLogs))
                    {
                        var method = filterMethod;
                        filteredLogs = filteredLogs.FindAll(p => p.method.ToLower().Contains(method.ToLower()));
                        updateLogs = true;
                        resetLogs = true;
                        newData = true;
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilter{logType}LogMessage", ref filterMessage, 80) ||
                        (filterMessage.Length > 0 && updateLogs))
                    {
                        var message = filterMessage;
                        filteredLogs = filteredLogs.FindAll(p => p.message.ToLower().Contains(message.ToLower()));
                        updateLogs = true;
                        resetLogs = true;
                        newData = true;
                    }

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKFilter{logType}LogId", ref filterId, 40) ||
                        (filterId.Length > 0 && updateLogs))
                    {
                        var id = filterId;
                        filteredLogs =
                            filteredLogs.FindAll(p => p.eventId.Id.ToString().ToLower().Contains(id.ToLower()));
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
                                    filteredLogs.Sort((a, b) => String.CompareOrdinal(a.method, b.method));
                                else
                                    filteredLogs.Sort((a, b) => String.CompareOrdinal(b.method, a.method));
                                break;
                            case 2:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredLogs.Sort((a, b) => String.CompareOrdinal(a.message, b.message));
                                else
                                    filteredLogs.Sort((a, b) => String.CompareOrdinal(b.message, a.message));
                                break;
                            case 3:
                                if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                    filteredLogs.Sort(
                                        (a, b) => String.CompareOrdinal(a.eventId.Id.ToString(), b.eventId.Id.ToString()));
                                else
                                    filteredLogs.Sort(
                                        (a, b) => String.CompareOrdinal(b.eventId.Id.ToString(), a.eventId.Id.ToString()));
                                break;
                        }

                        if (!LogHelper.Updating)
                            updateLogs = false;
                        sortSpecs.SpecsDirty = false;
                    }

                    foreach (var logMessage in filteredLogs)
                    {
                        ImGui.TableNextRow();
                        using (ImRaii.PushColor(ImGuiCol.Text, logMessage.color))
                        {
                            using (ImRaii.TextWrapPos(0))
                            {
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(logMessage.timeStamp.ToString("HH:mm:ss.fff"));
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(logMessage.method);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(logMessage.message);
                                ImGui.TableNextColumn();
                                ImGui.TextUnformatted(logMessage.eventId.Id.ToString());
                            }
                        }
                    }

                    if (jumpToBottom && newData)
                        ImGui.SetScrollHereY();
                }
            }
        }
    }
    #endregion

    #region Helper Functions

    private async void BackendTestVoice(EchokrautVoice voice)
    {
        BackendStopVoice();
        var eventId = LogHelper.Start(MethodBase.GetCurrentMethod().Name, TextSource.AddonTalk);
        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Testing voice: {voice.ToString()}", eventId);
        // Say the thing
        var volume = VolumeHelper.GetVoiceVolume(eventId) * voice.Volume;
        var voiceMessage = new VoiceMessage
        {
            SpeakerObj = null,
            Source = TextSource.VoiceTest,
            Speaker = new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None)
            {
                Gender = voice.AllowedGenders.Count > 0 ? voice.AllowedGenders[0] : Genders.Male,
                Race = voice.AllowedRaces.Count > 0 ? voice.AllowedRaces[0] : NpcRaces.Hyur,
                Name = voice.VoiceName,
                Voice = voice
            },
            Text = GetTestMessageText(Plugin.ClientState.ClientLanguage),
            Language = Plugin.ClientState.ClientLanguage,
            EventId = eventId,
            SpeakerFollowObj = DalamudHelper.LocalPlayer,
            Volume = volume
        };


        if (volume > 0)
            BackendHelper.OnSay(voiceMessage) ;
        else
        {
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping voice inference. Volume is 0", eventId);
            LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
        }
    }

    private string GetTestMessageText(ClientLanguage clientLanguage)
    {
        switch (clientLanguage)
        {
            case ClientLanguage.English:
                return Constants.TESTMESSAGEEN;
            case ClientLanguage.French:
                return Constants.TESTMESSAGEFR;
            case ClientLanguage.German:
                return Constants.TESTMESSAGEDE;
            case ClientLanguage.Japanese:
                return Constants.TESTMESSAGEJP;
        }

        return Constants.TESTMESSAGEEN;
    }

    private void BackendStopVoice()
    {
        BackendHelper.OnCancel(DialogExtraOptionsWindow.CurrentVoiceMessage);
        LogHelper.End(MethodBase.GetCurrentMethod().Name, new EKEventId(0, TextSource.AddonTalk));
    }

    private void ReloadRemoteMappings()
    {
        JsonLoaderHelper.Initialize(Plugin.ClientState.ClientLanguage);
    }
    #endregion

    public static void DrawExternalLinkButtons(Vector2 size, Vector2 offset)
    {
        var cursorPos = ImGui.GetCursorPos();
        ImGui.SetCursorPosX(size.X + offset.X - 105);
        ImGui.SetCursorPosY(offset.Y + 30);
        using (ImRaii.PushColor(ImGuiCol.Button, Constants.DISCORDCOLOR))
        {
            if (ImGui.Button($"Join discord server##EKDiscordLink"))
                CMDHelper.OpenUrl(Constants.DISCORDURL);
        }

        ImGui.SetCursorPosX(size.X + offset.X - 75);
        ImGui.SetCursorPosY(offset.Y + 60);
        if (ImGui.Button($"Alltalk Github##EKAlltalkGithub"))
            CMDHelper.OpenUrl(Constants.ALLTALKGITHUBURL);
        ImGui.SetCursorPos(cursorPos);
    }
}
