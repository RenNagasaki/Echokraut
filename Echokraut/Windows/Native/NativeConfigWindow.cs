using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Services;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using EKConfig = Echokraut.DataClasses.Configuration;

namespace Echokraut.Windows.Native;

/// <summary>
/// Native FFXIV-style settings window with top-level tabs
/// (Settings, Voice Sel., Phonetics, Logs) where Settings contains
/// 5 sub-tabs (General, Dialogue, Chat, Storage, Backend).
/// Battle Dialogue and NPC Bubbles are collapsible sections inside Dialogue.
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
    private readonly IVoiceTestService _voiceTest;

    // ── Top-level tab infrastructure ─────────────────────────────────────────
    // Index: 0=Settings, 1=Voice Sel., 2=Phonetics, 3=Logs
    private const int TopTabCount = 4;

    // Settings sub-panels (index matches inner tab order)
    private readonly ScrollingListNode?[] _settingsPanels = new ScrollingListNode?[5];
    private TabBarNode? _settingsTabBar;

    // Top-level tab bar (Settings / Voices / Phonetics / Logs). Promoted to a field because
    // None-mode (no live generation) hides the Voices + Phonetics tabs at runtime — we need
    // to Clear() + re-add tabs in correct order on InstanceType transitions, which can only
    // happen if the tab bar reference survives OnSetup.
    private TabBarNode? _topTabBar;
    private bool? _topTabsLiveGenSnapshot;

    // Tab-spanning shortcut buttons pinned at the bottom-left of the window. Always visible
    // regardless of which top-level tab is active (they live outside the tab content area).
    private DynamicIconButtonNode? _voiceClipManagerButton;
    private DynamicIconButtonNode? _gameDataToolsButton;

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
    private bool _wipeAllArmed;
    private volatile bool _wipeRunning;
    private volatile bool _wipeCompleted;
    private int _wipeDeletedCount;
    private DateTime _lastDeleteClick = DateTime.MinValue;
    private TextButtonNode? _clearNpcsButton;
    private TextButtonNode? _clearPlayersButton;
    private TextButtonNode? _clearBubblesButton;
    private TextButtonNode? _wipeAllButton;

    // General
    private SliderNode? _globalVolumeSlider;
    private CheckboxNode? _generateBySentenceCheck;
    private CheckboxNode? _hideUiCheck;
    private CheckboxNode? _showExtraOptionsCheck;
    private CheckboxNode? _removePunctuationCheck;

    // Dialogue
    private CheckboxNode? _voiceDialogueIn3DCheck;
    private SliderNode?   _dialogue3DSlider;
    // Player choices + retainer dialogue: live-only (route through TTS backend), so they
    // dim in None mode. Player choices have no audio-file fallback path — they're prompts
    // generated on the fly. Retainers likewise.
    private CheckboxNode? _voicePlayerCutsceneCheck;
    private CheckboxNode? _voicePlayerChoicesCheck;
    private CheckboxNode? _voiceRetainersCheck;

    // Battle
    private CheckboxNode? _voiceBattleQueuedCheck;

    // Chat — master toggle dims in None mode (chat TTS has no audio-file pipeline,
    // it's pure live generation per incoming chat line)
    private CheckboxNode?  _voiceChatCheck;
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
    // Auto-alias generation is live-only (each save triggers a backend call to produce the
    // alias variant), so it dims in None mode.
    private CheckboxNode?  _autoAliasCheck;
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
    // Mode switcher — 3 mutually-exclusive buttons (Local / Remote / None) replacing the
    // old 3-checkbox group. Selected button stays at full alpha; unselected ones dim. Click
    // any to switch InstanceType live; the rest of the panel reacts via OnUpdate.
    private TextButtonNode? _modeLocalBtn;
    private TextButtonNode? _modeRemoteBtn;
    private TextButtonNode? _modeNoneBtn;
    private HorizontalListNode? _modeSwitcherRow;
    private NativeAlltalkBuilder.LocalInstanceNodes? _atLocalNodes;
    private NativeAlltalkBuilder.RemoteInstanceNodes? _atRemoteNodes;
    private CheckboxNode? _atStreamingCheck;
    private TextInputNode? _atReloadModelInput;
    private TextButtonNode? _atReloadModelButton;
    private TextButtonNode? _atReloadVoicesButton;
    private HorizontalListNode? _atReloadRow;
    // None-mode section content — audio path + Google Drive download settings duplicated from
    // the Storage tab as a quick-edit, since "None" users mostly come here to configure those.
    private TextNode? _noneInfoLabel;
    private TextInputNode? _noneAudioPathInput;
    private CheckboxNode? _noneGdDownloadCheck;
    private TextInputNode? _noneGdLinkInput;
    private NodeBase[]? _noneSectionContent;
    private TextButtonNode? _noneSectionToggle;
    // Collapsible section toggle buttons + content arrays for per-frame visibility control
    private TextButtonNode? _localSectionToggle;
    private NodeBase[]? _localSectionContent;
    private TextButtonNode? _localAdvancedToggle;
    private NodeBase[]? _localAdvancedContent;
    private NodeBase[]? _localPostAdvancedContent;
    private TextButtonNode? _remoteSectionToggle;
    private NodeBase[]? _remoteSectionContent;
    private TextButtonNode? _serviceSectionToggle;
    private NodeBase[]? _serviceSectionContent;

    // Track previous backend visibility state to recalculate layout only on change
    private bool _prevShowLocal;
    private bool _prevShowRemote;
    private bool _prevShowService;
    private bool _prevShowNone;

    internal Action? OnToggleVoiceClipManager;
    internal Action? OnToggleGameDataTools;

    private readonly IDatabaseService _db;

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
        IGameObjectService gameObjects,
        IVoiceTestService voiceTest,
        IDatabaseService db)
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
        _voiceTest = voiceTest;
        _db = db;

        _backend.CharacterMapped += OnCharacterMapped;
        _backend.VoicesMapped += OnVoicesMapped;
    }

    public override void Dispose()
    {
        _backend.CharacterMapped -= OnCharacterMapped;
        _backend.VoicesMapped -= OnVoicesMapped;
        base.Dispose();
    }

    private void OnCharacterMapped()
    {
        _vsNpcNeedRebuild = true;
        _vsPlayerNeedRebuild = true;
        _vsBubbleNeedRebuild = true;
    }

    private void OnVoicesMapped()
    {
        _vsVoicesNeedRebuild = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Setup
    // ─────────────────────────────────────────────────────────────────────────

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var pos  = ContentStartPosition;
        var size = ContentSize;
        const float tabH = 32f;
        const float bottomBtnSize = 28f;
        const float bottomBtnGap = 4f;
        const float bottomRowMargin = 4f;
        var bottomRowY = pos.Y + size.Y - bottomBtnSize;

        // ── Top-level tab bar ────────────────────────────────────────────────
        _topTabBar = new TabBarNode { Size = new Vector2(size.X, tabH), Position = pos };

        // Content area below top tab bar — height shrunk so the tab-spanning bottom button
        // row gets its own dedicated strip and never overlaps tab content on any tab.
        _topContentPos  = pos  + new Vector2(0, tabH + 2);
        _topContentSize = size - new Vector2(0, tabH + 2 + bottomBtnSize + bottomRowMargin);

        // Inner content area (below inner tab bar, used by Settings / Voice Sel / Logs)
        _innerContentPos  = _topContentPos  + new Vector2(0, tabH + 2);
        _innerContentSize = _topContentSize - new Vector2(0, tabH + 2);
        _contentWidth = size.X;

        // ── Settings section ─────────────────────────────────────────────────
        _settingsTabBar = new TabBarNode { Size = new Vector2(size.X, tabH), Position = _topContentPos };

        _settingsPanels[0] = BuildGeneralPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[1] = BuildDialoguePanel(_innerContentPos, _innerContentSize);
        _settingsPanels[2] = BuildChatPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[3] = BuildSaveLoadPanel(_innerContentPos, _innerContentSize);
        _settingsPanels[4] = BuildBackendPanel(_innerContentPos, _innerContentSize);

        _settingsTabBar.AddTab(Loc.S("General"),   () => ShowSettingsPanel(0));
        _settingsTabBar.AddTab(Loc.S("Dialogue"),  () => ShowSettingsPanel(1));
        _settingsTabBar.AddTab(Loc.S("Chat"),      () => ShowSettingsPanel(2));
        _settingsTabBar.AddTab(Loc.S("Storage"),   () => ShowSettingsPanel(3));
        _settingsTabBar.AddTab(Loc.S("Backend"),   () => ShowSettingsPanel(4));

        // Link buttons — positioned top-right, only visible on Settings tab
        const float discordW = 160f;
        const float githubW  = 120f;
        const float btnGap   = 4f;
        var rightEdge = pos.X + size.X;

        _githubButton = Button(Loc.S("Alltalk Github"), githubW,
            () => CMDHelper.OpenUrl(Constants.ALLTALKGITHUBURL));
        _githubButton.Position = new Vector2(rightEdge - githubW, _innerContentPos.Y + 2);

        _discordButton = Button(Loc.S("Join discord server"), discordW,
            () => CMDHelper.OpenUrl(Constants.DISCORDURL));
        _discordButton.Position = new Vector2(rightEdge - githubW - btnGap - discordW, _innerContentPos.Y + 2);

        // ── Voice Selection section ──────────────────────────────────────────
        SetupVoiceSelection();

        // ── Phonetics section ────────────────────────────────────────────────
        SetupPhonetics();

        // ── Logs section ─────────────────────────────────────────────────────
        SetupLogs();

        // ── Top-level tabs ───────────────────────────────────────────────────
        // Initial population reflects the current InstanceType; OnUpdate re-runs this
        // whenever HasLiveGeneration flips so tabs follow mode changes live.
        _topTabsLiveGenSnapshot = _config.Alltalk.HasLiveGeneration;
        BuildTopTabs(_topTabsLiveGenSnapshot.Value);

        // ── Add all nodes to addon ───────────────────────────────────────────
        AddNode(_topTabBar);

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

        // ── Bottom row: tab-spanning shortcut buttons ────────────────────────
        // Always visible regardless of active tab; mirrors the pattern in
        // NativeVoiceClipManagerWindow / NativeGameDataToolsWindow. ImageNode-routed events
        // are mandatory in NativeAddon contexts (only those fire reliably).
        var normalTint = new Vector3(1f, 1f, 1f);
        var hoverTint = new Vector3(1.4f, 1.4f, 1.4f);

        // Voice Clip Manager — UV (112, 28) on Character.tex = ButtonIcon.MusicNote.
        _voiceClipManagerButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = ButtonIcon.MusicNote,
            Tooltip = Loc.S("Open Voice Clip Manager"),
            OnClick = () => OnToggleVoiceClipManager?.Invoke(),
        };
        _voiceClipManagerButton.ImageNode.MultiplyColor = normalTint;
        _voiceClipManagerButton.ImageNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (_voiceClipManagerButton == null) return;
            _voiceClipManagerButton.ImageNode.MultiplyColor = hoverTint;
            _voiceClipManagerButton.ShowTooltip();
        });
        _voiceClipManagerButton.ImageNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (_voiceClipManagerButton == null) return;
            _voiceClipManagerButton.ImageNode.MultiplyColor = normalTint;
            _voiceClipManagerButton.HideTooltip();
        });
        AddNode(_voiceClipManagerButton);

        // Game Data Tools — UV (168, 84) on Character.tex = ButtonIcon.GearCogWithChatBubble.
        _gameDataToolsButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X + bottomBtnSize + bottomBtnGap, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = ButtonIcon.GearCogWithChatBubble,
            Tooltip = Loc.S("Open Game Data Tools window"),
            OnClick = () => OnToggleGameDataTools?.Invoke(),
        };
        _gameDataToolsButton.ImageNode.MultiplyColor = normalTint;
        _gameDataToolsButton.ImageNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (_gameDataToolsButton == null) return;
            _gameDataToolsButton.ImageNode.MultiplyColor = hoverTint;
            _gameDataToolsButton.ShowTooltip();
        });
        _gameDataToolsButton.ImageNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (_gameDataToolsButton == null) return;
            _gameDataToolsButton.ImageNode.MultiplyColor = normalTint;
            _gameDataToolsButton.HideTooltip();
        });
        AddNode(_gameDataToolsButton);

        ShowTopPanel(0);
    }

    private int _activeSettingsTab;

    /// <summary>
    /// (Re)populates the top-level tab bar. Voices + Phonetics are present only when live
    /// generation is available — None mode hides them entirely (the Voice routing they edit
    /// only matters when there's a backend that consumes voice keys). Settings + Logs always
    /// stay because they cover playback / cleanup / debugging that work without a backend.
    /// Order is preserved by Clear()+AddTab — TabBarNode's AddTab always appends, so we can't
    /// insert tabs back at their original position without a full rebuild.
    /// </summary>
    private void BuildTopTabs(bool liveGen)
    {
        if (_topTabBar == null) return;
        _topTabBar.Clear();

        _topTabBar.AddTab(Loc.S("Settings"), () => ShowTopPanel(0));
        if (liveGen)
        {
            _topTabBar.AddTab(Loc.S("Voices"),    () => ShowTopPanel(1));
            _topTabBar.AddTab(Loc.S("Phonetics"), () => ShowTopPanel(2));
        }
        _topTabBar.AddTab(Loc.S("Logs"),     () => ShowTopPanel(3));

        // Restore the previously-active panel when its tab still exists; otherwise snap to
        // Settings. SelectTab updates the radio-button visual state but does NOT fire the
        // OnClick callback, so panel visibility has to be applied separately via ShowTopPanel.
        var keepActive = _activeTopTab == 0 || _activeTopTab == 3
            || (liveGen && (_activeTopTab == 1 || _activeTopTab == 2));
        var targetIndex = keepActive ? _activeTopTab : 0;
        var targetLabel = targetIndex switch
        {
            1 => Loc.S("Voices"),
            2 => Loc.S("Phonetics"),
            3 => Loc.S("Logs"),
            _ => Loc.S("Settings"),
        };
        _topTabBar.SelectTab(targetLabel);
        ShowTopPanel(targetIndex);
    }

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
        ScreenClampHelper.ClampToScreen(addon, Size);

        // Reset delete confirmations after 5 seconds
        if (_wipeCompleted)
        {
            _wipeCompleted = false;
            _wipeRunning = false;
            try
            {
                _npcData.MappedNpcs.Clear();
                _npcData.MappedPlayers.Clear();
            }
            catch { }
            if (_wipeAllButton != null) _wipeAllButton.String = Loc.S("Wipe database & local audio");
            _log.Info(nameof(OnUpdate),
                $"Wiped database and {_wipeDeletedCount} local audio entries.",
                new EKEventId(0, TextSource.None));
        }

        if (_lastDeleteClick.AddSeconds(5) <= DateTime.Now && (_deleteNpcsArmed || _deletePlayersArmed || _deleteBubblesArmed || _wipeAllArmed))
        {
            _deleteNpcsArmed = false;
            _deletePlayersArmed = false;
            _deleteBubblesArmed = false;
            _wipeAllArmed = false;
            if (_clearNpcsButton != null)    _clearNpcsButton.String    = Loc.S("Clear mapped NPCs");
            if (_clearPlayersButton != null) _clearPlayersButton.String = Loc.S("Clear mapped players");
            if (_clearBubblesButton != null) _clearBubblesButton.String = Loc.S("Clear mapped bubbles");
            if (_wipeAllButton != null)      _wipeAllButton.String      = Loc.S("Wipe database & local audio");
        }

        // Backend dropdown deferred selection (same crash-safe pattern as DialogTalkController)
        if (_pendingBackendSelection != null)
        {
            var sel = _pendingBackendSelection;
            _pendingBackendSelection = null;
            ProcessBackendSelection(sel);
        }

        var enabled = _config.Enabled;
        Dim(_globalVolumeSlider, enabled);
        Dim(_generateBySentenceCheck, enabled);
        Dim(_hideUiCheck,             enabled);
        Dim(_showExtraOptionsCheck,   enabled);
        Dim(_removePunctuationCheck,  enabled);

        Dim(_voiceDialogueIn3DCheck, _config.VoiceDialogue);
        Dim(_dialogue3DSlider,       _config.VoiceDialogue && _config.VoiceDialogueIn3D);

        Dim(_voiceBattleQueuedCheck, _config.VoiceBattleDialogue);

        // None-mode dim for live-only widgets — Battle dialogue + Bubble toggles deliberately
        // stay interactive even without a backend (Echokraut will play whatever pre-existing
        // audio the user has and silently skip the rest, so the toggles still control which
        // categories play). Player-choice routing, retainer dialogue, voice chat, and alias
        // auto-generation have no audio-file fallback path — they need a live backend.
        var liveGen = _config.Alltalk.HasLiveGeneration;
        Dim(_voicePlayerCutsceneCheck, liveGen);
        Dim(_voicePlayerChoicesCheck,  liveGen);
        Dim(_voiceRetainersCheck,      liveGen);
        Dim(_voiceChatCheck,           liveGen);
        Dim(_autoAliasCheck,           liveGen);

        // Chat sub-widgets cascade off both the master VoiceChat toggle AND liveGen so the
        // existing "dim when master off" behaviour also kicks in when the user is in None
        // mode (master itself is dimmed in that case).
        var voiceChat = _config.VoiceChat && liveGen;
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

        // Top tab bar follows live-generation availability. None mode → Voices + Phonetics
        // disappear (they only configure the routing for backend generation). Tab rebuild
        // is gated on a transition so we don't dispose+recreate radio buttons every frame.
        if (_topTabsLiveGenSnapshot != liveGen)
        {
            _topTabsLiveGenSnapshot = liveGen;
            BuildTopTabs(liveGen);
        }

        // Game Data Tools button is visually dimmed when live generation is unavailable.
        // The actual click is no-op-guarded at NativeWindowManager.ToggleGameDataTools()
        // (defense in depth — the button can only do harm if both gates fail).
        Dim(_gameDataToolsButton, liveGen);

        // Mode switcher — selected button stays bright, others dim. Block all clicks while
        // an install is in progress (mid-install switch would race on alltalkFolder).
        Dim(_modeLocalBtn,  !installing && isLocal);
        Dim(_modeRemoteBtn, !installing && isRemote);
        Dim(_modeNoneBtn,   !installing && isNone);

        // Show/hide entire collapsible sections based on instance type. Service options
        // (Streaming, ReloadModel, ReloadVoices) are Local-or-Remote only — None has no
        // backend to talk to, so the runtime knobs don't apply.
        var showLocal   = isAlltalk && isLocal;
        var showRemote  = isAlltalk && isRemote;
        var showNone    = isAlltalk && isNone;
        var showService = isAlltalk && (isLocal || isRemote);

        // Local section: toggle button + content
        SetVisible(_localSectionToggle, showLocal);
        if (!showLocal && _localSectionContent != null)
            foreach (var n in _localSectionContent) SetVisible(n, false);
        SetVisible(_localAdvancedToggle, showLocal);
        if (!showLocal && _localAdvancedContent != null)
            foreach (var n in _localAdvancedContent) SetVisible(n, false);
        if (_localPostAdvancedContent != null)
            foreach (var n in _localPostAdvancedContent) SetVisible(n, showLocal);
        if (showLocal) _atLocalNodes?.Update(_config, _alltalkInstance);

        // Remote section: toggle button + content
        SetVisible(_remoteSectionToggle, showRemote);
        if (!showRemote && _remoteSectionContent != null)
            foreach (var n in _remoteSectionContent) SetVisible(n, false);

        // None section: toggle button + content (info text + audio path + GD link)
        SetVisible(_noneSectionToggle, showNone);
        if (!showNone && _noneSectionContent != null)
            foreach (var n in _noneSectionContent) SetVisible(n, false);
        // GD link follows GoogleDriveDownload toggle even within the None section
        if (showNone) Dim(_noneGdLinkInput, _config.GoogleDriveDownload);

        // Service section: toggle button + content
        SetVisible(_serviceSectionToggle, showService);
        if (!showService && _serviceSectionContent != null)
            foreach (var n in _serviceSectionContent) SetVisible(n, false);

        // Recalculate backend panel layout when visibility changes
        if (showLocal != _prevShowLocal || showRemote != _prevShowRemote
            || showNone != _prevShowNone || showService != _prevShowService)
        {
            _prevShowLocal = showLocal;
            _prevShowRemote = showRemote;
            _prevShowNone = showNone;
            _prevShowService = showService;
            _settingsPanels[4]?.RecalculateLayout();
        }

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

        var enabledCheck = Check(Loc.S("Enabled"), 120, _config.Enabled,
            v => { _config.Enabled = v; _config.Save(); });
        _globalVolumeSlider = new SliderNode
        {
            Size = new Vector2(180, 20),
            Range = 0..200,
            DecimalPlaces = 2,
            Value = (int)(_config.GlobalVolume * 100),
        };
        _globalVolumeSlider.OnValueChanged = v =>
        {
            _config.GlobalVolume = v / 100.0f;
            _config.Save();
        };
        var enabledRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        enabledRow.AddNode(enabledCheck);
        enabledRow.AddNode(_globalVolumeSlider);

        _generateBySentenceCheck = Check(
            Loc.S("Generate per sentence (shorter latency, recommended for CPU inference)"), w,
            _config.GenerateBySentence,
            v => { _config.GenerateBySentence = v; _config.Save(); });

        var removeStuttersCheck = Check(Loc.S("Remove stutters"), w, _config.RemoveStutters,
            v => { _config.RemoveStutters = v; _config.Save(); });

        _hideUiCheck = Check(Loc.S("Hide UI in cutscenes"), w, _config.HideUiInCutscenes,
            v => { _config.HideUiInCutscenes = v; _config.Save(); });

        _showExtraOptionsCheck = Check(
            Loc.S("Show Play/Pause, Stop and Mute buttons in dialogue"), w,
            _config.ShowExtraOptionsInDialogue,
            v => { _config.ShowExtraOptionsInDialogue = v; _config.Save(); });

        _removePunctuationCheck = Check(
            Loc.S("Remove punctuation (may reduce speech hallucinations)"), w,
            _config.RemovePunctuation,
            v => { _config.RemovePunctuation = v; _config.Save(); });

        // Unrecoverable actions
        _clearNpcsButton = Button(Loc.S("Clear mapped NPCs"), 160, () =>
        {
            if (_deleteNpcsArmed)
            {
                _deleteNpcsArmed = false;
                _clearNpcsButton!.String = Loc.S("Clear mapped NPCs");
                foreach (var npc in _npcData.MappedNpcs.FindAll(p => !p.Name.StartsWith("BB")))
                {
                    _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, npc.Name);
                    _npcData.RemoveCharacter(npc);
                    _npcData.MappedNpcs.Remove(npc);
                }
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _deleteNpcsArmed = true;
                _clearNpcsButton!.String = Loc.S("Confirm clear NPCs!");
            }
        });
        _clearPlayersButton = Button(Loc.S("Clear mapped players"), 160, () =>
        {
            if (_deletePlayersArmed)
            {
                _deletePlayersArmed = false;
                _clearPlayersButton!.String = Loc.S("Clear mapped players");
                foreach (var p in _npcData.MappedPlayers)
                {
                    _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, p.Name);
                    _npcData.RemoveCharacter(p);
                    _npcData.MappedPlayers.Remove(p);
                }
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _deletePlayersArmed = true;
                _clearPlayersButton!.String = Loc.S("Confirm clear players!");
            }
        });
        _clearBubblesButton = Button(Loc.S("Clear mapped bubbles"), 160, () =>
        {
            if (_deleteBubblesArmed)
            {
                _deleteBubblesArmed = false;
                _clearBubblesButton!.String = Loc.S("Clear mapped bubbles");
                foreach (var npc in _npcData.MappedNpcs.FindAll(p => p.Name.StartsWith("BB")))
                {
                    _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, npc.Name);
                    _npcData.RemoveCharacter(npc);
                    _npcData.MappedNpcs.Remove(npc);
                }
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _deleteBubblesArmed = true;
                _clearBubblesButton!.String = Loc.S("Confirm clear bubbles!");
            }
        });
        _wipeAllButton = Button(Loc.S("Wipe database & local audio"), 220, () =>
        {
            if (_wipeRunning) return;
            if (_wipeAllArmed)
            {
                _wipeAllArmed = false;
                _wipeRunning = true;
                _wipeAllButton!.String = Loc.S("Wiping...");
                _audioPlayback.ClearQueue();

                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        Thread.Sleep(100); // let BASS release file handles
                        var deleted = _audioFiles.RemoveAllSavedFiles(_config.LocalSaveLocation);
                        _db.WipeAll();
                        _wipeDeletedCount = deleted;
                    }
                    catch (Exception ex)
                    {
                        _log.Error(nameof(BuildGeneralPanel), $"Wipe failed: {ex}", new EKEventId(0, TextSource.None));
                    }
                    finally
                    {
                        _wipeCompleted = true;
                    }
                });
            }
            else
            {
                _lastDeleteClick = DateTime.Now;
                _wipeAllArmed = true;
                _wipeAllButton!.String = Loc.S("Confirm wipe everything!");
            }
        });

        var reloadRemoteButton = Button(Loc.S("Reload remote mappings"), 180,
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
        _wipeAllButton!.Size      = new Vector2(innerW, 28);

        var row1 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row1.AddNode(_clearNpcsButton);
        row1.AddNode(_clearPlayersButton);
        var row2 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row2.AddNode(_clearBubblesButton);
        row2.AddNode(reloadRemoteButton);

        list.AddNode(enabledRow);
        list.AddNode(_generateBySentenceCheck);
        list.AddNode(removeStuttersCheck);
        list.AddNode(_removePunctuationCheck);
        list.AddNode(_hideUiCheck);

        CreateCollapsibleSection(list, Loc.S("In-Game Controls"), w, true,
            [_showExtraOptionsCheck]);

        CreateCollapsibleSection(list, Loc.S("Reset Data"), w, true, [row1, row2, _wipeAllButton]);

        CreateCollapsibleSection(list, Loc.S("Available commands"), w, true,
            commandNodes.Where(n => n != null).Cast<NodeBase>().ToArray());

        // Voice Clip Manager + Game Data Tools moved to tab-spanning circle buttons at the
        // bottom-left of the window — same affordance as the other plugin windows.
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dialogue tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildDialoguePanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var voiceDialogueCheck = Check(Loc.S("Voice dialogue"), w, _config.VoiceDialogue,
            v => { _config.VoiceDialogue = v; _config.Save(); });

        _voiceDialogueIn3DCheck = Check(Loc.S("Voice dialogue in 3D space"), w, _config.VoiceDialogueIn3D,
            v => { _config.VoiceDialogueIn3D = v; _config.Save(); });

        _dialogue3DSlider = Slider(w, _config.Voice3DAudibleRange,
            v => { _config.Voice3DAudibleRange = v; _config.Save(); _audioPlayback.Update3DFactors(v); });

        _voicePlayerCutsceneCheck = Check(
            Loc.S("Voice player choices in cutscenes"), w, _config.VoicePlayerChoicesCutscene,
            v => { _config.VoicePlayerChoicesCutscene = v; _config.Save(); });

        _voicePlayerChoicesCheck = Check(
            Loc.S("Voice player choices outside cutscenes"), w, _config.VoicePlayerChoices,
            v => { _config.VoicePlayerChoices = v; _config.Save(); });

        var cancelAdvanceCheck = Check(Loc.S("Cancel voice on text advance"), w,
            _config.CancelSpeechOnTextAdvance,
            v => { _config.CancelSpeechOnTextAdvance = v; _config.Save(); });

        var autoAdvanceCheck = Check(Loc.S("Auto-advance dialogue after speech completes"), w,
            _config.AutoAdvanceTextAfterSpeechCompleted,
            v => { _config.AutoAdvanceTextAfterSpeechCompleted = v; _config.Save(); });

        _voiceRetainersCheck = Check(Loc.S("Voice retainer dialogue"), w, _config.VoiceRetainers,
            v => { _config.VoiceRetainers = v; _config.Save(); });

        // Battle dialogue
        var voiceBattleCheck = Check(Loc.S("Voice battle dialogue"), w, _config.VoiceBattleDialogue,
            v => { _config.VoiceBattleDialogue = v; _config.Save(); });

        _voiceBattleQueuedCheck = Check(Loc.S("Queue battle dialogue"), w, _config.VoiceBattleDialogQueued,
            v => { _config.VoiceBattleDialogQueued = v; _config.Save(); });

        // NPC Bubbles
        var voiceBubblesCheck = Check(Loc.S("Voice NPC bubbles"), w, _config.VoiceBubble,
            v => { _config.VoiceBubble = v; _config.Save(); });

        _voiceBubblesInCityCheck = Check(Loc.S("Voice bubbles in cities"), w, _config.VoiceBubblesInCity,
            v => { _config.VoiceBubblesInCity = v; _config.Save(); });

        _voiceSourceCamCheck = Check(Loc.S("Use camera as 3D sound source"), w, _config.VoiceSourceCam,
            v => { _config.VoiceSourceCam = v; _config.Save(); });

        _bubbles3DSlider = Slider(w, _config.Voice3DAudibleRange,
            v => { _config.Voice3DAudibleRange = v; _config.Save(); _audioPlayback.Update3DFactors(v); });

        list.AddNode(voiceDialogueCheck);
        list.AddNode(_voiceDialogueIn3DCheck);
        list.AddNode(_dialogue3DSlider);
        list.AddNode(Separator(w));
        list.AddNode(_voicePlayerCutsceneCheck);
        list.AddNode(_voicePlayerChoicesCheck);
        list.AddNode(Separator(w));
        list.AddNode(cancelAdvanceCheck);
        list.AddNode(autoAdvanceCheck);
        list.AddNode(_voiceRetainersCheck);

        CreateCollapsibleSection(list, Loc.S("Battle Dialogue"), w, true,
            [voiceBattleCheck, _voiceBattleQueuedCheck]);

        CreateCollapsibleSection(list, Loc.S("NPC Bubbles"), w, true,
            [voiceBubblesCheck, _voiceBubblesInCityCheck, _voiceSourceCamCheck, _bubbles3DSlider]);

        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Chat tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildChatPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        _voiceChatCheck = Check(Loc.S("Voice chat"), w, _config.VoiceChat,
            v => { _config.VoiceChat = v; _config.Save(); });

        _chatApiKeyInput = Input(Loc.S("Detect Language API key (detectlanguage.com)"), w, 32,
            _config.VoiceChatLanguageAPIKey,
            v => { _config.VoiceChatLanguageAPIKey = v; _config.Save(); });

        _voiceChatIn3DCheck = Check(Loc.S("Voice chat in 3D space"), w, _config.VoiceChatIn3D,
            v => { _config.VoiceChatIn3D = v; _config.Save(); });

        _chat3DSlider = Slider(w, _config.Voice3DAudibleRange,
            v => { _config.Voice3DAudibleRange = v; _config.Save(); _audioPlayback.Update3DFactors(v); });

        _voiceChatPlayerCheck        = Check(Loc.S("Voice your own chat"),                      w, _config.VoiceChatPlayer,         v => { _config.VoiceChatPlayer         = v; _config.Save(); });
        _voiceChatSayCheck           = Check(Loc.S("Voice Say"),                                w, _config.VoiceChatSay,            v => { _config.VoiceChatSay            = v; _config.Save(); });
        _voiceChatYellCheck          = Check(Loc.S("Voice Yell"),                               w, _config.VoiceChatYell,           v => { _config.VoiceChatYell           = v; _config.Save(); });
        _voiceChatShoutCheck         = Check(Loc.S("Voice Shout"),                              w, _config.VoiceChatShout,          v => { _config.VoiceChatShout          = v; _config.Save(); });
        _voiceChatFCCheck            = Check(Loc.S("Voice Free Company"),                       w, _config.VoiceChatFreeCompany,    v => { _config.VoiceChatFreeCompany    = v; _config.Save(); });
        _voiceChatTellCheck          = Check(Loc.S("Voice Tell"),                               w, _config.VoiceChatTell,           v => { _config.VoiceChatTell           = v; _config.Save(); });
        _voiceChatPartyCheck         = Check(Loc.S("Voice Party"),                              w, _config.VoiceChatParty,          v => { _config.VoiceChatParty          = v; _config.Save(); });
        _voiceChatAllianceCheck      = Check(Loc.S("Voice Alliance"),                           w, _config.VoiceChatAlliance,       v => { _config.VoiceChatAlliance       = v; _config.Save(); });
        _voiceChatNoviceCheck        = Check(Loc.S("Voice Novice Network"),                     w, _config.VoiceChatNoviceNetwork,  v => { _config.VoiceChatNoviceNetwork  = v; _config.Save(); });
        _voiceChatLinkshellCheck     = Check(Loc.S("Voice linkshells"),                         w, _config.VoiceChatLinkshell,      v => { _config.VoiceChatLinkshell      = v; _config.Save(); });
        _voiceChatCrossLinkshellCheck= Check(Loc.S("Voice cross-world linkshells"),   w, _config.VoiceChatCrossLinkshell, v => { _config.VoiceChatCrossLinkshell = v; _config.Save(); });

        list.AddNode(_voiceChatCheck);
        list.AddNode(_chatApiKeyInput);

        CreateCollapsibleSection(list, Loc.S("3D Space"), w, true,
            [_voiceChatIn3DCheck, _chat3DSlider]);

        CreateCollapsibleSection(list, Loc.S("Chat channels"), w, false,
            [_voiceChatPlayerCheck, _voiceChatSayCheck, _voiceChatYellCheck,
             _voiceChatShoutCheck, _voiceChatFCCheck, _voiceChatTellCheck,
             _voiceChatPartyCheck, _voiceChatAllianceCheck, _voiceChatNoviceCheck,
             _voiceChatLinkshellCheck, _voiceChatCrossLinkshellCheck]);

        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Storage tab
    // ─────────────────────────────────────────────────────────────────────────

    private ScrollingListNode BuildSaveLoadPanel(Vector2 pos, Vector2 size)
    {
        var w    = size.X;
        var list = Panel(pos, size);

        var loadLocalFirst = Check(Loc.S("Search for audio locally before generating"), w,
            _config.LoadFromLocalFirst, v => { _config.LoadFromLocalFirst = v; _config.Save(); });
        var saveLocally = Check(Loc.S("Save generated audio locally"), w, _config.SaveToLocal,
            v => { _config.SaveToLocal = v; _config.Save(); });
        _createMissingDirCheck = Check(Loc.S("Create directory if missing"), w,
            _config.CreateMissingLocalSaveLocation,
            v => { _config.CreateMissingLocalSaveLocation = v; _config.Save(); });
        _localPathInput = Input(Loc.S("Local audio directory path"), w, 260, _config.LocalSaveLocation,
            v => { _config.LocalSaveLocation = v; _config.Save(); });
        _autoAliasCheck = Check(Loc.S("Auto-generate shareable alias variants for player-name dialog"), w,
            _config.AutoGenerateShareableAliases,
            v => { _config.AutoGenerateShareableAliases = v; _config.Save(); });

        // Google Drive
        var gdRequestVoiceLine = Check(
            Loc.S("Send dialogue lines to Ren Nagasaki's share for a voice line database"), w,
            _config.GoogleDriveRequestVoiceLine,
            v =>
            {
                _config.GoogleDriveRequestVoiceLine = v;
                _config.Save();
                if (v) _ = _googleDrive.CreateDriveServicePkceAsync();
            });

        _gdUploadCheck = Check(Loc.S("Upload to Google Drive (requires local save)"), w,
            _config.GoogleDriveUpload,
            v => { _config.GoogleDriveUpload = v; _config.Save(); });

        var gdDownload = Check(Loc.S("Download from Google Drive share"), w, _config.GoogleDriveDownload,
            v => { _config.GoogleDriveDownload = v; _config.Save(); });
        _gdDownloadPeriodicCheck = Check(
            Loc.S("Download periodically (every 60 min, new files only)"), w,
            _config.GoogleDriveDownloadPeriodically,
            v => { _config.GoogleDriveDownloadPeriodically = v; _config.Save(); });
        _gdShareLinkInput = Input(Loc.S("Google Drive share link"), w, 260, _config.GoogleDriveShareLink,
            v => { _config.GoogleDriveShareLink = v; _config.Save(); });
        _gdDownloadNowButton = Button(Loc.S("Download now"), 120, () =>
            _googleDrive.DownloadFolder(_config.LocalSaveLocation, _config.GoogleDriveShareLink));

        list.AddNode(loadLocalFirst);
        list.AddNode(saveLocally);
        list.AddNode(_createMissingDirCheck);
        list.AddNode(_localPathInput);
        list.AddNode(_autoAliasCheck);

        CreateCollapsibleSection(list, Loc.S("Google Drive"), w, false,
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

        // Mode switcher — 3 buttons styled like FTU Step 0's choices. Click switches
        // InstanceType live; OnUpdate then dims the inactive buttons and toggles which
        // section (Local / Remote / None) is shown below.
        var modeBtnW = (w - 8) / 3;
        _modeLocalBtn = ModeButton(Loc.S("Local TTS"), modeBtnW, () =>
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.Local;
            _config.Save();
        });
        _modeRemoteBtn = ModeButton(Loc.S("Remote Server"), modeBtnW, () =>
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.Remote;
            _config.Save();
        });
        _modeNoneBtn = ModeButton(Loc.S("Audio Files Only"), modeBtnW, () =>
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.None;
            _config.Save();
        });
        _modeSwitcherRow = new HorizontalListNode { Size = new Vector2(w, 30), ItemSpacing = 4 };
        _modeSwitcherRow.AddNode(_modeLocalBtn);
        _modeSwitcherRow.AddNode(_modeRemoteBtn);
        _modeSwitcherRow.AddNode(_modeNoneBtn);

        // Local instance (shared builder)
        _atLocalNodes = NativeAlltalkBuilder.BuildLocalInstance(w, _config, _alltalkInstance);

        // Remote instance (shared builder)
        _atRemoteNodes = NativeAlltalkBuilder.BuildRemoteInstance(w, _config, _backend);
        _atRemoteNodes.TestConnectionButton.OnClick = () => TestConnection();

        // None-mode section — info text + audio path + Google Drive download. The path
        // input and GD widgets are intentionally duplicates of the same Configuration
        // properties on the Storage tab; both edit the same backing fields, so a change
        // in either tab syncs immediately. Surface them here too because users in None
        // mode come to the Backend tab to configure their audio source.
        _noneInfoLabel = new TextNode
        {
            Size = new Vector2(w, 36),
            String = Loc.S("No live generation. Echokraut will only play pre-existing audio files."),
            FontType = FontType.Axis,
            FontSize = 12,
        };
        _noneInfoLabel.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
        _noneAudioPathInput = Input(Loc.S("Local audio directory"), w, 260,
            _config.LocalSaveLocation,
            v => { _config.LocalSaveLocation = v; _config.Save(); });
        _noneGdDownloadCheck = Check(Loc.S("Download from Google Drive"), w,
            _config.GoogleDriveDownload,
            v => { _config.GoogleDriveDownload = v; _config.Save(); });
        _noneGdLinkInput = Input(Loc.S("Google Drive share link"), w, 100,
            _config.GoogleDriveShareLink,
            v => { _config.GoogleDriveShareLink = v; _config.Save(); });

        // Service options (shared between local & remote)
        _atStreamingCheck = Check(Loc.S("Streaming generation (play audio before full text is generated)"), w,
            _config.Alltalk.StreamingGeneration,
            v => { _config.Alltalk.StreamingGeneration = v; _config.Save(); });
        _atReloadModelInput = Input(Loc.S("Model name to reload"), w, 40, _config.Alltalk.ReloadModel,
            v => { _config.Alltalk.ReloadModel = v; _config.Save(); });
        _atReloadModelButton = Button(Loc.S("Reload model"), 100, () =>
            _backend.ReloadService(_config.Alltalk.ReloadModel, new EKEventId(0, TextSource.None)));
        _atReloadVoicesButton = Button(Loc.S("Reload voices"), 100, () =>
        {
            _backend.SetBackendType(_config.BackendSelection);
            _backend.NotifyCharacterMapped();
        });
        _atReloadRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        _atReloadRow.AddNode(_atReloadModelButton);
        _atReloadRow.AddNode(_atReloadVoicesButton);

        list.AddNode(_backendDropDown);
        list.AddNode(_modeSwitcherRow);

        _localSectionContent = _atLocalNodes.EssentialNodes;
        _localSectionToggle = CreateCollapsibleSection(list, Loc.S("Local instance"), w, false, _localSectionContent);

        _localAdvancedContent = _atLocalNodes.AdvancedNodes;
        _localAdvancedToggle = CreateCollapsibleSection(list, Loc.S("Advanced options"), w, true, _localAdvancedContent);

        _localPostAdvancedContent = _atLocalNodes.PostAdvancedNodes;
        foreach (var n in _localPostAdvancedContent) list.AddNode(n);

        _remoteSectionContent = _atRemoteNodes.AllNodes;
        _remoteSectionToggle = CreateCollapsibleSection(list, Loc.S("Remote connection"), w, false, _remoteSectionContent);

        _noneSectionContent = [_noneInfoLabel, _noneAudioPathInput, _noneGdDownloadCheck, _noneGdLinkInput];
        _noneSectionToggle = CreateCollapsibleSection(list, Loc.S("Audio Files Only"), w, false, _noneSectionContent);

        _serviceSectionContent = [_atStreamingCheck, _atReloadModelInput, _atReloadRow];
        _serviceSectionToggle = CreateCollapsibleSection(list, Loc.S("Service options"), w, true, _serviceSectionContent);

        return list;
    }

    /// <summary>
    /// Build a mode-switcher button styled to look like a small radio. The selected/active
    /// state is conveyed via Alpha (1.0 active, 0.4 inactive) — set per frame in OnUpdate
    /// since InstanceType is the source of truth and may flip from the FTU window or another
    /// code path. Cheaper than tracking transitions; Alpha writes are idempotent.
    /// </summary>
    private static TextButtonNode ModeButton(string label, float width, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(width, 28), String = label };
        var textW = node.LabelNode.GetTextDrawSize(label).X + 36;
        if (textW > width) node.Size = new Vector2(textW, 28);
        node.OnClick = onClick;
        return node;
    }

    private void TestConnection()
    {
        if (_atRemoteNodes == null) return;
        _atRemoteNodes.ConnectionResultLabel.String = "Testing...";

        var task = _config.BackendSelection == TTSBackends.Alltalk
            ? _backend.CheckReady(new EKEventId(0, TextSource.None))
            : System.Threading.Tasks.Task.FromResult("No backend selected");

        task.ContinueWith(t =>
        {
            if (_atRemoteNodes == null) return;
            _atRemoteNodes.ConnectionResultLabel.String = t.IsFaulted
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

    private static TextNode HeaderLabel(string text, float width)
    {
        var node = Label(text, width);
        node.AddTextFlags(TextFlags.Ellipsis);
        return node;
    }

    private static TextButtonNode Button(string label, float minWidth, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(minWidth, 24), String = label };
        var textW = node.LabelNode.GetTextDrawSize(label).X + 36;
        if (textW > minWidth) node.Size = new Vector2(textW, 24);
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
