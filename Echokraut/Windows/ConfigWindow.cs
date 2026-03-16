using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using OtterGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly ILogService _log;
    private readonly IVolumeService _volumeService;
    private readonly Configuration _config;
    private readonly IFramework _framework;
    private readonly ICommandService _commands;
    private readonly ICommandManager _commandManager;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IBackendService _backend;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IClientState _clientState;
    private readonly IJsonDataService _jsonData;
    private readonly IAudioFileService _audioFiles;
    private readonly IGameObjectService _gameObjects;
    private readonly IGoogleDriveSyncService _googleDrive;
    private readonly INpcDataService _npcData;
    private readonly AlltalkInstanceWindow _alttalkInstanceWindow;
    private readonly FileDialogManager? fileDialogManager;
    private unsafe Camera* camera;
    #region Voice Selection
    private List<NpcMapData> filteredNpcs = [];
    private bool _updateDataNpcs;
    private bool resetDataNpcs;
    private string filterGenderNpcs = "";
    private string filterRaceNpcs = "";
    private string filterNameNpcs = "";
    private string filterVoiceNpcs = "";
    private List<NpcMapData> filteredPlayers = [];
    private bool _updateDataPlayers;
    private bool resetDataPlayers;
    private string filterGenderPlayers = "";
    private string filterRacePlayers = "";
    private string filterNamePlayers = "";
    private string filterVoicePlayers = "";
    private List<NpcMapData> filteredBubbles = [];
    private bool _updateDataBubbles;
    private bool resetDataBubbles;
    private string filterGenderBubbles = "";
    private string filterRaceBubbles = "";
    private string filterNameBubbles = "";
    private string filterVoiceBubbles = "";
    private List<EchokrautVoice> filteredVoices = [];
    private bool _updateDataVoices;
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
    private bool _updateLogGeneralFilter = true;
    private bool resetLogGeneralFilter = true;
    private List<LogMessage> filteredLogsTalk = [];
    private string filterLogsTalkMethod = "";
    private string filterLogsTalkMessage = "";
    private string filterLogsTalkId = "";
    private bool _updateLogTalkFilter = true;
    private bool resetLogTalkFilter = true;
    private List<LogMessage> filteredLogsBattleTalk = [];
    private string filterLogsBattleTalkMethod = "";
    private string filterLogsBattleTalkMessage = "";
    private string filterLogsBattleTalkId = "";
    private bool _updateLogBattleTalkFilter = true;
    private bool resetLogBattleTalkFilter = true;
    private List<LogMessage> filteredLogsBubbles = [];
    private string filterLogsBubblesMethod = "";
    private string filterLogsBubblesMessage = "";
    private string filterLogsBubblesId = "";
    private bool _updateLogBubblesFilter = true;
    private bool resetLogBubblesFilter = true;
    private List<LogMessage> filteredLogsChat = [];
    private string filterLogsChatMethod = "";
    private string filterLogsChatMessage = "";
    private string filterLogsChatId = "";
    private bool _updateLogChatFilter = true;
    private bool resetLogChatFilter = true;
    private List<LogMessage> filteredLogsCutsceneSelectString = [];
    private string filterLogsCutsceneSelectStringMethod = "";
    private string filterLogsCutsceneSelectStringMessage = "";
    private string filterLogsCutsceneSelectStringId = "";
    private bool _updateLogCutsceneSelectStringFilter = true;
    private bool resetLogCutsceneSelectStringFilter = true;
    private List<LogMessage> filteredLogsSelectString = [];
    private string filterLogsSelectStringMethod = "";
    private string filterLogsSelectStringMessage = "";
    private string filterLogsSelectStringId = "";
    private bool _updateLogSelectStringFilter = true;
    private bool resetLogSelectStringFilter = true;
    private List<LogMessage> _filteredLogsBackend = [];
    private string _filterLogsBackendMethod = "";
    private string _filterLogsBackendMessage = "";
    private string _filterLogsBackendId = "";
    private bool _updateLogBackendFilter = true;
    private bool _resetLogBackendFilter = true;
    #endregion
    #region Phonetic Corrections
    private List<PhoneticCorrection>? filteredPhon = [];
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
    public ConfigWindow(
        ILogService log,
        IVolumeService volumeService,
        Configuration config,
        IFramework framework,
        ICommandService commands,
        ICommandManager commandManager,
        IDalamudPluginInterface pluginInterface,
        IBackendService backend,
        IAudioPlaybackService audioPlayback,
        IClientState clientState,
        IJsonDataService jsonData,
        IAudioFileService audioFiles,
        IGameObjectService gameObjects,
        IGoogleDriveSyncService googleDrive,
        INpcDataService npcData,
        AlltalkInstanceWindow alttalkInstanceWindow) : base($"Echokraut Configuration###EKSettings")
    {
        _log = log;
        _volumeService = volumeService;
        _config = config;
        _framework = framework;
        _commands = commands;
        _commandManager = commandManager;
        _pluginInterface = pluginInterface;
        _backend = backend;
        _audioPlayback = audioPlayback;
        _clientState = clientState;
        _jsonData = jsonData;
        _audioFiles = audioFiles;
        _gameObjects = gameObjects;
        _googleDrive = googleDrive;
        _npcData = npcData;
        _alttalkInstanceWindow = alttalkInstanceWindow;
        fileDialogManager = new FileDialogManager();

        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar & ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        _log.LogUpdated += OnLogUpdated;
        _backend.VoicesMapped += OnVoicesMapped;
        _backend.CharacterMapped += OnCharacterMapped;
    }

    public void Dispose()
    {
        _log.LogUpdated -= OnLogUpdated;
        _backend.VoicesMapped -= OnVoicesMapped;
        _backend.CharacterMapped -= OnCharacterMapped;
    }

    private void OnVoicesMapped() => _updateDataVoices = true;

    private void OnCharacterMapped()
    {
        _updateDataNpcs = true;
        _updateDataBubbles = true;
        _updateDataPlayers = true;
    }

    private void OnLogUpdated(TextSource source)
    {
        switch (source)
        {
            case TextSource.None:                      _updateLogGeneralFilter = true; break;
            case TextSource.Chat:                      _updateLogChatFilter = true; break;
            case TextSource.AddonTalk:                 _updateLogTalkFilter = true; break;
            case TextSource.AddonBattleTalk:           _updateLogBattleTalkFilter = true; break;
            case TextSource.AddonSelectString:         _updateLogSelectStringFilter = true; break;
            case TextSource.AddonCutsceneSelectString: _updateLogCutsceneSelectStringFilter = true; break;
            case TextSource.AddonBubble:               _updateLogBubblesFilter = true; break;
            case TextSource.Backend:                   _updateLogBackendFilter = true; break;
        }
    }

    private List<LogMessage> RecreateLogList(TextSource textSource)
    {
        var logListFiltered = new List<LogMessage>(_log.GetLogsForSource(textSource));

        var showDebug = false;
        var showError = false;
        var showId0 = true;
        switch (textSource)
        {
            case TextSource.None:
                showDebug = _config.logConfig.ShowGeneralDebugLog;
                showError = _config.logConfig.ShowGeneralErrorLog;
                showId0 = true;
                break;
            case TextSource.Chat:
                showDebug = _config.logConfig.ShowChatDebugLog;
                showError = _config.logConfig.ShowChatErrorLog;
                showId0 = _config.logConfig.ShowChatId0;
                break;
            case TextSource.AddonTalk:
                showDebug = _config.logConfig.ShowTalkDebugLog;
                showError = _config.logConfig.ShowTalkErrorLog;
                showId0 = _config.logConfig.ShowTalkId0;
                break;
            case TextSource.AddonBattleTalk:
                showDebug = _config.logConfig.ShowBattleTalkDebugLog;
                showError = _config.logConfig.ShowBattleTalkErrorLog;
                showId0 = _config.logConfig.ShowBattleTalkId0;
                break;
            case TextSource.AddonSelectString:
                showDebug = _config.logConfig.ShowSelectStringDebugLog;
                showError = _config.logConfig.ShowSelectStringErrorLog;
                showId0 = _config.logConfig.ShowSelectStringId0;
                break;
            case TextSource.AddonCutsceneSelectString:
                showDebug = _config.logConfig.ShowCutsceneSelectStringDebugLog;
                showError = _config.logConfig.ShowCutsceneSelectStringErrorLog;
                showId0 = _config.logConfig.ShowCutsceneSelectStringId0;
                break;
            case TextSource.AddonBubble:
                showDebug = _config.logConfig.ShowBubbleDebugLog;
                showError = _config.logConfig.ShowBubbleErrorLog;
                showId0 = _config.logConfig.ShowBubbleId0;
                break;
            case TextSource.Backend:
                showDebug = _config.logConfig.ShowBackendDebugLog;
                showError = _config.logConfig.ShowBackendErrorLog;
                showId0 = _config.logConfig.ShowBackendId0;
                break;
        }

        if (!showDebug) logListFiltered.RemoveAll(p => p.type == LogType.Debug);
        if (!showError) logListFiltered.RemoveAll(p => p.type == LogType.Error);
        if (!showId0)   logListFiltered.RemoveAll(p => p.eventId.Id == 0);

        logListFiltered.Sort((p, q) => p.timeStamp.CompareTo(q.timeStamp));
        return logListFiltered;
    }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (_config.IsConfigWindowMovable)
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
            _framework.RunOnFrameworkThread(() => { _log.UpdateMainThreadLogs(); });
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
            _log.Error(nameof(Draw), $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
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
                                             _config.MappedNpcs.FindAll(p => !p.Name.StartsWith("BB") &&
                                                 !p.DoNotDelete))
                                    {
                                        _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation,
                                                                            npcMapData.Name);
                                        _config.MappedNpcs.Remove(npcMapData);
                                    }

                                    _updateDataNpcs = true;
                                    _config.Save();
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
                                             _config.MappedPlayers.FindAll(p => !p.DoNotDelete))
                                    {
                                        _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation,
                                                                            playerMapData.Name);
                                        _config.MappedPlayers.Remove(playerMapData);
                                    }

                                    _updateDataPlayers = true;
                                    _config.Save();
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
                                             _config.MappedNpcs.FindAll(p => p.Name.StartsWith("BB") &&
                                                 !p.DoNotDelete))
                                    {
                                        _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation,
                                                                            npcMapData.Name);
                                        _config.MappedNpcs.Remove(npcMapData);
                                    }

                                    _updateDataBubbles = true;
                                    _config.Save();
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
                        foreach (var commandKey in _commands.CommandKeys)
                        {
                            var command = _commandManager.Commands[commandKey];
                            ImGui.TextUnformatted(commandKey);
                            ImGui.SameLine();
                            ImGui.TextUnformatted(command.HelpMessage);
                        }
                    }
                }

                using (ImRaii.Disabled(!_config.Enabled))
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
            _log.Error(nameof(DrawSettings), $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawGeneralSettings()
    {
        var enabled = _config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            _config.Enabled = enabled;
            _config.Save();
        }

        using (ImRaii.Disabled(!enabled))
        {
            var generateBySentence = _config.GenerateBySentence;
            if (ImGui.Checkbox("Generate text per sentence instead of all at once(shorter sentences may sound weird, only needed if using CPU for inference)", ref generateBySentence))
            {
                _config.GenerateBySentence = generateBySentence;
                _config.Save();
            }
            
            var removeStutters = _config.RemoveStutters;
            if (ImGui.Checkbox("Remove stutters", ref removeStutters))
            {
                _config.RemoveStutters = removeStutters;
                _config.Save();
            }

            var hideUiInCutscenes = _config.HideUiInCutscenes;
            if (ImGui.Checkbox("Hide UI in Cutscenes", ref hideUiInCutscenes))
            {
                _config.HideUiInCutscenes = hideUiInCutscenes;
                _config.Save();
                _pluginInterface.UiBuilder.DisableCutsceneUiHide = !hideUiInCutscenes;
            }
            ImGui.NewLine();
            if (ImGui.CollapsingHeader("Experimental options:"))
            {
                var showExtraOptionsInDialogue = _config.ShowExtraOptionsInDialogue;
                if (ImGui.Checkbox("Shows Pause/Resume, Stop/Play and Mute buttons below the text in dialogues",
                                   ref showExtraOptionsInDialogue))
                {
                    _config.ShowExtraOptionsInDialogue = showExtraOptionsInDialogue;
                    _config.Save();
                }

                using (ImRaii.Disabled(!showExtraOptionsInDialogue))
                {
                    var showExtraExtraOptionsInDialogue = _config.ShowExtraExtraOptionsInDialogue;
                    if (ImGui.Checkbox("Shows even more options below the text in dialogues",
                                       ref showExtraExtraOptionsInDialogue))
                    {
                        _config.ShowExtraExtraOptionsInDialogue = showExtraExtraOptionsInDialogue;
                        _config.Save();
                    }
                }
            
                var removePunctuation = _config.RemovePunctuation;
                if (ImGui.Checkbox("Remove punctuation from the text (Experimental – may reduce end-of-speech hallucinations)", ref removePunctuation))
                {
                    _config.RemovePunctuation = removePunctuation;
                    _config.Save();
                }
            }
        }
    }

    private void DrawDialogueSettings()
    {
        var voiceDialog = _config.VoiceDialogue;
        if (ImGui.Checkbox("Voice dialog", ref voiceDialog))
        {
            _config.VoiceDialogue = voiceDialog;
            _config.Save();
        }

        using (ImRaii.Disabled(!voiceDialog))
        {
            var voiceDialogueIn3D = _config.VoiceDialogueIn3D;
            if (ImGui.Checkbox("Voice dialogue in 3D Space", ref voiceDialogueIn3D))
            {
                _config.VoiceDialogueIn3D = voiceDialogueIn3D;
                _config.Save();
            }

            if (voiceDialogueIn3D)
            {
                var voiceBubbleAudibleRange = _config.Voice3DAudibleRange;
                if (ImGui.SliderFloat("3D Space audible dropoff (shared setting), higher = lesser range, 0 = on player", ref voiceBubbleAudibleRange, 0f, 1f))
                {
                    _config.Voice3DAudibleRange = voiceBubbleAudibleRange;
                    _config.Save();

                    _audioPlayback.Update3DFactors(voiceBubbleAudibleRange);
                }
            }
        }

        var voicePlayerChoicesCutscene = _config.VoicePlayerChoicesCutscene;
        if (ImGui.Checkbox("Voice player choices in cutscene", ref voicePlayerChoicesCutscene))
        {
            _config.VoicePlayerChoicesCutscene = voicePlayerChoicesCutscene;
            _config.Save();
        }

        var voicePlayerChoices = _config.VoicePlayerChoices;
        if (ImGui.Checkbox("Voice player choices outside of cutscene", ref voicePlayerChoices))
        {
            _config.VoicePlayerChoices = voicePlayerChoices;
            _config.Save();
        }

        var cancelAdvance = _config.CancelSpeechOnTextAdvance;
        if (ImGui.Checkbox("Cancel voice on text advance", ref cancelAdvance))
        {
            _config.CancelSpeechOnTextAdvance = cancelAdvance;
            _config.Save();
        }

        var autoAdvanceOnSpeechCompletion = _config.AutoAdvanceTextAfterSpeechCompleted;
        if (ImGui.Checkbox("Click dialogue window after speech completion", ref autoAdvanceOnSpeechCompletion))
        {
            _config.AutoAdvanceTextAfterSpeechCompleted = autoAdvanceOnSpeechCompletion;
            _config.Save();
        }

        var voiceRetainers = _config.VoiceRetainers;
        if (ImGui.Checkbox("Voice retainer dialogues", ref voiceRetainers))
        {
            _config.VoiceRetainers = voiceRetainers;
            _config.Save();
        }
    }

    private void DrawBattleDialogueSettings()
    {
        var voiceBattleDialog = _config.VoiceBattleDialogue;
        if (ImGui.Checkbox("Voice battle dialog", ref voiceBattleDialog))
        {
            _config.VoiceBattleDialogue = voiceBattleDialog;
            _config.Save();
        }

        using (ImRaii.Disabled(!voiceBattleDialog))
        {
            var voiceBattleDialogQueued = _config.VoiceBattleDialogQueued;
            if (ImGui.Checkbox("Voice battle dialog in a queue", ref voiceBattleDialogQueued))
            {
                _config.VoiceBattleDialogQueued = voiceBattleDialogQueued;
                _config.Save();
            }
        }
    }

    private void DrawBackendSettings()
    {
        var backends = Enum.GetValues<TTSBackends>().ToArray();
        var backendsDisplay = backends.Select(b => b.ToString()).ToArray();
        var presetIndex = Enum.GetValues<TTSBackends>().ToList().IndexOf(_config.BackendSelection);
        if (ImGui.Combo($"Select Backend##EKCBoxBackend", ref presetIndex, backendsDisplay, backendsDisplay.Length))
        {
            var backendSelection = backends[presetIndex];
            _config.BackendSelection = backendSelection;
            _config.Save();
            _backend.SetBackendType(backendSelection);

            _log.Info(nameof(DrawBackendSettings), $"Updated backendselection to: {Constants.BACKENDS[presetIndex]}", new EKEventId(0, TextSource.None));
        }

        if (_config.BackendSelection == TTSBackends.Alltalk)
            _alttalkInstanceWindow.DrawAlltalk(false);
    }

    private void DrawSaveSettings()
    {
        var loadLocalFirst = _config.LoadFromLocalFirst;
        if (ImGui.Checkbox("Search audio locally first before generating", ref loadLocalFirst))
        {
            _config.LoadFromLocalFirst = loadLocalFirst;
            _config.Save();
        }
        var saveLocally = _config.SaveToLocal;
        if (ImGui.Checkbox("Save generated audio locally", ref saveLocally))
        {
            _config.SaveToLocal = saveLocally;
            _config.Save();
        }

        using (ImRaii.Disabled(!saveLocally))
        {
            var createMissingLocalSave = _config.CreateMissingLocalSaveLocation;
            if (ImGui.Checkbox("Create directory if not existing", ref createMissingLocalSave))
            {
                _config.CreateMissingLocalSaveLocation = createMissingLocalSave;
                _config.Save();
            }
        }

        using (ImRaii.Disabled(!saveLocally && !loadLocalFirst))
        {
            var localSaveLocation = _config.LocalSaveLocation;
            if (ImGui.InputText($"##EKSavePath", ref localSaveLocation, 40))
            {
                _config.LocalSaveLocation = localSaveLocation;
                _config.Save();
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##import", new Vector2(25, 25),
                    "Select a directory via dialog.", false, true))
            {
                var startDir = _config.LocalSaveLocation.Length > 0 && Directory.Exists(_config.LocalSaveLocation)
                ? _config.LocalSaveLocation
                    : null;

                _log.Debug(nameof(DrawSaveSettings), $"Connection test result: {startDir}", new EKEventId(0, TextSource.None));
                fileDialogManager!.OpenFolderDialog("Choose audio files directory", (b, s) =>
                {
                    if (!b)
                        return;

                    _config.LocalSaveLocation = s;
                    _config.Save();
                }, startDir);
            }

            fileDialogManager!.Draw();
        }

        ImGui.NewLine();

        if (ImGui.CollapsingHeader("Google Drive:"))
        {
            var googleDriveRequestVoiceLine = _config.GoogleDriveRequestVoiceLine;
            ImGui.LabelText("", "This setting may tremendously help in the future. Please consider helping out.");
            if (ImGui.Checkbox("Send any dialogue line to my (Ren Nagasaki's) Share for a full database of needed voice lines.", ref googleDriveRequestVoiceLine))
            {
                _config.GoogleDriveRequestVoiceLine = googleDriveRequestVoiceLine;
                _config.Save();

                if (googleDriveRequestVoiceLine)
                    _ = _googleDrive.CreateDriveServicePkceAsync();
            }
            ImGui.NewLine();
            
            using (ImRaii.Disabled(!saveLocally))
            {
                var googleDriveUpload = _config.GoogleDriveUpload;
                if (ImGui.Checkbox("Upload to Google Drive (requires 'Save generated audio locally')", ref googleDriveUpload))
                {
                    _config.GoogleDriveUpload = googleDriveUpload;
                    _config.Save();
                }
            }

            var googleDriveDownload = _config.GoogleDriveDownload;
            if (ImGui.Checkbox("Download from Google Drive Share", ref googleDriveDownload))
            {
                _config.GoogleDriveDownload = googleDriveDownload;
                _config.Save();
            }

            using (ImRaii.Disabled(!googleDriveDownload))
            {
                var googleDriveDownloadPeriodically = _config.GoogleDriveDownloadPeriodically;
                if (ImGui.Checkbox("Download periodically (every 60 minutes, only updating/downloading new files)",
                                   ref googleDriveDownloadPeriodically))
                {
                    _config.GoogleDriveDownloadPeriodically = googleDriveDownloadPeriodically;
                    _config.Save();
                }

                ImGui.LabelText("", "Google Drive share link");
                var googleDriveShareLink = _config.GoogleDriveShareLink;
                if (ImGui.InputText($"##EKGDShareLink", ref googleDriveShareLink, 100))
                {
                    _config.GoogleDriveShareLink = googleDriveShareLink;
                    _config.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button("Download now##EKGDDownloadNow"))
                {
                    _googleDrive.DownloadFolder(_config.LocalSaveLocation, _config.GoogleDriveShareLink);
                }
            }
        }
    }

    private unsafe void DrawBubbleSettings()
    {
        var voiceBubbles = _config.VoiceBubble;
        if (ImGui.Checkbox("Voice NPC Bubbles", ref voiceBubbles))
        {
            _config.VoiceBubble = voiceBubbles;
            _config.Save();
        }

        using (ImRaii.Disabled(!voiceBubbles))
        {
            var voiceBubblesInCity = _config.VoiceBubblesInCity;
            if (ImGui.Checkbox("Voice NPC Bubbles in City", ref voiceBubblesInCity))
            {
                _config.VoiceBubblesInCity = voiceBubblesInCity;
                _config.Save();
            }

            var voiceSourceCam = _config.VoiceSourceCam;
            if (ImGui.Checkbox("Voice Bubbles with camera as center", ref voiceSourceCam))
            {
                _config.VoiceSourceCam = voiceSourceCam;
                _config.Save();
            }

            var voiceBubbleAudibleRange = _config.Voice3DAudibleRange;
            if (ImGui.SliderFloat("3D Space audible dropoff (shared setting), higher = lesser range, 0 = on player", ref voiceBubbleAudibleRange, 0f, 1f))
            {
                _config.Voice3DAudibleRange = voiceBubbleAudibleRange;
                _config.Save();

                _audioPlayback.Update3DFactors(voiceBubbleAudibleRange);
            }


            if (camera == null && CameraManager.Instance() != null)
                camera = CameraManager.Instance()->GetActiveCamera();

            var position = _gameObjects.LocalPlayer?.Position ?? System.Numerics.Vector3.Zero;
            if (_config.VoiceSourceCam)
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
        var voiceChat = _config.VoiceChat;
        if (ImGui.Checkbox("Voice Chat", ref voiceChat))
        {
            _config.VoiceChat = voiceChat;
            _config.Save();
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
            var voiceChatLanguageApiKey = _config.VoiceChatLanguageAPIKey;
            if (ImGui.InputText("Detect Language API Key", ref voiceChatLanguageApiKey, 32))
            {
                _config.VoiceChatLanguageAPIKey = voiceChatLanguageApiKey;
                _config.Save();
            }

            var voiceChatIn3D = _config.VoiceChatIn3D;
            if (ImGui.Checkbox("Voice Chat in 3D Space", ref voiceChatIn3D))
            {
                _config.VoiceChatIn3D = voiceChatIn3D;
                _config.Save();
            }

            if (voiceChatIn3D)
            {
                var voiceBubbleAudibleRange = _config.Voice3DAudibleRange;
                if (ImGui.SliderFloat("3D Space audible dropoff (shared setting), higher = lesser range, 0 = on player", ref voiceBubbleAudibleRange, 0f, 1f))
                {
                    _config.Voice3DAudibleRange = voiceBubbleAudibleRange;
                    _config.Save();

                    _audioPlayback.Update3DFactors(voiceBubbleAudibleRange);
                }
            }

            var voiceChatPlayer = _config.VoiceChatPlayer;
            if (ImGui.Checkbox("Voice your own Chat", ref voiceChatPlayer))
            {
                _config.VoiceChatPlayer = voiceChatPlayer;
                _config.Save();
            }

            var voiceChatSay = _config.VoiceChatSay;
            if (ImGui.Checkbox("Voice say Chat", ref voiceChatSay))
            {
                _config.VoiceChatSay = voiceChatSay;
                _config.Save();
            }

            var voiceChatYell = _config.VoiceChatYell;
            if (ImGui.Checkbox("Voice yell Chat", ref voiceChatYell))
            {
                _config.VoiceChatYell = voiceChatYell;
                _config.Save();
            }

            var voiceChatShout = _config.VoiceChatShout;
            if (ImGui.Checkbox("Voice shout Chat", ref voiceChatShout))
            {
                _config.VoiceChatShout = voiceChatShout;
                _config.Save();
            }

            var voiceChatFreeCompany = _config.VoiceChatFreeCompany;
            if (ImGui.Checkbox("Voice free company Chat", ref voiceChatFreeCompany))
            {
                _config.VoiceChatFreeCompany = voiceChatFreeCompany;
                _config.Save();
            }

            var voiceChatTell = _config.VoiceChatTell;
            if (ImGui.Checkbox("Voice tell Chat", ref voiceChatTell))
            {
                _config.VoiceChatTell = voiceChatTell;
                _config.Save();
            }

            var voiceChatParty = _config.VoiceChatParty;
            if (ImGui.Checkbox("Voice party Chat", ref voiceChatParty))
            {
                _config.VoiceChatParty = voiceChatParty;
                _config.Save();
            }

            var voiceChatAlliance = _config.VoiceChatAlliance;
            if (ImGui.Checkbox("Voice alliance Chat", ref voiceChatAlliance))
            {
                _config.VoiceChatAlliance = voiceChatAlliance;
                _config.Save();
            }

            var voiceChatNoviceNetwork = _config.VoiceChatNoviceNetwork;
            if (ImGui.Checkbox("Voice novice network Chat", ref voiceChatNoviceNetwork))
            {
                _config.VoiceChatNoviceNetwork = voiceChatNoviceNetwork;
                _config.Save();
            }

            var voiceChatLinkshell = _config.VoiceChatLinkshell;
            if (ImGui.Checkbox("Voice Linkshells", ref voiceChatLinkshell))
            {
                _config.VoiceChatLinkshell = voiceChatLinkshell;
                _config.Save();
            }

            var voiceChatCrossLinkshell = _config.VoiceChatCrossLinkshell;
            if (ImGui.Checkbox("Voice Cross Linkshells", ref voiceChatCrossLinkshell))
            {
                _config.VoiceChatCrossLinkshell = voiceChatCrossLinkshell;
                _config.Save();
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
                        DrawVoiceSelectionTable("NPCs", _config.MappedNpcs, ref filteredNpcs,
                                                ref _updateDataNpcs, ref resetDataNpcs, ref filterGenderNpcs,
                                                ref filterRaceNpcs, ref filterNameNpcs, ref filterVoiceNpcs);
                    }
                }

                using (var tabItemPlayers = ImRaii.TabItem("Players"))
                {
                    if (tabItemPlayers)
                    {
                        DrawVoiceSelectionTable("Players", _config.MappedPlayers, ref filteredPlayers,
                                                ref _updateDataNpcs, ref resetDataPlayers, ref filterGenderPlayers,
                                                ref filterRacePlayers, ref filterNamePlayers, ref filterVoicePlayers);
                    }
                }

                using (var tabItemBubbles = ImRaii.TabItem("Bubbles"))
                {
                    if (tabItemBubbles)
                    {
                        DrawVoiceSelectionTable("Bubbles", _config.MappedNpcs, ref filteredBubbles,
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
            _log.Error(nameof(DrawVoiceSelection), $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    private void DrawVoices()
    {
        var voiceArr = _config.EchokrautVoices.ConvertAll(p => p.ToString()).ToArray();
        var defaultVoiceIndex = _config.EchokrautVoices.FindIndex(p => p.IsDefault);
        if (ImGui.Combo($"Default Voice:##EKDefaultVoice", ref defaultVoiceIndex, voiceArr, voiceArr.Length))
        {
            // Clear all defaults
            foreach (var voice in _config.EchokrautVoices)
                voice.IsDefault = false;

            _config.EchokrautVoices[defaultVoiceIndex].IsDefault = true;
            _config.Save();
        }

        _updateDataVoices = filteredVoices.Count == 0;

        if (_updateDataVoices || (resetDataVoices && (filterGenderVoices.Length == 0 || filterRaceVoices.Length == 0 || filterNameVoices.Length == 0)))
        {
            filteredVoices = _config.EchokrautVoices;
            _updateDataVoices = true;
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
                if (ImGui.InputText($"##EKFilterNpcName", ref filterNameVoices, 40) || (filterNameVoices.Length > 0 && _updateDataVoices))
                {
                    filteredVoices = filteredVoices.FindAll(p => p.VoiceName.ToLower().Contains(filterNameVoices.ToLower()));
                    _updateDataVoices = true;
                    resetDataVoices = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilterNpcNote", ref filterNoteVoices, 40) || (filterNoteVoices.Length > 0 && _updateDataVoices))
                {
                    filteredVoices = filteredVoices.FindAll(p => p.Note.ToLower().Contains(filterNoteVoices.ToLower()));
                    _updateDataVoices = true;
                    resetDataVoices = true;
                }
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilterNpcGenders", ref filterGenderVoices, 40) || (filterGenderVoices.Length > 0 && _updateDataVoices))
                {
                    var foundGenderIndex = Constants.GENDERLIST.FindIndex(p => p.ToString().Contains(filterGenderVoices));
                    filteredVoices = foundGenderIndex >= 0 ? filteredVoices.FindAll(p => p.AllowedGenders.Contains(Constants.GENDERLIST[foundGenderIndex])): filteredVoices.FindAll(p => p.AllowedGenders.Count == 0);
                    _updateDataVoices = true;
                    resetDataVoices = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilterNpcRaces", ref filterRaceVoices, 40) || (filterRaceVoices.Length > 0 && _updateDataVoices))
                {
                    var foundRaceIndex = Constants.RACELIST.FindIndex(p => p.ToString().Contains(filterRaceVoices, StringComparison.OrdinalIgnoreCase));
                    filteredVoices = foundRaceIndex >= 0 ? filteredVoices.FindAll(p => p.AllowedRaces.Contains(Constants.RACELIST[foundRaceIndex])) : filteredVoices.FindAll(p => p.AllowedRaces.Count == 0);
                    _updateDataVoices = true;
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

                    _updateDataVoices = false;
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
                        _config.Save();
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    ImGui.TextUnformatted(voice.VoiceName);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##EKVoiceNote{voice}", ref voice.Note, 80))
                    {
                        _config.Save();
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
                                    _config.Save();
                                }

                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                var isChildVoice = voice.IsChildVoice;
                                if (ImGui.Checkbox($"Child Voice##EKVoiceIsChildVoice{voice}", ref isChildVoice))
                                {
                                    voice.IsChildVoice = isChildVoice;
                                    _config.Save();
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
                                    _npcData.ReSetVoiceGenders(voice);
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

                                        _npcData.RefreshSelectables(_config.EchokrautVoices);
                                        _config.Save();
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

                                    _npcData.RefreshSelectables(_config.EchokrautVoices);
                                    _config.Save();
                                }

                                ImGui.TableNextColumn();
                                ImGui.TableNextColumn();
                                if (ImGui.Button($"Reset##EKVoiceAllowedRace{voice}Reset"))
                                {
                                    _npcData.ReSetVoiceRaces(voice);
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

                                        _npcData.RefreshSelectables(_config.EchokrautVoices);
                                        _config.Save();
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
                        _config.Save();
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

            NpcMapData? toBeRemoved = null;
            foreach (NpcMapData mapData in filteredData)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##testvoice{mapData}", new Vector2(25, 25), "Test Voice", false, true))
                {
                    if (mapData.Voice != null) BackendTestVoice(mapData.Voice);
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
                    _config.Save();
                }
                ImGui.TableNextColumn();
                var isEnabled = isBubble ? mapData.IsEnabledBubble : mapData.IsEnabled;
                if (ImGui.Checkbox($"##EKNpcEnabled{mapData.ToString()}", ref isEnabled))
                {
                    if (isBubble)
                        mapData.IsEnabledBubble = isEnabled;
                    else
                        mapData.IsEnabled = isEnabled;
                    _config.Save();
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
                            _log.Info(nameof(DrawVoiceSelectionTable), $"Updated Gender for {dataType}: {mapData.ToString()} from: {mapData.Gender} to: {newGender}", new EKEventId(0, TextSource.None));

                            mapData.Gender = newGender;
                            mapData.RefreshSelectable();
                            mapData.DoNotDelete = true;
                            updateData = true;
                            _config.Save();
                        }
                    }
                    else
                        _log.Error(nameof(DrawVoiceSelectionTable), $"Couldnt update Gender for {dataType}: {mapData}", new EKEventId(0, TextSource.None));
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
                            _log.Info(nameof(DrawVoiceSelectionTable), $"Updated Race for {dataType}: {mapData.ToString()} from: {mapData.Race} to: {newRace}", new EKEventId(0, TextSource.None));

                            mapData.Race = newRace;
                            mapData.RefreshSelectable();
                            mapData.DoNotDelete = true;
                            updateData = true;
                            _config.Save();
                        }
                    }
                    else
                        _log.Error(nameof(DrawVoiceSelectionTable), $"Couldnt update Race for {dataType}: {mapData}", new EKEventId(0, TextSource.None));
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(mapData.Name);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                if (mapData.VoicesSelectable.Draw(mapData.Voice?.VoiceName ?? "", out var selectedIndexVoice))
                {
                    var newVoiceItem = _config.EchokrautVoices.FindAll(f => f.IsSelectable(mapData.Name, mapData.Gender, mapData.Race, mapData.IsChild))[selectedIndexVoice];

                    mapData.Voice = newVoiceItem;
                    mapData.DoNotDelete = true;
                    mapData.RefreshSelectable();
                    updateData = true;
                    _config.Save();
                    _log.Info(nameof(DrawVoiceSelectionTable), $"Updated Voice for {dataType}: {mapData.ToString()} from: {mapData.Voice} to: {newVoiceItem}", new EKEventId(0, TextSource.None));
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
                    _config.Save();
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
                        _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, mapData.Name);
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
                _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, toBeRemoved.Name);
                realData.Remove(toBeRemoved);
                updateData = true;
                _config.Save();
            }
        }
    }
    #endregion

    #region Phonetic corrections
    private void DrawPhoneticCorrections()
    {
        try
        {
            if (_config.PhoneticCorrections.Count == 0)
            {
                _config.PhoneticCorrections.Add(new PhoneticCorrection("C'ami", "Kami"));
                _config.Save();
                updatePhonData = true;
            }

            if (filteredPhon == null)
            {
                updatePhonData = true;
            }

            if (updatePhonData || (resetPhonFilter && (filterPhonOriginal.Length == 0 || filterPhonCorrected.Length == 0)))
            {
                filteredPhon = _config.PhoneticCorrections ?? [];
                updatePhonData = true;
                resetPhonFilter = false;
            }
            filteredPhon ??= [];
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
                        if (!_config.PhoneticCorrections!.Contains(newCorrection))
                        {
                            _config.PhoneticCorrections.Add(newCorrection);
                            _config.PhoneticCorrections.Sort();
                            _config.Save();
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

                PhoneticCorrection? toBeRemoved = null;
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
                        _config.Save();
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.InputText($"##correctText{i}", ref phoneticCorrection.CorrectedText, 25))
                        _config.Save();

                    i++;
                }

                if (toBeRemoved != null)
                {
                    _config.PhoneticCorrections!.Remove(toBeRemoved);
                    _config.PhoneticCorrections.Sort();
                    _config.Save();
                    updatePhonData = true;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawPhoneticCorrections), $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
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
                                     _config.logConfig.GeneralJumpToBottom,
                                     _config.logConfig.ShowGeneralDebugLog,
                                     _config.logConfig.ShowGeneralErrorLog,
                                     true,
                                     ref filteredLogsGeneral,
                                     ref _updateLogGeneralFilter,
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
                                     _config.logConfig.TalkJumpToBottom,
                                     _config.logConfig.ShowTalkDebugLog,
                                     _config.logConfig.ShowTalkErrorLog,
                                     _config.logConfig.ShowTalkId0,
                                     ref filteredLogsTalk,
                                     ref _updateLogTalkFilter,
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
                                     _config.logConfig.BattleTalkJumpToBottom,
                                     _config.logConfig.ShowBattleTalkDebugLog,
                                     _config.logConfig.ShowBattleTalkErrorLog,
                                     _config.logConfig.ShowBattleTalkId0,
                                     ref filteredLogsBattleTalk,
                                     ref _updateLogBattleTalkFilter,
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
                                     _config.logConfig.ChatJumpToBottom,
                                     _config.logConfig.ShowChatDebugLog,
                                     _config.logConfig.ShowChatErrorLog,
                                     _config.logConfig.ShowChatId0,
                                     ref filteredLogsChat,
                                     ref _updateLogChatFilter,
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
                                     _config.logConfig.BubbleJumpToBottom,
                                     _config.logConfig.ShowBubbleDebugLog,
                                     _config.logConfig.ShowBubbleErrorLog,
                                     _config.logConfig.ShowBubbleId0,
                                     ref filteredLogsBubbles,
                                     ref _updateLogBubblesFilter,
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
                                     _config.logConfig.CutsceneSelectStringJumpToBottom,
                                     _config.logConfig.ShowCutsceneSelectStringDebugLog,
                                     _config.logConfig.ShowCutsceneSelectStringErrorLog,
                                     _config.logConfig.ShowCutsceneSelectStringId0,
                                     ref filteredLogsCutsceneSelectString,
                                     ref _updateLogCutsceneSelectStringFilter,
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
                                     _config.logConfig.SelectStringJumpToBottom,
                                     _config.logConfig.ShowSelectStringDebugLog,
                                     _config.logConfig.ShowSelectStringErrorLog,
                                     _config.logConfig.ShowSelectStringId0,
                                     ref filteredLogsSelectString,
                                     ref _updateLogSelectStringFilter,
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
                                     _config.logConfig.BackendJumpToBottom,
                                     _config.logConfig.ShowBackendDebugLog,
                                     _config.logConfig.ShowBackendErrorLog,
                                     _config.logConfig.ShowBackendId0,
                                     ref _filteredLogsBackend,
                                     ref _updateLogBackendFilter,
                                     ref _resetLogBackendFilter,
                                     ref _filterLogsBackendMethod,
                                     ref _filterLogsBackendMessage,
                                     ref _filterLogsBackendId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawLogs), $"Something went wrong: {ex}", new EKEventId(0, TextSource.None));
        }
    }

    internal void DrawLogTableSettings(TextSource textSource, bool configJumpToBottom, bool configShowDebugLog, bool configShowErrorLog, bool configShowId0, ref bool updateLogs){
        if (ImGui.CollapsingHeader("Options:"))
        {
            using (ImRaii.Disabled(_log.Updating))
            {
                if (ImGui.Checkbox("Show debug logs", ref configShowDebugLog))
                {
                    switch (textSource)
                    {
                        case TextSource.None:
                            _config.logConfig.ShowGeneralDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonBubble:
                            _config.logConfig.ShowBubbleDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonTalk:
                            _config.logConfig.ShowTalkDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonBattleTalk:
                            _config.logConfig.ShowBattleTalkDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonSelectString:
                            _config.logConfig.ShowSelectStringDebugLog = configShowDebugLog;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            _config.logConfig.ShowCutsceneSelectStringDebugLog = configShowDebugLog;
                            break;
                        case TextSource.Chat:
                            _config.logConfig.ShowChatDebugLog = configShowDebugLog;
                            break;
                        case TextSource.Backend:
                            _config.logConfig.ShowBackendDebugLog = configShowDebugLog;
                            break;
                    }

                    _config.Save();
                    updateLogs = true;
                }

                if (ImGui.Checkbox("Show error logs", ref configShowErrorLog))
                {
                    switch (textSource)
                    {
                        case TextSource.None:
                            _config.logConfig.ShowGeneralErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonBubble:
                            _config.logConfig.ShowBubbleErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonTalk:
                            _config.logConfig.ShowTalkErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonBattleTalk:
                            _config.logConfig.ShowBattleTalkErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonSelectString:
                            _config.logConfig.ShowSelectStringErrorLog = configShowErrorLog;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            _config.logConfig.ShowCutsceneSelectStringErrorLog = configShowErrorLog;
                            break;
                        case TextSource.Chat:
                            _config.logConfig.ShowChatErrorLog = configShowErrorLog;
                            break;
                        case TextSource.Backend:
                            _config.logConfig.ShowBackendErrorLog = configShowErrorLog;
                            break;
                    }

                    _config.Save();
                    updateLogs = true;
                }

                if (ImGui.Checkbox("Show ID: 0", ref configShowId0))
                {
                    switch (textSource)
                    {
                        case TextSource.AddonBubble:
                            _config.logConfig.ShowBubbleId0 = configShowId0;
                            break;
                        case TextSource.AddonTalk:
                            _config.logConfig.ShowTalkId0 = configShowId0;
                            break;
                        case TextSource.AddonBattleTalk:
                            _config.logConfig.ShowBattleTalkId0 = configShowId0;
                            break;
                        case TextSource.AddonSelectString:
                            _config.logConfig.ShowSelectStringId0 = configShowId0;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            _config.logConfig.ShowCutsceneSelectStringId0 = configShowId0;
                            break;
                        case TextSource.Chat:
                            _config.logConfig.ShowChatId0 = configShowId0;
                            break;
                        case TextSource.Backend:
                            _config.logConfig.ShowBackendId0 = configShowId0;
                            break;
                    }

                    _config.Save();
                    updateLogs = true;
                }

                if (ImGui.Checkbox("Always jump to bottom", ref configJumpToBottom))
                {
                    switch (textSource)
                    {
                        case TextSource.None:
                            _config.logConfig.GeneralJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonBubble:
                            _config.logConfig.BubbleJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonTalk:
                            _config.logConfig.TalkJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonBattleTalk:
                            _config.logConfig.BattleTalkJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonSelectString:
                            _config.logConfig.SelectStringJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.AddonCutsceneSelectString:
                            _config.logConfig.CutsceneSelectStringJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.Chat:
                            _config.logConfig.ChatJumpToBottom = configJumpToBottom;
                            break;
                        case TextSource.Backend:
                            _config.logConfig.BackendJumpToBottom = configJumpToBottom;
                            break;
                    }

                    _config.Save();
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
                if (!_log.Updating)
                {
                    filteredLogs = RecreateLogList(source);
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

                        if (!_log.Updating)
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
        var eventId = _log.Start(nameof(BackendTestVoice), TextSource.AddonTalk);
        _log.Debug(nameof(BackendTestVoice), $"Testing voice: {voice.ToString()}", eventId);
        // Say the thing
        var volume = _volumeService.GetVoiceVolume(eventId) * voice.Volume;
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
            Text = GetTestMessageText(_clientState.ClientLanguage),
            OriginalText = GetTestMessageText(_clientState.ClientLanguage),
            Language = _clientState.ClientLanguage,
            EventId = eventId,
            SpeakerFollowObj = _gameObjects.LocalPlayer,
            Volume = volume
        };


        if (volume > 0)
            _backend.ProcessVoiceMessage(voiceMessage);
        else
        {
            _log.Debug(nameof(BackendTestVoice), $"Skipping voice inference. Volume is 0", eventId);
            _log.End(nameof(BackendTestVoice), eventId);
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
        if (DialogState.CurrentVoiceMessage != null)
            _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
        _log.End(nameof(BackendStopVoice), new EKEventId(0, TextSource.AddonTalk));
    }

    private void ReloadRemoteMappings()
    {
        _jsonData.Reload(_clientState.ClientLanguage);
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
