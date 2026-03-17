using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using EKConfig = Echokraut.DataClasses.Configuration;

namespace Echokraut.Windows.Native;

/// <summary>
/// Native FFXIV-style settings window with top-level tabs
/// (Settings, Voice Sel., Phonetics, Logs) where Settings contains
/// the original 7 sub-tabs (General, Dialogue, Battle, Chat, Bubbles, Save/Load, Backend).
/// </summary>
public sealed unsafe partial class NativeConfigWindow : NativeAddon
{
    private readonly EKConfig _config;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IBackendService _backend;
    private readonly IGoogleDriveSyncService _googleDrive;
    private readonly IAlltalkInstanceService _alltalkInstance;
    private readonly IAudioFileService _audioFiles;
    private readonly IJsonDataService _jsonData;
    private readonly ICommandService _commands;
    private readonly ICommandManager _commandManager;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly INpcDataService _npcData;
    private readonly IVolumeService _volumeService;
    private readonly IGameObjectService _gameObjects;

    // ── Top-level tab infrastructure ─────────────────────────────────────────
    // Index: 0=Settings, 1=Voice Sel., 2=Phonetics, 3=Logs
    private const int TopTabCount = 4;

    // Settings sub-panels (index matches inner tab order)
    private readonly ScrollingListNode?[] _settingsPanels = new ScrollingListNode?[7];
    private TabBarNode? _settingsTabBar;

    // Positions cached from OnSetup for partial-class builders
    private Vector2 _topContentPos;
    private Vector2 _topContentSize;
    private Vector2 _innerContentPos;
    private Vector2 _innerContentSize;
    private float _contentWidth;

    // Visibility tracking for top-level sections
    private int _activeTopTab;

    // ── Nodes that need per-frame enabled/disabled sync ───────────────────────

    // General — link buttons (attached directly to addon, top-right)
    private TextButtonNode? _discordButton;
    private TextButtonNode? _githubButton;

    // General — delete confirmation state
    private bool _deleteNpcsArmed;
    private bool _deletePlayersArmed;
    private bool _deleteBubblesArmed;
    private DateTime _lastDeleteClick = DateTime.MinValue;
    private TextButtonNode? _clearNpcsButton;
    private TextButtonNode? _clearPlayersButton;
    private TextButtonNode? _clearBubblesButton;

    // General
    private CheckboxNode? _generateBySentenceCheck;
    private CheckboxNode? _hideUiCheck;
    private CheckboxNode? _showExtraOptionsCheck;
    private CheckboxNode? _showExtraExtraCheck;
    private CheckboxNode? _removePunctuationCheck;

    // Dialogue
    private CheckboxNode? _voiceDialogueIn3DCheck;
    private SliderNode?   _dialogue3DSlider;

    // Battle
    private CheckboxNode? _voiceBattleQueuedCheck;

    // Chat
    private CheckboxNode?  _voiceChatIn3DCheck;
    private SliderNode?    _chat3DSlider;
    private TextInputNode? _chatApiKeyInput;
    private CheckboxNode?  _voiceChatPlayerCheck;
    private CheckboxNode?  _voiceChatSayCheck;
    private CheckboxNode?  _voiceChatYellCheck;
    private CheckboxNode?  _voiceChatShoutCheck;
    private CheckboxNode?  _voiceChatFCCheck;
    private CheckboxNode?  _voiceChatTellCheck;
    private CheckboxNode?  _voiceChatPartyCheck;
    private CheckboxNode?  _voiceChatAllianceCheck;
    private CheckboxNode?  _voiceChatNoviceCheck;
    private CheckboxNode?  _voiceChatLinkshellCheck;
    private CheckboxNode?  _voiceChatCrossLinkshellCheck;

    // Bubbles
    private CheckboxNode? _voiceBubblesInCityCheck;
    private CheckboxNode? _voiceSourceCamCheck;
    private SliderNode?   _bubbles3DSlider;

    // Save/Load
    private CheckboxNode?  _createMissingDirCheck;
    private TextInputNode? _localPathInput;
    private CheckboxNode?  _gdUploadCheck;
    private CheckboxNode?  _gdDownloadPeriodicCheck;
    private TextInputNode? _gdShareLinkInput;
    private TextButtonNode? _gdDownloadNowButton;

    // Backend — deferred dropdown selection (same crash-safe pattern as DialogTalkController)
    private TextDropDownNode? _backendDropDown;
    private string? _pendingBackendSelection;

    // Alltalk controls
    private CheckboxNode? _atLocalCheck;
    private CheckboxNode? _atRemoteCheck;
    private CheckboxNode? _atNoInstanceCheck;
    private CheckboxNode? _atAutoStartCheck;
    private TextButtonNode? _atStartButton;
    private TextButtonNode? _atStopButton;
    private TextInputNode? _atBaseUrlInput;
    private TextButtonNode? _atTestConnectionButton;
    private TextNode? _atConnectionResultLabel;
    private CheckboxNode? _atStreamingCheck;
    private TextInputNode? _atReloadModelInput;
    private TextButtonNode? _atReloadModelButton;
    private TextButtonNode? _atReloadVoicesButton;
    private HorizontalListNode? _atStartStopRow;
    // Collapsible section toggle buttons + content arrays for per-frame visibility control
    private TextButtonNode? _localSectionToggle;
    private NodeBase[]? _localSectionContent;
    private TextButtonNode? _remoteSectionToggle;
    private NodeBase[]? _remoteSectionContent;
    private TextButtonNode? _serviceSectionToggle;
    private NodeBase[]? _serviceSectionContent;

    public NativeConfigWindow(
        EKConfig config,
        IAudioPlaybackService audioPlayback,
        IBackendService backend,
        IGoogleDriveSyncService googleDrive,
        IAlltalkInstanceService alltalkInstance,
        IAudioFileService audioFiles,
        IJsonDataService jsonData,
        ICommandService commands,
        ICommandManager commandManager,
        IClientState clientState,
        ILogService log,
        INpcDataService npcData,
        IVolumeService volumeService,
        IGameObjectService gameObjects)
    {
        _config = config;
        _audioPlayback = audioPlayback;
        _backend = backend;
        _googleDrive = googleDrive;
        _alltalkInstance = alltalkInstance;
        _audioFiles = audioFiles;
        _jsonData = jsonData;
        _commands = commands;
        _commandManager = commandManager;
        _clientState = clientState;
        _log = log;
        _npcData = npcData;
        _volumeService = volumeService;
        _gameObjects = gameObjects;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Setup
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnSetup(AtkUnitBase* addon)
    {
        var pos  = ContentStartPosition;
        var size = ContentSize;
        const float tabH = 32f;

        // ── Top-level tab bar ────────────────────────────────────────────────
        var topTabBar = new TabBarNode { Size = new Vector2(size.X, tabH), Position = pos };

        // Content area below top tab bar
        _topContentPos  = pos  + new Vector2(0, tabH + 2);
        _topContentSize = size - new Vector2(0, tabH + 2);

        // Inner content area (below inner tab bar, used by Settings / Voice Sel / Logs)
        _innerContentPos  = _topContentPos  + new Vector2(0, tabH + 2);
        _innerContentSize = _topContentSize - new Vector2(0, tabH + 2);
        _contentWidth = size.X;

        // ── Settings section ─────────────────────────────────────────────────
        _settingsTabBar = new TabBarNode { Size = new Vector2(size.X, tabH), Position = _topContentPos };

        _settingsPanels[0] = BuildGeneralPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[1] = BuildDialoguePanel(_innerContentPos, _innerContentSize);
        _settingsPanels[2] = BuildBattlePanel(_innerContentPos, _innerContentSize);
        _settingsPanels[3] = BuildChatPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[4] = BuildBubblesPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[5] = BuildSaveLoadPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[6] = BuildBackendPanel(_innerContentPos, _innerContentSize);

        _settingsTabBar.AddTab("General",   () => ShowSettingsPanel(0));
        _settingsTabBar.AddTab("Dialogue",  () => ShowSettingsPanel(1));
        _settingsTabBar.AddTab("Battle",    () => ShowSettingsPanel(2));
        _settingsTabBar.AddTab("Chat",      () => ShowSettingsPanel(3));
        _settingsTabBar.AddTab("Bubbles",   () => ShowSettingsPanel(4));
        _settingsTabBar.AddTab("Save/Load", () => ShowSettingsPanel(5));
        _settingsTabBar.AddTab("Backend",   () => ShowSettingsPanel(6));

        // Link buttons — positioned top-right, only visible on Settings tab
        const float discordW = 160f;
        const float githubW  = 120f;
        const float btnGap   = 4f;
        var rightEdge = pos.X + size.X;

        _githubButton = Button("Alltalk Github", githubW,
            () => CMDHelper.OpenUrl(Constants.ALLTALKGITHUBURL));
        _githubButton.Position = new Vector2(rightEdge - githubW, _innerContentPos.Y + 2);

        _discordButton = Button("Join discord server", discordW,
            () => CMDHelper.OpenUrl(Constants.DISCORDURL));
        _discordButton.Position = new Vector2(rightEdge - githubW - btnGap - discordW, _innerContentPos.Y + 2);

        // ── Voice Selection section ──────────────────────────────────────────
        SetupVoiceSelection();

        // ── Phonetics section ────────────────────────────────────────────────
        SetupPhonetics();

        // ── Logs section ─────────────────────────────────────────────────────
        SetupLogs();

        // ── Top-level tabs ───────────────────────────────────────────────────
        topTabBar.AddTab("Settings",   () => ShowTopPanel(0));
        topTabBar.AddTab("Voice Sel.", () => ShowTopPanel(1));
        topTabBar.AddTab("Phonetics",  () => ShowTopPanel(2));
        topTabBar.AddTab("Logs",       () => ShowTopPanel(3));

        // ── Add all nodes to addon ───────────────────────────────────────────
        AddNode(topTabBar);

        // Settings nodes
        AddNode(_settingsTabBar);
        foreach (var p in _settingsPanels)
            if (p != null) AddNode(p);
        AddNode(_discordButton);
        AddNode(_githubButton);

        // Voice Selection nodes
        AddVoiceSelectionNodes();

        // Phonetics nodes
        AddPhoneticsNodes();

        // Logs nodes
        AddLogsNodes();

        ShowTopPanel(0);
    }

    private int _activeSettingsTab;

    private void ShowTopPanel(int index)
    {
        _activeTopTab = index;

        // Hide everything first
        SetVisible(_settingsTabBar, false);
        for (var i = 0; i < _settingsPanels.Length; i++)
            SetVisible(_settingsPanels[i], false);
        SetVisible(_discordButton, false);
        SetVisible(_githubButton, false);

        // Show the selected top-level section
        if (index == 0)
        {
            SetVisible(_settingsTabBar, true);
            ShowSettingsPanel(_activeSettingsTab);
        }

        ShowVoiceSelectionSection(index == 1);
        ShowPhoneticsSection(index == 2);
        ShowLogsSection(index == 3);
    }

    private void ShowSettingsPanel(int index)
    {
        _activeSettingsTab = index;
        for (var i = 0; i < _settingsPanels.Length; i++)
            SetVisible(_settingsPanels[i], i == index);

        // Link buttons only visible on General tab (index 0)
        SetVisible(_discordButton, index == 0);
        SetVisible(_githubButton,  index == 0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-frame update — sync enabled/disabled states and deferred selection
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        // Reset delete confirmations after 5 seconds
        if (_lastDeleteClick.AddSeconds(5) <= DateTime.Now && (_deleteNpcsArmed || _deletePlayersArmed || _deleteBubblesArmed))
        {
            _deleteNpcsArmed = false;
            _deletePlayersArmed = false;
            _deleteBubblesArmed = false;
            if (_clearNpcsButton != null)    _clearNpcsButton.String    = "Clear mapped NPCs";
            if (_clearPlayersButton != null) _clearPlayersButton.String = "Clear mapped players";
            if (_clearBubblesButton != null) _clearBubblesButton.String = "Clear mapped bubbles";
        }

        // Backend dropdown deferred selection (same crash-safe pattern as DialogTalkController)
        if (_pendingBackendSelection != null)
        {
            var sel = _pendingBackendSelection;
            _pendingBackendSelection = null;
            ProcessBackendSelection(sel);
        }

        var enabled = _config.Enabled;
        Dim(_generateBySentenceCheck, enabled);
        Dim(_hideUiCheck,             enabled);
        Dim(_showExtraOptionsCheck,   enabled);
        Dim(_showExtraExtraCheck,     enabled && _config.ShowExtraOptionsInDialogue);
        Dim(_removePunctuationCheck,  enabled);

        Dim(_voiceDialogueIn3DCheck, _config.VoiceDialogue);
        Dim(_dialogue3DSlider,       _config.VoiceDialogue && _config.VoiceDialogueIn3D);

        Dim(_voiceBattleQueuedCheck, _config.VoiceBattleDialogue);

        var voiceChat = _config.VoiceChat;
        Dim(_voiceChatIn3DCheck,          voiceChat);
        Dim(_chat3DSlider,                voiceChat && _config.VoiceChatIn3D);
        Dim(_chatApiKeyInput,             voiceChat);
        Dim(_voiceChatPlayerCheck,        voiceChat);
        Dim(_voiceChatSayCheck,           voiceChat);
        Dim(_voiceChatYellCheck,          voiceChat);
        Dim(_voiceChatShoutCheck,         voiceChat);
        Dim(_voiceChatFCCheck,            voiceChat);
        Dim(_voiceChatTellCheck,          voiceChat);
        Dim(_voiceChatPartyCheck,         voiceChat);
        Dim(_voiceChatAllianceCheck,      voiceChat);
        Dim(_voiceChatNoviceCheck,        voiceChat);
        Dim(_voiceChatLinkshellCheck,     voiceChat);
        Dim(_voiceChatCrossLinkshellCheck,voiceChat);

        Dim(_voiceBubblesInCityCheck, _config.VoiceBubble);
        Dim(_voiceSourceCamCheck,     _config.VoiceBubble);
        Dim(_bubbles3DSlider,         _config.VoiceBubble);

        Dim(_createMissingDirCheck, _config.SaveToLocal);
        Dim(_localPathInput,        _config.SaveToLocal || _config.LoadFromLocalFirst);
        Dim(_gdUploadCheck,           _config.SaveToLocal);
        Dim(_gdDownloadPeriodicCheck, _config.GoogleDriveDownload);
        Dim(_gdShareLinkInput,        _config.GoogleDriveDownload);
        Dim(_gdDownloadNowButton,     _config.GoogleDriveDownload);

        // Alltalk controls — visibility and state
        var isAlltalk = _config.BackendSelection == TTSBackends.Alltalk;
        var instanceType = _config.Alltalk.InstanceType;
        var isLocal   = instanceType == AlltalkInstanceType.Local;
        var isRemote  = instanceType == AlltalkInstanceType.Remote;
        var isNone    = instanceType == AlltalkInstanceType.None;
        var installing = _alltalkInstance.Installing;

        // Instance type — dim the already-selected option
        Dim(_atLocalCheck,      !installing && !isLocal);
        Dim(_atRemoteCheck,     !installing && !isRemote);
        Dim(_atNoInstanceCheck, !installing && !isNone);

        // Show/hide entire collapsible sections based on instance type
        var showLocal   = isAlltalk && isLocal;
        var showRemote  = isAlltalk && isRemote;
        var showService = isAlltalk && (isLocal || isRemote);

        // Local section: toggle button + content
        SetVisible(_localSectionToggle, showLocal);
        if (!showLocal && _localSectionContent != null)
            foreach (var n in _localSectionContent) SetVisible(n, false);

        // Remote section: toggle button + content
        SetVisible(_remoteSectionToggle, showRemote);
        if (!showRemote && _remoteSectionContent != null)
            foreach (var n in _remoteSectionContent) SetVisible(n, false);

        // Service section: toggle button + content
        SetVisible(_serviceSectionToggle, showService);
        if (!showService && _serviceSectionContent != null)
            foreach (var n in _serviceSectionContent) SetVisible(n, false);

        if (_atStartButton != null)
        {
            _atStartButton.String = _alltalkInstance.InstanceStarting ? "Starting..." : _alltalkInstance.InstanceRunning ? "Running" : "Start";
            Dim(_atStartButton, !_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting);
        }
        if (_atStopButton != null)
            Dim(_atStopButton, (_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting) && !_alltalkInstance.InstanceStopping);

        // Partial class updates
        UpdateVoiceSelection();
        UpdatePhonetics();
        UpdateLogs();
    }

    private static void Dim(NodeBase? node, bool enabled)
    {
        if (node != null) node.Alpha = enabled ? 1.0f : 0.4f;
    }

    private static void SetVisible(NodeBase? node, bool visible)
    {
        if (node != null) node.IsVisible = visible;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // General tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildGeneralPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var enabledCheck = Check("Enabled", w, _config.Enabled,
            v => { _config.Enabled = v; _config.Save(); });

        var useNativeUiCheck = Check("Use native FFXIV UI", w, _config.UseNativeUI,
            v => { _config.UseNativeUI = v; _config.Save(); _commands.RequestUiModeSwitch(); });

        _generateBySentenceCheck = Check(
            "Generate per sentence (shorter latency, recommended for CPU inference)", w,
            _config.GenerateBySentence,
            v => { _config.GenerateBySentence = v; _config.Save(); });

        var removeStuttersCheck = Check("Remove stutters", w, _config.RemoveStutters,
            v => { _config.RemoveStutters = v; _config.Save(); });

        _hideUiCheck = Check("Hide UI in cutscenes", w, _config.HideUiInCutscenes,
            v => { _config.HideUiInCutscenes = v; _config.Save(); });

        _showExtraOptionsCheck = Check(
            "Show Play/Pause, Stop and Mute buttons in dialogues", w,
            _config.ShowExtraOptionsInDialogue,
            v => { _config.ShowExtraOptionsInDialogue = v; _config.Save(); });

        _showExtraExtraCheck = Check(
            "Show extended dialogue options (voice selector, auto-advance)", w,
            _config.ShowExtraExtraOptionsInDialogue,
            v => { _config.ShowExtraExtraOptionsInDialogue = v; _config.Save(); });

        _removePunctuationCheck = Check(
            "Remove punctuation (may reduce end-of-speech hallucinations)", w,
            _config.RemovePunctuation,
            v => { _config.RemovePunctuation = v; _config.Save(); });

        // Unrecoverable actions
        _clearNpcsButton = Button("Clear mapped NPCs", 160, () =>
        {
            if (_deleteNpcsArmed)
            {
                _deleteNpcsArmed = false;
                _clearNpcsButton!.String = "Clear mapped NPCs";
                foreach (var npc in _config.MappedNpcs.FindAll(p => !p.Name.StartsWith("BB") && !p.DoNotDelete))
                {
                    _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, npc.Name);
                    _config.MappedNpcs.Remove(npc);
                }
                _config.Save();
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _deleteNpcsArmed = true;
                _clearNpcsButton!.String = "Confirm clear NPCs!";
            }
        });
        _clearPlayersButton = Button("Clear mapped players", 160, () =>
        {
            if (_deletePlayersArmed)
            {
                _deletePlayersArmed = false;
                _clearPlayersButton!.String = "Clear mapped players";
                foreach (var p in _config.MappedPlayers.FindAll(p => !p.DoNotDelete))
                {
                    _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, p.Name);
                    _config.MappedPlayers.Remove(p);
                }
                _config.Save();
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _deletePlayersArmed = true;
                _clearPlayersButton!.String = "Confirm clear players!";
            }
        });
        _clearBubblesButton = Button("Clear mapped bubbles", 160, () =>
        {
            if (_deleteBubblesArmed)
            {
                _deleteBubblesArmed = false;
                _clearBubblesButton!.String = "Clear mapped bubbles";
                foreach (var npc in _config.MappedNpcs.FindAll(p => p.Name.StartsWith("BB") && !p.DoNotDelete))
                {
                    _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, npc.Name);
                    _config.MappedNpcs.Remove(npc);
                }
                _config.Save();
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _deleteBubblesArmed = true;
                _clearBubblesButton!.String = "Confirm clear bubbles!";
            }
        });
        var reloadRemoteButton = Button("Reload remote mappings", 180,
            () => _jsonData.Reload(_clientState.ClientLanguage));

        // Available commands
        var commandNodes = _commands.CommandKeys
            .Select(key => _commandManager.Commands.TryGetValue(key, out var cmd)
                ? Label($"{key}  {cmd.HelpMessage}", w)
                : null)
            .Where(n => n != null)
            .ToList();

        // Unrecoverable actions content (subtract scroll bar width ~20px)
        var innerW = w - 20;
        var btnW = (innerW - 4) / 2;
        _clearNpcsButton!.Size    = new Vector2(btnW, 24);
        _clearPlayersButton!.Size = new Vector2(btnW, 24);
        _clearBubblesButton!.Size = new Vector2(btnW, 24);
        reloadRemoteButton.Size   = new Vector2(btnW, 24);

        var row1 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row1.AddNode(_clearNpcsButton);
        row1.AddNode(_clearPlayersButton);
        var row2 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row2.AddNode(_clearBubblesButton);
        row2.AddNode(reloadRemoteButton);

        list.AddNode(enabledCheck);
        list.AddNode(useNativeUiCheck);
        list.AddNode(_generateBySentenceCheck);
        list.AddNode(removeStuttersCheck);
        list.AddNode(_hideUiCheck);

        CreateCollapsibleSection(list, "Experimental options", w, true,
            [_showExtraOptionsCheck, _showExtraExtraCheck, _removePunctuationCheck]);

        CreateCollapsibleSection(list, "Unrecoverable actions", w, true, [row1, row2]);

        CreateCollapsibleSection(list, "Available commands", w, true,
            commandNodes.Where(n => n != null).Cast<NodeBase>().ToArray());
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dialogue tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildDialoguePanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var voiceDialogueCheck = Check("Voice dialogue", w, _config.VoiceDialogue,
            v => { _config.VoiceDialogue = v; _config.Save(); });

        _voiceDialogueIn3DCheck = Check("Voice dialogue in 3D space", w, _config.VoiceDialogueIn3D,
            v => { _config.VoiceDialogueIn3D = v; _config.Save(); });

        _dialogue3DSlider = Slider(w, _config.Voice3DAudibleRange,
            v => { _config.Voice3DAudibleRange = v; _config.Save(); _audioPlayback.Update3DFactors(v); });

        var voicePlayerCutsceneCheck = Check(
            "Voice player choices in cutscene", w, _config.VoicePlayerChoicesCutscene,
            v => { _config.VoicePlayerChoicesCutscene = v; _config.Save(); });

        var voicePlayerChoicesCheck = Check(
            "Voice player choices outside cutscene", w, _config.VoicePlayerChoices,
            v => { _config.VoicePlayerChoices = v; _config.Save(); });

        var cancelAdvanceCheck = Check("Cancel voice on text advance", w,
            _config.CancelSpeechOnTextAdvance,
            v => { _config.CancelSpeechOnTextAdvance = v; _config.Save(); });

        var autoAdvanceCheck = Check("Click dialogue after speech completion", w,
            _config.AutoAdvanceTextAfterSpeechCompleted,
            v => { _config.AutoAdvanceTextAfterSpeechCompleted = v; _config.Save(); });

        var voiceRetainersCheck = Check("Voice retainer dialogues", w, _config.VoiceRetainers,
            v => { _config.VoiceRetainers = v; _config.Save(); });

        list.AddNode(voiceDialogueCheck);
        list.AddNode(_voiceDialogueIn3DCheck);
        list.AddNode(_dialogue3DSlider);
        list.AddNode(Separator(w));
        list.AddNode(voicePlayerCutsceneCheck);
        list.AddNode(voicePlayerChoicesCheck);
        list.AddNode(Separator(w));
        list.AddNode(cancelAdvanceCheck);
        list.AddNode(autoAdvanceCheck);
        list.AddNode(voiceRetainersCheck);
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Battle dialogue tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildBattlePanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var voiceBattleCheck = Check("Voice battle dialogue", w, _config.VoiceBattleDialogue,
            v => { _config.VoiceBattleDialogue = v; _config.Save(); });

        _voiceBattleQueuedCheck = Check("Queue battle dialogue", w, _config.VoiceBattleDialogQueued,
            v => { _config.VoiceBattleDialogQueued = v; _config.Save(); });

        list.AddNode(voiceBattleCheck);
        list.AddNode(_voiceBattleQueuedCheck);
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Chat tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildChatPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var voiceChatCheck = Check("Voice chat", w, _config.VoiceChat,
            v => { _config.VoiceChat = v; _config.Save(); });

        _chatApiKeyInput = Input("Detect Language API key (detectlanguage.com)", w, 32,
            _config.VoiceChatLanguageAPIKey,
            v => { _config.VoiceChatLanguageAPIKey = v; _config.Save(); });

        _voiceChatIn3DCheck = Check("Voice chat in 3D space", w, _config.VoiceChatIn3D,
            v => { _config.VoiceChatIn3D = v; _config.Save(); });

        _chat3DSlider = Slider(w, _config.Voice3DAudibleRange,
            v => { _config.Voice3DAudibleRange = v; _config.Save(); _audioPlayback.Update3DFactors(v); });

        _voiceChatPlayerCheck        = Check("Voice your own chat",            w, _config.VoiceChatPlayer,         v => { _config.VoiceChatPlayer         = v; _config.Save(); });
        _voiceChatSayCheck           = Check("Voice say",                      w, _config.VoiceChatSay,            v => { _config.VoiceChatSay            = v; _config.Save(); });
        _voiceChatYellCheck          = Check("Voice yell",                     w, _config.VoiceChatYell,           v => { _config.VoiceChatYell           = v; _config.Save(); });
        _voiceChatShoutCheck         = Check("Voice shout",                    w, _config.VoiceChatShout,          v => { _config.VoiceChatShout          = v; _config.Save(); });
        _voiceChatFCCheck            = Check("Voice free company",             w, _config.VoiceChatFreeCompany,    v => { _config.VoiceChatFreeCompany    = v; _config.Save(); });
        _voiceChatTellCheck          = Check("Voice tell",                     w, _config.VoiceChatTell,           v => { _config.VoiceChatTell           = v; _config.Save(); });
        _voiceChatPartyCheck         = Check("Voice party",                    w, _config.VoiceChatParty,          v => { _config.VoiceChatParty          = v; _config.Save(); });
        _voiceChatAllianceCheck      = Check("Voice alliance",                 w, _config.VoiceChatAlliance,       v => { _config.VoiceChatAlliance       = v; _config.Save(); });
        _voiceChatNoviceCheck        = Check("Voice novice network",           w, _config.VoiceChatNoviceNetwork,  v => { _config.VoiceChatNoviceNetwork  = v; _config.Save(); });
        _voiceChatLinkshellCheck     = Check("Voice linkshells",               w, _config.VoiceChatLinkshell,      v => { _config.VoiceChatLinkshell      = v; _config.Save(); });
        _voiceChatCrossLinkshellCheck= Check("Voice cross-world linkshells",   w, _config.VoiceChatCrossLinkshell, v => { _config.VoiceChatCrossLinkshell = v; _config.Save(); });

        list.AddNode(voiceChatCheck);
        list.AddNode(_chatApiKeyInput);

        CreateCollapsibleSection(list, "3D Space", w, true,
            [_voiceChatIn3DCheck, _chat3DSlider]);

        CreateCollapsibleSection(list, "Chat channels", w, false,
            [_voiceChatPlayerCheck, _voiceChatSayCheck, _voiceChatYellCheck,
             _voiceChatShoutCheck, _voiceChatFCCheck, _voiceChatTellCheck,
             _voiceChatPartyCheck, _voiceChatAllianceCheck, _voiceChatNoviceCheck,
             _voiceChatLinkshellCheck, _voiceChatCrossLinkshellCheck]);

        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bubbles tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildBubblesPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var voiceBubblesCheck = Check("Voice NPC bubbles", w, _config.VoiceBubble,
            v => { _config.VoiceBubble = v; _config.Save(); });

        _voiceBubblesInCityCheck = Check("Voice bubbles in city", w, _config.VoiceBubblesInCity,
            v => { _config.VoiceBubblesInCity = v; _config.Save(); });

        _voiceSourceCamCheck = Check("Use camera as 3D sound source", w, _config.VoiceSourceCam,
            v => { _config.VoiceSourceCam = v; _config.Save(); });

        _bubbles3DSlider = Slider(w, _config.Voice3DAudibleRange,
            v => { _config.Voice3DAudibleRange = v; _config.Save(); _audioPlayback.Update3DFactors(v); });

        list.AddNode(voiceBubblesCheck);
        list.AddNode(_voiceBubblesInCityCheck);

        CreateCollapsibleSection(list, "3D Space", w, true,
            [_voiceSourceCamCheck, _bubbles3DSlider]);

        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Save/Load tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildSaveLoadPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var loadLocalFirst = Check("Search audio locally before generating", w,
            _config.LoadFromLocalFirst, v => { _config.LoadFromLocalFirst = v; _config.Save(); });
        var saveLocally = Check("Save generated audio locally", w, _config.SaveToLocal,
            v => { _config.SaveToLocal = v; _config.Save(); });
        _createMissingDirCheck = Check("Create directory if missing", w,
            _config.CreateMissingLocalSaveLocation,
            v => { _config.CreateMissingLocalSaveLocation = v; _config.Save(); });
        _localPathInput = Input("Local audio directory path", w, 260, _config.LocalSaveLocation,
            v => { _config.LocalSaveLocation = v; _config.Save(); });

        // Google Drive
        var gdRequestVoiceLine = Check(
            "Send dialogue lines to Ren Nagasaki's share for a voice line database", w,
            _config.GoogleDriveRequestVoiceLine,
            v =>
            {
                _config.GoogleDriveRequestVoiceLine = v;
                _config.Save();
                if (v) _ = _googleDrive.CreateDriveServicePkceAsync();
            });

        _gdUploadCheck = Check("Upload to Google Drive (requires local save)", w,
            _config.GoogleDriveUpload,
            v => { _config.GoogleDriveUpload = v; _config.Save(); });

        var gdDownload = Check("Download from Google Drive share", w, _config.GoogleDriveDownload,
            v => { _config.GoogleDriveDownload = v; _config.Save(); });
        _gdDownloadPeriodicCheck = Check(
            "Download periodically (every 60 min, new files only)", w,
            _config.GoogleDriveDownloadPeriodically,
            v => { _config.GoogleDriveDownloadPeriodically = v; _config.Save(); });
        _gdShareLinkInput = Input("Google Drive share link", w, 260, _config.GoogleDriveShareLink,
            v => { _config.GoogleDriveShareLink = v; _config.Save(); });
        _gdDownloadNowButton = Button("Download now", 120, () =>
            _googleDrive.DownloadFolder(_config.LocalSaveLocation, _config.GoogleDriveShareLink));

        list.AddNode(loadLocalFirst);
        list.AddNode(saveLocally);
        list.AddNode(_createMissingDirCheck);
        list.AddNode(_localPathInput);

        CreateCollapsibleSection(list, "Google Drive", w, false,
            [gdRequestVoiceLine, _gdUploadCheck, gdDownload,
             _gdDownloadPeriodicCheck, _gdShareLinkInput, _gdDownloadNowButton]);

        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backend tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildBackendPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var backends       = Enum.GetValues<TTSBackends>().Select(b => b.ToString()).ToList();
        var currentBackend = _config.BackendSelection.ToString();

        _backendDropDown = new TextDropDownNode { Size = new Vector2(w, 24), Options = [] };
        _backendDropDown.OptionListNode.Options        = backends;
        _backendDropDown.OptionListNode.SelectedOption = currentBackend;
        if (_backendDropDown.LabelNode.Node != null)
            _backendDropDown.LabelNode.String = currentBackend;

        // Capture in OnOptionSelected (fires before UpdateLabel crash); process next frame.
        _backendDropDown.OnOptionSelected = option => _pendingBackendSelection = option;

        // Alltalk instance type — radio-style mutual exclusion via enum
        _atLocalCheck = Check("Local instance", w, _config.Alltalk.InstanceType == AlltalkInstanceType.Local, v =>
        {
            if (!v) return;
            _config.Alltalk.InstanceType = AlltalkInstanceType.Local;
            _config.Save();
            _atLocalCheck!.IsChecked = true;
            _atRemoteCheck!.IsChecked = false;
            _atNoInstanceCheck!.IsChecked = false;
        });
        _atRemoteCheck = Check("Remote instance", w, _config.Alltalk.InstanceType == AlltalkInstanceType.Remote, v =>
        {
            if (!v) return;
            _config.Alltalk.InstanceType = AlltalkInstanceType.Remote;
            _config.Save();
            _atLocalCheck!.IsChecked = false;
            _atRemoteCheck!.IsChecked = true;
            _atNoInstanceCheck!.IsChecked = false;
        });
        _atNoInstanceCheck = Check("No instance", w, _config.Alltalk.InstanceType == AlltalkInstanceType.None, v =>
        {
            if (!v) return;
            _config.Alltalk.InstanceType = AlltalkInstanceType.None;
            _config.Save();
            _atLocalCheck!.IsChecked = false;
            _atRemoteCheck!.IsChecked = false;
            _atNoInstanceCheck!.IsChecked = true;
        });

        // Local instance controls
        _atAutoStartCheck = Check("Auto start local instance on plugin load", w,
            _config.Alltalk.AutoStartLocalInstance,
            v =>
            {
                _config.Alltalk.AutoStartLocalInstance = v;
                _config.Save();
                if (v && _config.Alltalk.LocalInstall && !_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting)
                    _alltalkInstance.StartInstance();
            });
        _atStartButton = Button("Start", 80, () => _alltalkInstance.StartInstance());
        _atStopButton = Button("Stop", 80, () => _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend)));

        _atStartStopRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        _atStartStopRow.AddNode(_atStartButton);
        _atStartStopRow.AddNode(_atStopButton);

        // Remote instance controls
        _atBaseUrlInput = Input("Alltalk base URL", w, 80, _config.Alltalk.BaseUrl,
            v => { _config.Alltalk.BaseUrl = v; _config.Save(); });
        _atTestConnectionButton = Button("Test", 60, () => TestConnection());
        _atConnectionResultLabel = new TextNode
        {
            Size     = new Vector2(w, 20),
            String   = " ",
            FontType = FontType.Axis,
            FontSize = 12,
        };

        // Service options (shared between local & remote)
        _atStreamingCheck = Check("Streaming generation (play audio before full text is generated)", w,
            _config.Alltalk.StreamingGeneration,
            v => { _config.Alltalk.StreamingGeneration = v; _config.Save(); });
        _atReloadModelInput = Input("Model name to reload", w, 40, _config.Alltalk.ReloadModel,
            v => { _config.Alltalk.ReloadModel = v; _config.Save(); });
        _atReloadModelButton = Button("Reload model", 120, () =>
            _backend.ReloadService(_config.Alltalk.ReloadModel, new EKEventId(0, TextSource.None)));
        _atReloadVoicesButton = Button("Reload voices", 120, () =>
        {
            _backend.SetBackendType(_config.BackendSelection);
            _backend.NotifyCharacterMapped();
        });

        list.AddNode(_backendDropDown);

        CreateCollapsibleSection(list, "Instance type", w, false,
            [_atLocalCheck, _atRemoteCheck, _atNoInstanceCheck]);

        _localSectionContent = [_atAutoStartCheck, _atStartStopRow!];
        _localSectionToggle = CreateCollapsibleSection(list, "Local instance", w, false, _localSectionContent);

        _remoteSectionContent = [_atBaseUrlInput, _atTestConnectionButton, _atConnectionResultLabel!];
        _remoteSectionToggle = CreateCollapsibleSection(list, "Remote connection", w, false, _remoteSectionContent);

        _serviceSectionContent = [_atStreamingCheck, _atReloadModelInput, _atReloadModelButton, _atReloadVoicesButton];
        _serviceSectionToggle = CreateCollapsibleSection(list, "Service options", w, true, _serviceSectionContent);

        return list;
    }

    private void TestConnection()
    {
        if (_atConnectionResultLabel != null)
            _atConnectionResultLabel.String = "Testing...";

        var task = _config.BackendSelection == TTSBackends.Alltalk
            ? _backend.CheckReady(new EKEventId(0, TextSource.None))
            : System.Threading.Tasks.Task.FromResult("No backend selected");

        task.ContinueWith(t =>
        {
            if (_atConnectionResultLabel == null) return;
            _atConnectionResultLabel.String = t.IsFaulted
                ? $"Error: {t.Exception?.InnerException?.Message}"
                : $"Result: {t.Result}";
        });
    }

    /// <summary>
    /// Applies a backend selection deferred from the ATK event context.
    /// Safe to call from OnUpdate — no restriction on managed/native calls here.
    /// </summary>
    private void ProcessBackendSelection(string option)
    {
        if (Enum.TryParse<TTSBackends>(option, out var backend))
        {
            _config.BackendSelection = backend;
            _config.Save();
            _backend.SetBackendType(backend);
        }

        if (_backendDropDown == null || _backendDropDown.IsCollapsed) return;
        try
        {
            _backendDropDown.OptionListNode.SelectedOption = option;
            if (_backendDropDown.LabelNode.Node != null)
                _backendDropDown.LabelNode.String = option;
            _backendDropDown.Collapse(false);
        }
        catch { /* ReattachNode may crash; dropdown state will recover on next open */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Node factory helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static ScrollingListNode Panel(Vector2 pos, Vector2 size) => new()
    {
        Position    = pos,
        Size        = size,
        FitWidth    = true,
        ItemSpacing = 4,
    };

    private static CheckboxNode Check(string label, float width, bool initial, Action<bool> onChange) => new()
    {
        Size      = new Vector2(width, 24),
        String    = label,
        IsChecked = initial,
        OnClick   = onChange,
    };

    private static SliderNode Slider(float width, float initialValue, Action<float> onChange)
    {
        var node = new SliderNode
        {
            Size          = new Vector2(width, 20),
            Range         = 0..100,
            DecimalPlaces = 2,
            Value         = (int)(initialValue * 100),
        };
        node.OnValueChanged = v => onChange(v / 100.0f);
        return node;
    }

    private static HorizontalLineNode Separator(float width) => new()
    {
        Size = new Vector2(width, 4),
    };

    /// <summary>Invisible node that reserves layout space in a HorizontalListNode.</summary>
    private static ResNode Spacer(float width, float height) => new()
    {
        Size = new Vector2(width, height),
        Alpha = 0,
    };

    private static TextNode Label(string text, float width) => new()
    {
        Size     = new Vector2(width, 18),
        String   = text,
        FontType = FontType.Axis,
        FontSize = 12,
    };

    private static TextButtonNode Button(string label, float width, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(width, 24), String = label };
        node.OnClick = onClick;
        return node;
    }

    private static TextInputNode Input(string placeholder, float width, int maxChars, string initial, Action<string> onComplete)
    {
        var node = new TextInputNode
        {
            Size              = new Vector2(width, 28),
            MaxCharacters     = maxChars,
            PlaceholderString = placeholder,
            String            = initial,
        };
        node.OnInputReceived = s => onComplete(s.ToString());
        return node;
    }

    /// <summary>
    /// Adds a collapsible section to a ScrollingListNode using a TextButtonNode toggle.
    /// Uses component events (no CollisionNode), so it works inside nested containers.
    /// Returns the toggle button so the caller can position it.
    /// </summary>
    private static TextButtonNode CreateCollapsibleSection(
        ScrollingListNode list, string title, float width, bool startCollapsed, NodeBase[] contentNodes)
    {
        var arrow = startCollapsed ? "[+]" : "[-]";
        TextButtonNode? toggle = null;
        toggle = new TextButtonNode { Size = new Vector2(width, 24), String = $"{arrow} {title}" };
        toggle.OnClick = () =>
        {
            var isHidden = contentNodes.Length > 0 && !contentNodes[0].IsVisible;
            foreach (var n in contentNodes)
                n.IsVisible = isHidden;
            toggle!.String = isHidden ? $"[-] {title}" : $"[+] {title}";
            list.RecalculateLayout();
        };

        // Set initial visibility
        if (startCollapsed)
            foreach (var n in contentNodes)
                n.IsVisible = false;

        list.AddNode(toggle);
        foreach (var n in contentNodes)
            list.AddNode(n);

        return toggle;
    }
}
