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
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using EKConfig = Echokraut.DataClasses.Configuration;

using static Echokraut.Windows.Native.NativeNodeFactory;
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
    private readonly IEchokrauTtsInstanceService _echokrauTtsInstance;
    private readonly ITtsVoiceSyncService _voiceSync;
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
    // Live-generation snapshot for the settings sub-tab bar (Chat tab disappears in None
    // mode — chat TTS is purely live-per-line, has no audio-file fallback like dialogue
    // does, so the whole tab is meaningless without a backend).
    private bool? _settingsTabsLiveGenSnapshot;

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
    private StringDropDownNode? _backendDropDown;
    private string? _pendingBackendSelection;

    // Set from the (background) connection-test continuation, processed on the main thread in
    // OnUpdate: after a successful reconnect we must remap voices + refresh selectables so mapped
    // NPCs pick up a non-stale selectable list (see TestConnection).
    private volatile bool _pendingRemapAfterConnect;

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
    // EchokrauTTS engine sections (shown when BackendSelection == EchokrauTTS).
    private NativeEchokrauTtsBuilder.LocalInstanceNodes? _ekLocalNodes;
    private NativeEchokrauTtsBuilder.RemoteInstanceNodes? _ekRemoteNodes;
    private NodeBase[]? _ekLocalSectionContent;
    private TextButtonNode? _ekLocalSectionToggle;
    private bool _ekLocalExpanded;
    private NodeBase[]? _ekRemoteSectionContent;
    private TextButtonNode? _ekRemoteSectionToggle;
    private bool _ekRemoteExpanded;
    private bool _prevShowEkLocal, _prevShowEkRemote;
    // Streaming checkbox + Reload-Voices button used to live in the Backend tab's "Service
    // options" section. Streaming moved to the General tab as a top-level generation toggle
    // (dims in None mode); Reload-Voices moved into General → Reset Data alongside the
    // other DB-affecting actions. Reload-Model + its input field were dropped entirely —
    // it's a niche AllTalk re-init hook the average user never touches and could just as
    // well be triggered via /api directly. The whole "Service options" collapsible is gone.
    private CheckboxNode? _atStreamingCheck;
    private TextButtonNode? _atReloadVoicesButton;
    // None-mode section content — audio path + Google Drive download settings duplicated from
    // the Storage tab as a quick-edit, since "None" users mostly come here to configure those.
    private TextNode? _noneInfoLabel;
    private TextInputNode? _noneAudioPathInput;
    private CheckboxNode? _noneGdDownloadCheck;
    private TextInputNode? _noneGdLinkInput;
    private NodeBase[]? _noneSectionContent;
    private TextButtonNode? _noneSectionToggle;
    // Collapsible section toggle buttons + content arrays for per-frame visibility control.
    // Expanded state is tracked explicitly per section instead of derived from
    // contentNodes[0].IsVisible — OnUpdate also writes IsVisible to hide whole sections in
    // off-modes, so the IsVisible bit is no longer a reliable expanded-state proxy. Without
    // explicit tracking, switching None→Local left the Local toggle reading "[-]" while its
    // content was force-hidden, with the post-advanced (Install/Start-Stop) row visible and
    // the section essentials missing — clicking the toggle then "did nothing visually" since
    // the arrow was already "[-]".
    private TextButtonNode? _localSectionToggle;
    private NodeBase[]? _localSectionContent;
    private bool _localExpanded = true;             // built with startCollapsed=false
    private TextButtonNode? _localAdvancedToggle;
    private NodeBase[]? _localAdvancedContent;
    private bool _localAdvancedExpanded;            // built with startCollapsed=true
    private NodeBase[]? _localPostAdvancedContent;
    private TextButtonNode? _remoteSectionToggle;
    private NodeBase[]? _remoteSectionContent;
    private bool _remoteExpanded = true;            // built with startCollapsed=false
    private bool _noneExpanded = true;              // built with startCollapsed=false

    // Track previous backend visibility state to recalculate layout only on change
    private bool _prevShowLocal;
    private bool _prevShowRemote;
    private bool _prevShowNone;

    // Game-Data-Tools icon button "enabled" snapshot — used by SyncIconButton to gate
    // NodeFlags toggles on transitions (per CLAUDE.md, AddNodeFlags/RemoveNodeFlags can
    // crash if hit every frame; only transitions are safe).
    private bool? _gameDataBtnEnabledState;

    internal Action? OnToggleVoiceClipManager;
    internal Action? OnToggleGameDataTools;

    private readonly IDatabaseService _db;
    private readonly IBatchModeService _batchMode;

    public NativeConfigWindow(
        EKConfig config,
        IAudioPlaybackService audioPlayback,
        IBackendService backend,
        IGoogleDriveSyncService googleDrive,
        IAlltalkInstanceService alltalkInstance,
        IEchokrauTtsInstanceService echokrauTtsInstance,
        ITtsVoiceSyncService voiceSync,
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
        IDatabaseService db,
        IBatchModeService batchMode)
    {
        _config = config;
        _audioPlayback = audioPlayback;
        _backend = backend;
        _googleDrive = googleDrive;
        _alltalkInstance = alltalkInstance;
        _echokrauTtsInstance = echokrauTtsInstance;
        _voiceSync = voiceSync;
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
        _batchMode = batchMode;

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
        // OnSetup re-runs on every (re)open with a fresh, fully-expanded backend panel, but the
        // per-frame section-visibility recalc is transition-gated on these instance fields, which
        // survive close/reopen. Reset them so the first post-open OnUpdate sees a transition and
        // compacts the layout — otherwise the active section (EchokrauTTS is added last) stays
        // pushed below the viewport by the now-hidden sections above it and appears to vanish.
        _prevShowLocal = _prevShowRemote = _prevShowNone = _prevShowEkLocal = _prevShowEkRemote = false;

        _settingsTabsLiveGenSnapshot = _config.HasLiveGeneration;
        BuildSettingsTabs(_settingsTabsLiveGenSnapshot.Value);

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
        _topTabsLiveGenSnapshot = _config.HasLiveGeneration;
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
        // Voice Clip Manager — UV (112, 28) on Character.tex = CircleButtonIcon.MusicNote.
        _voiceClipManagerButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = CircleButtonIcon.MusicNote,
            Tooltip = Loc.S("Open Voice Clip Manager"),
            OnClick = () => OnToggleVoiceClipManager?.Invoke(),
        };
        WireIconButtonHover(_voiceClipManagerButton, () => _voiceClipManagerButton != null,
            _voiceClipManagerButton.ShowTooltip, _voiceClipManagerButton.HideTooltip);
        AddNode(_voiceClipManagerButton);

        // Game Data Tools — UV (168, 84) on Character.tex = CircleButtonIcon.GearCogWithChatBubble.
        _gameDataToolsButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X + bottomBtnSize + bottomBtnGap, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = CircleButtonIcon.GearCogWithChatBubble,
            Tooltip = Loc.S("Open Game Data Tools window"),
            OnClick = () => OnToggleGameDataTools?.Invoke(),
        };
        // Hover gates on IsEnabled: in None-mode the button is disabled and its dimmed look is
        // owned by ATK's disabled timeline (alpha 178 + multiplier 0.5). Bailing on disabled in
        // BOTH over and out stops a cursor sweep from resetting MultiplyColor to (1,1,1) and
        // snapping the icon brighter than its disabled state. A stuck hover from an enabled→
        // disabled transition while hovered is force-cleared by the OnUpdate transition handler.
        WireIconButtonHover(_gameDataToolsButton,
            () => _gameDataToolsButton != null && _gameDataToolsButton.IsEnabled,
            _gameDataToolsButton.ShowTooltip, _gameDataToolsButton.HideTooltip);
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

    /// <summary>
    /// (Re)populates the inner Settings tab bar. Chat is omitted in None mode — chat TTS is
    /// purely live-per-line, has no audio-file fallback path the way dialogue does, so the
    /// whole tab has nothing to control without a backend. General / Dialogue / Storage /
    /// Backend always stay because they cover playback controls, file paths, and the mode
    /// switcher itself (which is how the user gets out of None mode).
    /// </summary>
    private void BuildSettingsTabs(bool liveGen)
    {
        if (_settingsTabBar == null) return;
        _settingsTabBar.Clear();

        _settingsTabBar.AddTab(Loc.S("General"),  () => ShowSettingsPanel(0));
        _settingsTabBar.AddTab(Loc.S("Dialogue"), () => ShowSettingsPanel(1));
        if (liveGen)
            _settingsTabBar.AddTab(Loc.S("Chat"), () => ShowSettingsPanel(2));
        _settingsTabBar.AddTab(Loc.S("Storage"),  () => ShowSettingsPanel(3));
        _settingsTabBar.AddTab(Loc.S("Backend"),  () => ShowSettingsPanel(4));

        // Restore the previously-active sub-tab when its tab still exists; otherwise (the
        // user was on Chat and we just hid it) snap to General. Panel indices stay the
        // same regardless of which tabs are present — only the tab bar itself shrinks.
        var keepActive = _activeSettingsTab != 2 || liveGen;
        var targetIndex = keepActive ? _activeSettingsTab : 0;
        var targetLabel = targetIndex switch
        {
            1 => Loc.S("Dialogue"),
            2 => Loc.S("Chat"),
            3 => Loc.S("Storage"),
            4 => Loc.S("Backend"),
            _ => Loc.S("General"),
        };
        _settingsTabBar.SelectTab(targetLabel);
        // Only force-show the settings panel if we're actually on the Settings top section.
        // Otherwise (e.g. user is on Logs and a mode flip rebuilds tabs) this would force
        // a settings panel visible on top of the active section. Update the index anyway so
        // a later ShowTopPanel(0) lands on the right sub-tab.
        if (_activeTopTab == 0)
            ShowSettingsPanel(targetIndex);
        else
            _activeSettingsTab = targetIndex;
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

        // Deferred voice remap after a successful connection test (see TestConnection). Runs on the
        // main thread so RefreshBackend's DB/NPC-list work and VoicesMapped rebuild are frame-safe.
        if (_pendingRemapAfterConnect)
        {
            _pendingRemapAfterConnect = false;
            _backend.RefreshBackend();
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
        var liveGen = _config.HasLiveGeneration;
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

        // Engine + instance-type — visibility and state. Mode (Local/Remote/None) follows the
        // ACTIVE engine; which engine's Local/Remote section is shown follows BackendSelection.
        var isAlltalk = _config.BackendSelection == TTSBackends.Alltalk;
        var isEk      = _config.BackendSelection == TTSBackends.EchokrauTTS;
        var instanceType = _config.ActiveInstanceType;
        var isLocal   = instanceType == AlltalkInstanceType.Local;
        var isRemote  = instanceType == AlltalkInstanceType.Remote;
        var isNone    = instanceType == AlltalkInstanceType.None;
        var installing = isEk ? _echokrauTtsInstance.Installing : _alltalkInstance.Installing;
        // Batch lock — harvest / voice-sample extract / (future) import / export. While any
        // such operation is in flight, every backend-affecting widget (mode switcher, install
        // controls, backend dropdown, reload buttons, etc.) is dimmed so the run isn't
        // disturbed mid-flight. Use-XYZ playback toggles stay interactive (per user spec).
        var batchActive = _batchMode.IsActive;
        var modeLocked = installing || batchActive;

        // Top tab bar follows live-generation availability. None mode → Voices + Phonetics
        // disappear (they only configure the routing for backend generation). Tab rebuild
        // is gated on a transition so we don't dispose+recreate radio buttons every frame.
        if (_topTabsLiveGenSnapshot != liveGen)
        {
            _topTabsLiveGenSnapshot = liveGen;
            BuildTopTabs(liveGen);
        }

        // Settings sub-tab bar follows the same gate — Chat tab disappears in None mode.
        if (_settingsTabsLiveGenSnapshot != liveGen)
        {
            _settingsTabsLiveGenSnapshot = liveGen;
            BuildSettingsTabs(liveGen);
        }

        // Logs sub-tab bar follows the same gate — Chat / Cutscene / Choice / Backend log
        // tabs are LiveOnly and disappear in None mode (no audio-file fallback / no backend
        // traffic to log). Source-indexed log writes continue in the background, so history
        // is preserved when the user switches back to a live backend.
        if (_logsTabsLiveGenSnapshot != liveGen)
        {
            _logsTabsLiveGenSnapshot = liveGen;
            BuildLogsTabs(liveGen);
        }

        // Game Data Tools button: route through ATK's component-disabled state via
        // ButtonBase.IsEnabled, which calls ComponentBase->SetEnabledState. That triggers
        // the FFXIV-standard disabled visual (~0.7 alpha + multiplier 0.5) AND silences
        // cursor flip + click animation at the component level.
        //
        // The hover handlers we wired manually on ImageNode (icon brighten + tooltip)
        // have their own bail-out via IsEnabled inside the callbacks. On the disable
        // transition itself we still need to clear any stuck hover state — if the user
        // was hovering when liveGen flipped, MouseOut may have already fired (state ok),
        // but we force-reset MultiplyColor + HideTooltip to be sure neither is left
        // brightened or showing.
        if (_gameDataToolsButton != null && _gameDataBtnEnabledState != liveGen)
        {
            _gameDataBtnEnabledState = liveGen;
            _gameDataToolsButton.IsEnabled = liveGen;
            if (!liveGen)
            {
                _gameDataToolsButton.ImageNode.MultiplyColor = new Vector3(1f, 1f, 1f);
                _gameDataToolsButton.HideTooltip();
            }
        }

        // Mode switcher — selected button stays bright, others dim. Locked while an install
        // is in progress (mid-install switch would race on alltalkFolder) AND while a batch
        // op (harvest / voice-extract) runs — switching modes would change which backend
        // the op talks to mid-flight.
        Dim(_modeLocalBtn,  !modeLocked && isLocal);
        Dim(_modeRemoteBtn, !modeLocked && isRemote);
        Dim(_modeNoneBtn,   !modeLocked && isNone);

        // Backend dropdown — must not flip mid-batch.
        Dim(_backendDropDown, !batchActive);
        // Streaming + Reload-Voices live in the General tab now. Streaming dims when
        // there's no backend to stream from (None mode) OR during batch. Reload-Voices
        // same: no backend to reload from in None, and would race a running batch.
        Dim(_atStreamingCheck,    liveGen && !batchActive);
        Dim(_atReloadVoicesButton, liveGen && !batchActive);
        // None-mode panel: lock the path / GD widgets too — the user could change the
        // audio source mid-batch, but VoiceSampleExtractor writes to the same FF14-Voices
        // root and we don't want it pointed at a moving target.
        Dim(_noneAudioPathInput, !batchActive);
        Dim(_noneGdDownloadCheck,!batchActive);
        // _noneGdLinkInput already dims via GoogleDriveDownload below; combine.

        // Show/hide entire collapsible sections based on instance type.
        var showLocal   = isAlltalk && isLocal;
        var showRemote  = isAlltalk && isRemote;
        var showNone    = isAlltalk && isNone;

        // Local section: toggle button + content. Content visibility = section shown × expanded.
        // Single-source-of-truth for expanded state lives in _localExpanded (mirrored by toggle text).
        SetVisible(_localSectionToggle, showLocal);
        if (_localSectionContent != null)
            foreach (var n in _localSectionContent) SetVisible(n, showLocal && _localExpanded);
        SetVisible(_localAdvancedToggle, showLocal);
        if (_localAdvancedContent != null)
            foreach (var n in _localAdvancedContent) SetVisible(n, showLocal && _localAdvancedExpanded);
        if (_localPostAdvancedContent != null)
            foreach (var n in _localPostAdvancedContent) SetVisible(n, showLocal);
        if (showLocal) _atLocalNodes?.Update(_config, _alltalkInstance, batchActive);

        // Remote section: toggle button + content
        SetVisible(_remoteSectionToggle, showRemote);
        if (_remoteSectionContent != null)
            foreach (var n in _remoteSectionContent) SetVisible(n, showRemote && _remoteExpanded);
        // Lock remote URL editing + test-connection button during batch — same rationale
        // as the install controls: the running backend would see the URL flip mid-flight.
        if (showRemote && _atRemoteNodes != null)
        {
            Dim(_atRemoteNodes.BaseUrlInput,        !batchActive);
            Dim(_atRemoteNodes.TestConnectionButton,!batchActive);
        }

        // None section: toggle button + content (info text + audio path + GD link). None mode is
        // engine-independent, so this shows whenever the active instance type is None.
        SetVisible(_noneSectionToggle, showNone);
        if (_noneSectionContent != null)
            foreach (var n in _noneSectionContent) SetVisible(n, showNone && _noneExpanded);
        // GD link follows GoogleDriveDownload toggle even within the None section
        if (showNone) Dim(_noneGdLinkInput, _config.GoogleDriveDownload && !batchActive);

        // EchokrauTTS Local/Remote sections (extracted to keep this method's complexity down).
        var showEkLocal = isEk && isLocal;
        var showEkRemote = isEk && isRemote;
        UpdateEchokrauTtsSections(showEkLocal, showEkRemote, batchActive);

        // Recalculate backend panel layout when visibility changes
        if (showLocal != _prevShowLocal || showRemote != _prevShowRemote
            || showNone != _prevShowNone || showEkLocal != _prevShowEkLocal || showEkRemote != _prevShowEkRemote)
        {
            _prevShowLocal = showLocal;
            _prevShowRemote = showRemote;
            _prevShowNone = showNone;
            _prevShowEkLocal = showEkLocal;
            _prevShowEkRemote = showEkRemote;
            _settingsPanels[4]?.RecalculateLayout();
        }

        // Partial class updates
        UpdateVoiceSelection();
        UpdatePhonetics();
        UpdateLogs();
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

        // Streaming generation moved here from the Backend tab's old Service-options
        // section. It's an AllTalk-only knob, so it dims (and is no-op) in None mode but
        // stays interactive on Local/Remote regardless of which Backend sub-tab is open.
        _atStreamingCheck = Check(
            Loc.S("Streaming generation (play audio before full text is generated)"), w,
            _config.Alltalk.StreamingGeneration,
            v => { _config.Alltalk.StreamingGeneration = v; _config.Save(); });

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

        // "Reload voices" moved here from the Backend tab — same family as the other
        // Reset-Data actions (DB / file-state refresh) and easier to find for users.
        // Re-pulls the backend's voice list and re-runs character voice mapping. No-op
        // in None mode (no backend), so it dims along with the Streaming checkbox.
        _atReloadVoicesButton = Button(Loc.S("Reload voices"), 180, () =>
        {
            _backend.SetBackendType(_config.BackendSelection);
            _backend.NotifyCharacterMapped();
        });

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
        _atReloadVoicesButton!.Size = new Vector2(btnW, 24);
        _wipeAllButton!.Size      = new Vector2(innerW, 28);

        var row1 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row1.AddNode(_clearNpcsButton);
        row1.AddNode(_clearPlayersButton);
        var row2 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row2.AddNode(_clearBubblesButton);
        row2.AddNode(reloadRemoteButton);
        // Reload voices sits next to the other reload-style action (remote mappings) so
        // both "refresh upstream-sourced data" buttons are paired visually.
        var row3 = new HorizontalListNode { Size = new Vector2(innerW, 28), ItemSpacing = 4 };
        row3.AddNode(_atReloadVoicesButton);

        list.AddNode(enabledRow);
        list.AddNode(_generateBySentenceCheck);
        list.AddNode(_atStreamingCheck);
        list.AddNode(removeStuttersCheck);
        list.AddNode(_removePunctuationCheck);
        list.AddNode(_hideUiCheck);

        CreateCollapsibleSection(list, Loc.S("In-Game Controls"), w, true,
            [_showExtraOptionsCheck]);

        CreateCollapsibleSection(list, Loc.S("Reset Data"), w, true, [row1, row2, row3, _wipeAllButton]);

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

        _backendDropDown = new StringDropDownNode { Size = new Vector2(w, 24), Options = backends };
        _backendDropDown.SelectedOption = currentBackend;
        _backendDropDown.LabelNode.String = currentBackend;

        // Capture in OnOptionSelected (fires before UpdateLabel crash); process next frame.
        _backendDropDown.OnOptionSelected = option => _pendingBackendSelection = option;

        // Mode switcher — 3 buttons styled like FTU Step 0's choices. Click switches
        // InstanceType live; OnUpdate then dims the inactive buttons and toggles which
        // section (Local / Remote / None) is shown below.
        //
        // Width = longest localized label + 36 chrome, applied uniformly. Earlier the
        // row was stretched to full panel width with `(w-8)/3` per button, which both
        // (a) wasted horizontal space on short labels and (b) when ScrollingListNode's
        // FitWidth pass forced the row to `panelWidth - 16` (scrollbar reserve), the
        // third button got pushed past the panel's clip rect — its right hitbox
        // landed outside the scrolling viewport, so clicks there were silently dropped
        // (visible bug: from None mode you could pick Local but then couldn't click
        // back on None — None had become the third button and was clipped). Sizing
        // exactly to content sidesteps both.
        var labels = new[] { Loc.S("Local TTS"), Loc.S("Remote Server"), Loc.S("Audio Files Only") };
        var sample = new TextButtonNode { Size = new Vector2(1, 28), String = labels[0] };
        var modeBtnW = labels.Max(s => sample.LabelNode.GetTextDrawSize(s).X) + 36;
        _modeLocalBtn = ModeButton(Loc.S("Local TTS"), modeBtnW, () => SetActiveInstanceType(AlltalkInstanceType.Local));
        _modeRemoteBtn = ModeButton(Loc.S("Remote Server"), modeBtnW, () => SetActiveInstanceType(AlltalkInstanceType.Remote));
        _modeNoneBtn = ModeButton(Loc.S("Audio Files Only"), modeBtnW, () => SetActiveInstanceType(AlltalkInstanceType.None));
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
            TextColor = LabelColor,
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

        list.AddNode(_backendDropDown);
        list.AddNode(_modeSwitcherRow);

        _localSectionContent = _atLocalNodes.EssentialNodes;
        _localSectionToggle = CreateTrackedCollapsibleSection(list, Loc.S("Local instance"), w,
            _localSectionContent, () => _localExpanded, v => _localExpanded = v);

        _localAdvancedContent = _atLocalNodes.AdvancedNodes;
        _localAdvancedToggle = CreateTrackedCollapsibleSection(list, Loc.S("Advanced options"), w,
            _localAdvancedContent, () => _localAdvancedExpanded, v => _localAdvancedExpanded = v);

        _localPostAdvancedContent = _atLocalNodes.PostAdvancedNodes;
        foreach (var n in _localPostAdvancedContent) list.AddNode(n);

        _remoteSectionContent = _atRemoteNodes.AllNodes;
        _remoteSectionToggle = CreateTrackedCollapsibleSection(list, Loc.S("Remote connection"), w,
            _remoteSectionContent, () => _remoteExpanded, v => _remoteExpanded = v);

        _noneSectionContent = [_noneInfoLabel, _noneAudioPathInput, _noneGdDownloadCheck, _noneGdLinkInput];
        _noneSectionToggle = CreateTrackedCollapsibleSection(list, Loc.S("Audio Files Only"), w,
            _noneSectionContent, () => _noneExpanded, v => _noneExpanded = v);

        // EchokrauTTS engine sections (parallel to AllTalk; shown when EchokrauTTS is selected).
        _ekLocalNodes = NativeEchokrauTtsBuilder.BuildLocalInstance(w, _config, _echokrauTtsInstance, _alltalkInstance.IsCudaInstalled);
        _ekRemoteNodes = NativeEchokrauTtsBuilder.BuildRemoteInstance(w, _config);
        _ekRemoteNodes.TestConnectionButton.OnClick = () => TestConnection();

        _ekLocalSectionContent = _ekLocalNodes.AllNodes;
        _ekLocalSectionToggle = CreateTrackedCollapsibleSection(list, Loc.S("EchokrauTTS local instance"), w,
            _ekLocalSectionContent, () => _ekLocalExpanded, v => _ekLocalExpanded = v);

        _ekRemoteSectionContent = _ekRemoteNodes.AllNodes;
        _ekRemoteSectionToggle = CreateTrackedCollapsibleSection(list, Loc.S("EchokrauTTS remote connection"), w,
            _ekRemoteSectionContent, () => _ekRemoteExpanded, v => _ekRemoteExpanded = v);

        return list;
    }

    /// <summary>Per-frame visibility + state for the EchokrauTTS Local/Remote sections.</summary>
    private void UpdateEchokrauTtsSections(bool showEkLocal, bool showEkRemote, bool batchActive)
    {
        SetVisible(_ekLocalSectionToggle, showEkLocal);
        if (_ekLocalSectionContent != null)
            foreach (var n in _ekLocalSectionContent) SetVisible(n, showEkLocal && _ekLocalExpanded);
        if (showEkLocal) _ekLocalNodes?.Update(_config, _echokrauTtsInstance, batchActive);

        SetVisible(_ekRemoteSectionToggle, showEkRemote);
        if (_ekRemoteSectionContent != null)
            foreach (var n in _ekRemoteSectionContent) SetVisible(n, showEkRemote && _ekRemoteExpanded);
        if (showEkRemote && _ekRemoteNodes != null)
        {
            Dim(_ekRemoteNodes.BaseUrlInput,         !batchActive);
            Dim(_ekRemoteNodes.TestConnectionButton, !batchActive);
        }
    }

    /// <summary>Write the Local/Remote/None mode onto the currently-selected engine + save.</summary>
    private void SetActiveInstanceType(AlltalkInstanceType type)
    {
        if (_config.BackendSelection == TTSBackends.EchokrauTTS)
            _config.EchokrauTts.InstanceType = type;
        else
            _config.Alltalk.InstanceType = type;
        _config.Save();
    }

    /// <summary>
    /// Build a mode-switcher button styled to look like a small radio. The selected/active
    /// state is conveyed via Alpha (1.0 active, 0.4 inactive) — set per frame in OnUpdate
    /// since InstanceType is the source of truth and may flip from the FTU window or another
    /// code path. Cheaper than tracking transitions; Alpha writes are idempotent.
    ///
    /// Width is fixed at the caller-provided value — no auto-grow. The 3-button row is sized
    /// to fit the panel; auto-growing past it would push the third button outside the
    /// panel's clip region, which makes the right portion of its hitbox dead (you can see
    /// the button but clicks there don't register). Long localized labels are clipped at
    /// the button's right edge instead, which is preferable to broken click handling.
    /// </summary>
    private static TextButtonNode ModeButton(string label, float width, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(width, 28), String = label };
        node.OnClick = onClick;
        return node;
    }

    private void TestConnection()
    {
        // Route the result to whichever remote section is active — the Alltalk and EchokrauTTS
        // remote sections each have their own result label but share this handler.
        var isEk = _config.BackendSelection == TTSBackends.EchokrauTTS;
        var resultLabel = isEk ? _ekRemoteNodes?.ConnectionResultLabel : _atRemoteNodes?.ConnectionResultLabel;
        if (resultLabel == null) return;
        resultLabel.String = "Testing...";

        _backend.CheckReady(new EKEventId(0, TextSource.None)).ContinueWith(t =>
        {
            resultLabel.String = t.IsFaulted
                ? $"Error: {t.Exception?.InnerException?.Message}"
                : $"Result: {t.Result}";

            // If the backend was unreachable at startup, MapVoices never ran and every mapped NPC
            // still holds a stale/empty selectable list — so persisted voice keys can't resolve and
            // generation warns "No voice assigned". A successful (re)connect must remap voices +
            // refresh selectables; defer to the main thread (RefreshBackend touches the DB, the NPC
            // lists, and fires VoicesMapped/UI events).
            if (!t.IsFaulted)
                _pendingRemapAfterConnect = true;
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
            // SwitchEngine flushes the queue, copies voices old→new, persists the selection, and
            // reconnects the backend. No-op when already on that engine.
            _voiceSync.SwitchEngine(backend);
        }

        if (_backendDropDown == null || _backendDropDown.IsCollapsed) return;
        try
        {
            _backendDropDown.SelectedOption = option;
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

    private static SliderNode Slider(float width, float initialValue, Action<float> onChange)
    {
        var node = new SliderNode
        {
            Size          = new Vector2(width, 20),
            Range         = 0..100,
            Value         = (int)(initialValue * 100),
        };
        node.OnValueChanged = v => onChange(v / 100.0f);
        return node;
    }







    /// <summary>
    /// Adds a collapsible section to a ScrollingListNode using a TextButtonNode toggle.
    /// Uses component events (no CollisionNode), so it works inside nested containers.
    /// Returns the toggle button so the caller can position it.
    /// </summary>

    /// <summary>
    /// Variant of <see cref="CreateCollapsibleSection"/> for sections whose visibility is
    /// also driven externally (e.g. backend mode-switcher hiding the entire Local/Remote/None
    /// section). The expanded state is tracked via the supplied getter/setter so OnUpdate can
    /// compute content visibility as <c>showSection &amp;&amp; expanded</c> without falling
    /// back to <c>contentNodes[0].IsVisible</c> — that proxy desyncs as soon as OnUpdate
    /// force-hides the section, leaving the toggle text reading "[-]" while content is gone.
    ///
    /// Initial visibility of the content nodes is left to the caller's OnUpdate pass.
    /// </summary>
    private static TextButtonNode CreateTrackedCollapsibleSection(
        ScrollingListNode list, string title, float width, NodeBase[] contentNodes,
        Func<bool> getExpanded, Action<bool> setExpanded)
    {
        var arrow = getExpanded() ? "[-]" : "[+]";
        TextButtonNode? toggle = null;
        toggle = new TextButtonNode { Size = new Vector2(width, 24), String = $"{arrow} {title}" };
        toggle.OnClick = () =>
        {
            var expanded = !getExpanded();
            setExpanded(expanded);
            foreach (var n in contentNodes) n.IsVisible = expanded;
            toggle!.String = expanded ? $"[-] {title}" : $"[+] {title}";
            list.RecalculateLayout();
        };

        list.AddNode(toggle);
        foreach (var n in contentNodes)
            list.AddNode(n);

        return toggle;
    }
}
