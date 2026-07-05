using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.UI.Nodes;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

using static Echokraut.Windows.Native.NativeNodeFactory;
namespace Echokraut.Windows.Native;

public sealed unsafe class NativeVoiceClipManagerWindow : NativeAddon
{
    private readonly IDatabaseService _db;
    private readonly IVoiceClipManagerService _voiceClipManager;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly INpcDataService _npcData;
    private readonly IGameObjectService _gameObjects;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly IBackendService _backend;
    private readonly Action _toggleConfig;
    private readonly Action _toggleGameDataTools;

    // Generation status bar (harvest progress + start/stop now lives in NativeGameDataToolsWindow)
    private StatusProgressBar? _statusBar;
    private TextNode? _backendStatusLabel;
    private DynamicIconButtonNode? _settingsButton;
    private DynamicIconButtonNode? _gameDataToolsButton;
    private TextButtonNode? _genAllToggleButton;
    private CancellationTokenSource? _genAllCts;
    private bool _genAllRunning;
    private int _genAllDone;
    private int _genAllTotal;
    private int _statusUpdateCounter;
    private bool _statusForceRecompute;
    private volatile bool _statusCalcRunning;
    private volatile int _statusTotalClips;
    private volatile int _statusTotalSaved;

    // Layout
    private float _contentWidth;

    // Filter
    private TextInputNode? _filterInput;
    private string _filterText = "";

    // Quest type filter
    private StringDropDownNode? _questTypeDropDown;
    private int _selectedQuestType = -1; // -1 = All
    private int _pendingQuestTypeSelection = -1;
    private string[]? _questTypeLabels;
    // Maps dropdown index → QuestType enum value (-1 = all, 0-6 = enum values)
    private static readonly int[] QuestTypeValues = { -1, 1, 2, 3, 4, 5, 6, 0 };

    // Tabs
    private TabBarNode? _tabBar;
    private int _activeTab;
    // Virtualized tree (KTK TreeListNode) — headers = NPCs, rows = quest-type groups. The node
    // pools a fixed set of row views and only updates content/visibility/position, so there is no
    // per-item dispose/rebuild cycle (this replaced the old ScrollingTreeNode/TreeListCategoryNode
    // build-and-destroy model that fed the ATK draw-node-list crash class).
    private TreeListNode<VcRow, VoiceClipRowNode>?[] _treePanels = new TreeListNode<VcRow, VoiceClipRowNode>?[2];
    private bool[] _panelDirty = { true, true };
    private bool[] _panelBuilt = { false, false };

    // Pagination
    private const int PageSize = 100;
    private PaginationBar?[] _paginationBars = new PaginationBar?[2];
    private Vector2 _treePos;
    private Vector2 _treeSize;

    // Data
    private List<NpcMapData> _npcList = new();
    private List<NpcMapData> _playerList = new();
    private int _lastNpcCount;
    private int _lastPlayerCount;
    private bool _needsRebuild = true;

    // Progressive load+build — each frame loads a batch from DB and accumulates VcRow entries into
    // the tree's Options dictionary (headers=NPCs). Re-assigning Options per batch is cheap: the
    // virtualized tree only re-pools/re-populates visible rows and preserves collapse + scroll state.
    private const int BatchSize = 5;
    private int _progressiveIndex = -1; // -1 = idle
    private int _progressiveTab = -1;
    private List<NpcMapData> _progressiveDataList = new();
    private int _progressivePageStart;
    private int _progressivePageEnd;
    private Dictionary<ReadOnlySeString, List<VcRow>> _progressiveOptions = new();
    private bool _firstFrame = true; // skip first OnUpdate to let OnSetup settle

    // Caches
    private readonly Dictionary<string, int> _charIdCache = new();

    // Detail window reference
    private NativeVoiceClipDetailWindow? _detailWindow;

    // NPC edit popup reference (Race/Gender override)
    private NativeNpcEditWindow? _npcEditWindow;

    private bool _countsNeedRefresh;

    private readonly Configuration _config;

    // Game-Data-Tools icon button "enabled" snapshot — gates NodeFlags toggles on
    // transitions only (CLAUDE.md: AddNodeFlags every frame can crash the game).
    private bool? _gameDataBtnEnabledState;
    // Bulk-generate button "enabled" snapshot — same rationale.
    private bool? _genAllBtnEnabledState;

    public NativeVoiceClipManagerWindow(
        IDatabaseService db,
        IVoiceClipManagerService voiceClipManager,
        IAudioPlaybackService audioPlayback,
        INpcDataService npcData,
        IGameObjectService gameObjects,
        IClientState clientState,
        ILogService log,
        IBackendService backend,
        Configuration config,
        Action toggleConfig,
        Action toggleGameDataTools)
    {
        _db = db;
        _voiceClipManager = voiceClipManager;
        _audioPlayback = audioPlayback;
        _npcData = npcData;
        _gameObjects = gameObjects;
        _clientState = clientState;
        _log = log;
        _backend = backend;
        _config = config;
        _toggleConfig = toggleConfig;
        _toggleGameDataTools = toggleGameDataTools;

        _onVoiceClipUpdated = () => _countsNeedRefresh = true;
        _onVoiceClipLogged = () => _needsRebuild = true;
        _voiceClipManager.VoiceClipUpdated += _onVoiceClipUpdated;
        _db.VoiceClipLogged += _onVoiceClipLogged;
    }

    private readonly Action _onVoiceClipUpdated;
    private readonly Action _onVoiceClipLogged;

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        try { _voiceClipManager.VoiceClipUpdated -= _onVoiceClipUpdated; } catch { }
        try { _db.VoiceClipLogged -= _onVoiceClipLogged; } catch { }
    }

    public void SetDetailWindow(NativeVoiceClipDetailWindow detailWindow)
    {
        _detailWindow = detailWindow;
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        _contentWidth = size.X;
        const float topRowH = 28f;

        // Quest-type filter (NPC tree pre-filter). Harvest controls now live exclusively in
        // the Game Data Tools window — the dropdown sits at the left edge where the harvest
        // button used to be.
        _questTypeLabels = new[]
        {
            Loc.S("All"), Loc.S("Main Scenario"), Loc.S("Side Quest"),
            Loc.S("Unlock / Class Quest"), Loc.S("Beast Tribe"),
            Loc.S("Repeatable"), Loc.S("Seasonal Event"), Loc.S("Non-Quest Dialog")
        };
        _questTypeDropDown = new StringDropDownNode
        {
            Size = new Vector2(180, topRowH),
            Position = pos,
            MaxListOptions = 8,
            Options = new List<string>(_questTypeLabels),
        };
        _questTypeDropDown.SelectedOption = _questTypeLabels[0];
        _questTypeDropDown.LabelNode.String = _questTypeLabels[0];
        _questTypeDropDown.OnOptionSelected = selected => _pendingQuestTypeSelection = Array.IndexOf(_questTypeLabels, selected);
        AddNode(_questTypeDropDown);

        var tabY = pos.Y + topRowH + 4;
        const float tabH = 32f;

        _tabBar = new TabBarNode { Size = new Vector2(size.X, tabH), Position = new Vector2(pos.X, tabY) };
        _tabBar.AddTab(Loc.S("NPCs"), () => ShowPanel(0));
        _tabBar.AddTab(Loc.S("Players"), () => ShowPanel(1));
        AddNode(_tabBar);

        // Generate all row + progress bar
        const float genRowH = 28f;
        var genRowY = tabY + tabH + 2;
        var genMeasure = new TextButtonNode { Size = new Vector2(100, genRowH), String = Loc.S("Generate All Unsaved") };
        var genW1 = genMeasure.LabelNode.GetTextDrawSize(Loc.S("Generate All Unsaved")).X + 36;
        var genW2 = genMeasure.LabelNode.GetTextDrawSize(Loc.S("Stop")).X + 36;
        var genBtnW = Math.Max(genW1, genW2);
        genMeasure.Dispose();

        _genAllToggleButton = new TextButtonNode
        {
            Size = new Vector2(genBtnW, genRowH),
            Position = new Vector2(pos.X, genRowY),
            String = Loc.S("Generate All Unsaved"),
        };
        _genAllToggleButton.OnClick = () =>
        {
            if (_genAllRunning)
            {
                _genAllCts?.Cancel();
                return;
            }

            _genAllRunning = true;
            _genAllCts?.Dispose();
            _genAllCts = new CancellationTokenSource();
            if (_genAllToggleButton != null) _genAllToggleButton.String = Loc.S("Stop");

            // Gather ALL clips matching current language + quest type filter for active tab
            var allClips = new List<VoiceClipEntity>();
            var langInt = (int)_clientState.ClientLanguage;
            var chars = _activeTab == 0 ? _db.GetNpcs() : _db.GetPlayers();
            foreach (var c in chars)
            {
                if (c.Language != langInt) continue;
                var clips = _db.GetVoiceClipsForCharacter(c.Id, 100000);
                if (_selectedQuestType >= 0)
                    clips = clips.FindAll(vc => vc.QuestType == _selectedQuestType);
                allClips.AddRange(clips);
            }

            _voiceClipManager.GenerateAllUnsaved(allClips, (done, total) =>
            {
                _genAllDone = done;
                _genAllTotal = total;
            }, _genAllCts.Token).ContinueWith(_ =>
            {
                _genAllRunning = false;
                _genAllDone = 0;
                _genAllTotal = 0;
                if (_genAllToggleButton != null) _genAllToggleButton.String = Loc.S("Generate All Unsaved");
                _countsNeedRefresh = true;
            });
        };
        AddNode(_genAllToggleButton);

        _statusBar = new StatusProgressBar
        {
            Position = new Vector2(pos.X + genBtnW + 4, genRowY),
            Size = new Vector2(size.X - genBtnW - 4, genRowH),
        };
        AddNode(_statusBar);

        // Filter input
        const float filterH = 28f;
        var filterY = genRowY + genRowH + 2;
        _filterInput = new TextInputNode
        {
            Size = new Vector2(size.X, filterH),
            Position = new Vector2(pos.X, filterY),
            MaxCharacters = 80,
            PlaceholderString = Loc.S("Search..."),
            String = "",
        };
        _filterInput.OnInputReceived = s =>
        {
            _filterText = s.ToString();
            _needsRebuild = true;
        };
        AddNode(_filterInput);

        const float paginationH = 28f;
        const float settingsBtnSize = 28f;
        const float settingsBtnGap = 4f;
        const float backendStatusW = 180f;
        var treeTop = filterY + filterH + 2;
        _treePos = new Vector2(pos.X, treeTop);
        _treeSize = new Vector2(size.X, pos.Y + size.Y - treeTop - paginationH - 4);
        var pagY = pos.Y + size.Y - paginationH;

        // Settings (gear) button — same DynamicIconButtonNode pattern as DialogTalkController.
        // Sits at the very bottom-left, opens the config window on click.
        _settingsButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X, pagY),
            Size = new Vector2(settingsBtnSize, settingsBtnSize),
            Icon = CircleButtonIcon.GearCog,
            Tooltip = Loc.S("Open configuration window"),
            OnClick = () => _toggleConfig(),
        };
        // Manual hover highlight + tooltip — in NativeAddon contexts only ImageNode events
        // fire reliably (same reason DynamicIconButtonNode reroutes OnClick to ImageNode).
        // The Tooltip setter registers MouseOver/MouseOut on the button itself, which never
        // fires here, so we drive ShowTooltip/HideTooltip manually from the ImageNode events
        // we know work.
        WireIconButtonHover(_settingsButton, () => _settingsButton != null,
            _settingsButton.ShowTooltip, _settingsButton.HideTooltip);
        AddNode(_settingsButton);

        // Game Data Tools button — opens the bulk-data window (harvest + voice starter set).
        // Same DynamicIconButtonNode pattern as the settings button; sits one slot to its right.
        // Icon UV (168, 84) on Character.tex matches CircleButtonIcon.GearCogWithChatBubble — a gear
        // wedded to a speech bubble, fitting for "data + dialog tooling".
        _gameDataToolsButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X + settingsBtnSize + settingsBtnGap, pagY),
            Size = new Vector2(settingsBtnSize, settingsBtnSize),
            Icon = CircleButtonIcon.GearCogWithChatBubble,
            Tooltip = Loc.S("Open Game Data Tools window"),
            OnClick = () => _toggleGameDataTools(),
        };
        // Both MouseOver and MouseOut bail when disabled so the None-mode dimmed look is
        // owned exclusively by ATK's disabled timeline; without the MouseOut gate, the
        // cursor leaving a dimmed button reset MultiplyColor to (1,1,1) and the icon
        // snapped brighter than its disabled state.
        WireIconButtonHover(_gameDataToolsButton,
            () => _gameDataToolsButton != null && _gameDataToolsButton.IsEnabled,
            _gameDataToolsButton.ShowTooltip, _gameDataToolsButton.HideTooltip);
        AddNode(_gameDataToolsButton);

        // Backend reachability indicator — to the right of both icon buttons, on the pagination row.
        // Narrow (180px) so it doesn't fight the pagination buttons which sit further right.
        _backendStatusLabel = new TextNode
        {
            Position = new Vector2(pos.X + 2 * (settingsBtnSize + settingsBtnGap), pagY + 5),
            Size = new Vector2(backendStatusW, paginationH - 10),
            String = "",
            FontType = FontType.Axis,
            FontSize = 12,
            AlignmentType = AlignmentType.Left,
            TextColor = LabelColor,
        };
        AddNode(_backendStatusLabel);

        for (var i = 0; i < 2; i++)
        {
            var tree = new TreeListNode<VcRow, VoiceClipRowNode>
            {
                Position = _treePos,
                Size = _treeSize,
                ItemSpacing = 2f,
                NoResultsString = Loc.S("No voice clips found."),
            };
            // Clicking a quest-type row opens the detail window with that group's clips. Per-NPC
            // actions (Generate All / Delete All / Edit Character) now live in the detail window.
            tree.OnItemSelected = row =>
            {
                if (row == null) return;
                var mapData = _npcData.MappedNpcs.Find(n => n.ToString() == row.NpcKey)
                           ?? _npcData.MappedPlayers.Find(n => n.ToString() == row.NpcKey);
                _detailWindow?.ShowVoiceClips(row.DetailTitle, row.VoiceClips, row.NpcKey,
                    mapData != null ? () => OpenNpcEdit(mapData) : null);
            };
            _treePanels[i] = tree;
            AddNode(tree);

            var tabIdx = i;
            _paginationBars[i] = new PaginationBar(
                new Vector2(pos.X, pagY), size.X,
                page => _panelDirty[tabIdx] = true);
            foreach (var node in _paginationBars[i]!.Nodes)
                AddNode(node);
        }

        ShowPanel(0);
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        ScreenClampHelper.ClampToScreen(addon, Size);

        // Skip first frame so OnSetup node creation doesn't compound with data loading
        if (_firstFrame) { _firstFrame = false; return; }

        var liveGen = _config.HasLiveGeneration;
        UpdateGenAllButtonState(liveGen);

        // Game Data Tools icon button: route through ATK's component-disabled state via
        // ButtonBase.IsEnabled. SetEnabledState triggers the FFXIV-standard disabled
        // timeline (alpha ~0.7, multiplier 0.5) and silences cursor flip + click anim at
        // the component level. The hover handlers wired on ImageNode bail on IsEnabled
        // (see OnSetup); the disable transition itself force-clears any stuck hover
        // state so the dimmed button never lingers brightened or showing a tooltip.
        if (_gameDataToolsButton != null && _gameDataBtnEnabledState != liveGen)
        {
            _gameDataBtnEnabledState = liveGen;
            _gameDataToolsButton.IsEnabled = liveGen;
            if (!liveGen)
            {
                _gameDataToolsButton.ImageNode.MultiplyColor = new System.Numerics.Vector3(1f, 1f, 1f);
                _gameDataToolsButton.HideTooltip();
            }
        }

        // Process deferred quest type selection
        if (_pendingQuestTypeSelection >= 0)
        {
            _selectedQuestType = QuestTypeValues[_pendingQuestTypeSelection];
            _pendingQuestTypeSelection = -1;
            _needsRebuild = true;
            // Force the status bar to recompute against the new filter on the next frame
            _statusForceRecompute = true;
        }

        // Update status bar
        UpdateStatusBar();

        if (_npcData.MappedNpcs.Count != _lastNpcCount || _npcData.MappedPlayers.Count != _lastPlayerCount)
            _needsRebuild = true;

        // Start or restart progressive load+build
        if (_needsRebuild)
        {
            StartProgressive();
            return;
        }

        // Continue progressive load+build (batch per frame)
        if (_progressiveIndex >= 0)
        {
            ContinueProgressive();
            return;
        }

        _paginationBars[_activeTab]?.Update();

        // Refresh generated counts after a single clip was (re)generated. Re-run the current
        // page build: it re-queries the counts and re-assigns the tree's Options, which the
        // virtualized tree applies while preserving collapse + scroll state.
        if (_countsNeedRefresh)
        {
            _countsNeedRefresh = false;
            _panelDirty[_activeTab] = true;
        }

        // Panel dirty without full rebuild (e.g. page change)
        if (_panelDirty[_activeTab])
        {
            _panelDirty[_activeTab] = false;
            StartProgressivePage(_activeTab);
            return;
        }
    }

    private void ShowPanel(int index)
    {
        _activeTab = index;
        for (var i = 0; i < 2; i++)
        {
            SetVisible(_treePanels[i], i == index);
            if (_paginationBars[i] != null)
                foreach (var node in _paginationBars[i]!.Nodes)
                    SetVisible(node, i == index);
        }
        // Cancel any in-progress progressive build for the previous tab
        _progressiveIndex = -1;
        _progressiveTab = -1;
        _panelDirty[index] = true;
    }

    /// <summary>
    /// Full rebuild: filter + sort data, prepare fresh tree, start progressive load+build.
    /// </summary>
    private void StartProgressive()
    {
        _needsRebuild = false;
        _progressiveIndex = -1; // cancel any in-progress work

        var filter = _filterText.Trim();
        var hasFilter = !string.IsNullOrEmpty(filter);

        _npcList = _npcData.MappedNpcs
            .FindAll(n => n.Language == _clientState.ClientLanguage
                && (!hasFilter || n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _playerList = _npcData.MappedPlayers
            .FindAll(n => n.Language == _clientState.ClientLanguage
                && (!hasFilter || n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _lastNpcCount = _npcData.MappedNpcs.Count;
        _lastPlayerCount = _npcData.MappedPlayers.Count;

        _charIdCache.Clear();

        // Pre-filter NPC lists by quest type if a filter is active
        if (_selectedQuestType >= 0)
        {
            var charIds = _db.GetCharacterIdsWithQuestType(_selectedQuestType);
            // Build a fast lookup: character key → ID from cached NPC/player data
            var npcCharIds = new HashSet<string>();
            foreach (var npc in _db.GetNpcs())
                if (charIds.Contains(npc.Id))
                    npcCharIds.Add($"{npc.Name}|{npc.Gender}|{npc.Race}|{npc.Language}");
            foreach (var player in _db.GetPlayers())
                if (charIds.Contains(player.Id))
                    npcCharIds.Add($"{player.Name}|{player.Gender}|{player.Race}|{player.Language}");

            _npcList = _npcList.FindAll(n => npcCharIds.Contains($"{n.Name}|{(int)n.Gender}|{(int)n.Race}|{(int)n.Language}"));
            _playerList = _playerList.FindAll(n => npcCharIds.Contains($"{n.Name}|{(int)n.Gender}|{(int)n.Race}|{(int)n.Language}"));
        }

        _paginationBars[0]?.SetTotalItems(_npcList.Count, PageSize);
        _paginationBars[1]?.SetTotalItems(_playerList.Count, PageSize);

        // Prepare a fresh tree for the active tab
        PrepareTreeForBuild(_activeTab);

        var dataList = _activeTab == 0 ? _npcList : _playerList;
        var currentPage = _paginationBars[_activeTab]?.CurrentPage ?? 0;
        _progressiveTab = _activeTab;
        _progressiveDataList = dataList;
        _progressivePageStart = currentPage * PageSize;
        _progressivePageEnd = Math.Min(_progressivePageStart + PageSize, dataList.Count);
        _progressiveIndex = _progressivePageStart;
    }

    /// <summary>
    /// Page-only rebuild: data is already loaded, just rebuild the tree for the current page.
    /// </summary>
    private void StartProgressivePage(int tabIndex)
    {
        PrepareTreeForBuild(tabIndex);

        var dataList = tabIndex == 0 ? _npcList : _playerList;
        var currentPage = _paginationBars[tabIndex]?.CurrentPage ?? 0;
        _progressiveTab = tabIndex;
        _progressiveDataList = dataList;
        _progressivePageStart = currentPage * PageSize;
        _progressivePageEnd = Math.Min(_progressivePageStart + PageSize, dataList.Count);
        _progressiveIndex = _progressivePageStart;
    }

    /// <summary>
    /// Each frame: load DB data for a batch of NPCs and immediately create their tree nodes.
    /// </summary>
    private void ContinueProgressive()
    {
        var tree = _treePanels[_progressiveTab];
        if (tree == null) { _progressiveIndex = -1; return; }

        var isPlayer = _progressiveTab == 1;
        var batchEnd = Math.Min(_progressiveIndex + BatchSize, _progressivePageEnd);

        for (var idx = _progressiveIndex; idx < batchEnd; idx++)
        {
            var mapData = _progressiveDataList[idx];
            var npcKey = mapData.ToString();

            // Resolve + cache the character id once per NPC.
            if (!_charIdCache.ContainsKey(npcKey))
            {
                var character = _db.FindCharacter(mapData.Name, mapData.Gender, mapData.Race, (int)mapData.Language);
                if (character != null)
                    _charIdCache[npcKey] = character.Id;
            }

            // Show every mapped NPC as a header regardless of clip count — migrated configs and
            // unspoken-to NPCs still need to surface (an empty row list under the header).
            ReadOnlySeString header = $"{mapData.Name}  |  {mapData.Gender}  |  {mapData.Race}";
            _progressiveOptions[header] = BuildRowsForNpc(npcKey, mapData, isPlayer);
        }

        _progressiveIndex = batchEnd;
        // Re-assign so the virtualized tree picks up the newly added headers/rows. The setter
        // rebuilds only the pooled visible nodes and does NOT touch CollapsedEntries/scrollPosition,
        // so collapse + scroll state are preserved across incremental batches and count refreshes.
        tree.Options = _progressiveOptions;
        // The setter may have rebuilt the header node pool — recolor the category labels to match
        // the plugin's normal labels (they default to the native journal-header brown).
        RecolorTreeHeaders(tree);

        // Done
        if (_progressiveIndex >= _progressivePageEnd)
        {
            _progressiveIndex = -1;
            _progressiveTab = -1;
        }
    }

    /// <summary>
    /// Builds the quest-type group rows for one NPC (MSQ first, then by type). Each row carries the
    /// group's clips so the detail window can open without re-querying. Returns an empty list for
    /// NPCs with no resolved character or no clips (the header still shows).
    /// </summary>
    private List<VcRow> BuildRowsForNpc(string npcKey, NpcMapData mapData, bool isPlayer)
    {
        var rows = new List<VcRow>();
        if (!_charIdCache.TryGetValue(npcKey, out var charId))
            return rows;

        var allVoiceClips = _db.GetVoiceClipsForCharacter(charId, 100000);
        if (_selectedQuestType >= 0)
            allVoiceClips = allVoiceClips.FindAll(vc => vc.QuestType == _selectedQuestType);

        var byType = new Dictionary<int, List<VoiceClipEntity>>();
        foreach (var vc in allVoiceClips)
        {
            if (!byType.TryGetValue(vc.QuestType, out var list))
            {
                list = new List<VoiceClipEntity>();
                byType[vc.QuestType] = list;
            }
            list.Add(vc);
        }

        foreach (var (qt, vcs) in byType.OrderBy(kvp => kvp.Key == (int)QuestType.MSQ ? 0 : kvp.Key))
        {
            var label = isPlayer ? Loc.S("Chat") : GetQuestTypeLabel(qt);
            var ordered = vcs.OrderByDescending(vc => vc.Timestamp).ToList();
            var savedCount = ordered.Count(vc => _voiceClipManager.HasLocalAudio(vc));
            QuestTypeIcons.TryGetValue(qt, out var iconId);
            rows.Add(new VcRow
            {
                NpcKey = npcKey,
                DetailTitle = $"{mapData.Name} — {label}",
                Label = label,
                Subtitle = $"{ordered.Count} {Loc.S("Voice Clips")} | {savedCount} {Loc.S("Generated")}",
                IconId = iconId,
                VoiceClips = ordered,
            });
        }

        return rows;
    }

    // Bulk "Generate All Unsaved" — fully disabled in None mode (no backend to call). Dim
    // *and* IsEnabled=false so clicks are swallowed by ATK instead of running through the
    // no-op generation gate. Per-clip Generate buttons are handled in the detail window.
    private void UpdateGenAllButtonState(bool liveGen)
    {
        if (_genAllToggleButton == null) return;
        _genAllToggleButton.Alpha = liveGen ? 1.0f : 0.4f;
        if (_genAllBtnEnabledState != liveGen)
        {
            _genAllBtnEnabledState = liveGen;
            _genAllToggleButton.IsEnabled = liveGen;
        }
    }

    private void UpdateStatusBar()
    {
        if (_statusBar == null) return;

        // Backend reachability label (bottom-left). In None mode there is no backend to
        // ping — show a neutral "Audio Files Only" instead of the misleading "online" the
        // ping returns by design (see BackendService.PingBackendAsync). Otherwise: cached
        // for 30s so this is cheap to call repeatedly; if no cached value yet, kick off
        // an async check.
        if (_backendStatusLabel != null)
        {
            if (!_config.HasLiveGeneration)
            {
                _backendStatusLabel.String = Loc.S("Audio Files Only");
                _backendStatusLabel.TextColor = new System.Numerics.Vector4(0.75f, 0.75f, 0.75f, 1f); // neutral grey
            }
            else
            {
                var reachability = _backend.CachedReachability;
                if (reachability == null)
                    _ = _backend.IsBackendReachableAsync();
                _backendStatusLabel.String = reachability switch
                {
                    true => Loc.S("Backend: online"),
                    false => Loc.S("Backend: offline"),
                    _ => Loc.S("Backend: checking..."),
                };
                // FFXIV's color codes via UI ATK: tint via TextColor (RGBA Vector4).
                _backendStatusLabel.TextColor = reachability == false
                    ? new System.Numerics.Vector4(1.0f, 0.4f, 0.4f, 1f)   // red-ish for offline
                    : new System.Numerics.Vector4(0.6f, 0.85f, 0.6f, 1f); // greenish for online/checking
            }
        }

        // Harvest progress lives in the Game Data Tools window now; this status bar is
        // reserved for voice clip generation totals.
        var isGenerating = _genAllRunning || _voiceClipManager.IsGenerating;
        var questLabel = _questTypeLabels != null && _selectedQuestType >= 0
            ? _questTypeLabels[Array.IndexOf(QuestTypeValues, _selectedQuestType)]
            : (_questTypeLabels != null ? _questTypeLabels[0] : Loc.S("All"));
        _statusBar.ActionText = isGenerating
            ? Loc.S("Generating voice clips...")
            : string.Format(Loc.S("{0} generation progress"), questLabel);

        // Show overall generated/total counts — computed on background thread
        _statusUpdateCounter++;
        var updateInterval = isGenerating ? 10 : 60;
        if ((_statusForceRecompute || _statusUpdateCounter >= updateInterval) && !_statusCalcRunning)
        {
            _statusUpdateCounter = 0;
            _statusForceRecompute = false;
            _statusCalcRunning = true;
            var langInt = (int)_clientState.ClientLanguage;
            var playerId = (long)_gameObjects.LocalPlayerContentId;
            int? questFilter = _selectedQuestType >= 0 ? _selectedQuestType : null;
            var contextType = _activeTab == 0 ? "npc" : "player";
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var (clips, saved) = _db.GetClipTotalsForLanguage(langInt, contextType, playerId, questFilter);
                    _statusTotalClips = clips;
                    _statusTotalSaved = saved;
                }
                finally
                {
                    _statusCalcRunning = false;
                }
            });
        }

        var tc = _statusTotalClips;
        var ts = _statusTotalSaved;
        var fraction = tc > 0 ? (float)ts / tc : 0f;
        _statusBar.SetProgress(fraction, $"{ts}/{tc}");
    }

    private void PrepareTreeForBuild(int tabIndex)
    {
        var tree = _treePanels[tabIndex];
        if (tree == null) return;

        // The virtualized tree is reusable — start a fresh Options dictionary instead of
        // disposing/recreating the node (which was the old ScrollingTreeNode crash source).
        _progressiveOptions = new Dictionary<ReadOnlySeString, List<VcRow>>();
        tree.Options = _progressiveOptions;

        _panelBuilt[tabIndex] = true;

        for (var i = 0; i < 2; i++)
            SetVisible(_treePanels[i], i == tabIndex);
    }

    // Quest type icon IDs (FFXIV quest marker icons)
    private static readonly Dictionary<int, uint> QuestTypeIcons = new()
    {
        { (int)QuestType.None, 61502 },       // Chat bubble icon
        { (int)QuestType.MSQ, 71201 },        // Meteor (MSQ)
        { (int)QuestType.SideQuest, 71221 },  // ! (side quest)
        { (int)QuestType.Unlock, 71341 },     // Blue + (unlock)
        { (int)QuestType.BeastTribe, 71221 }, // ! (beast tribe)
        { (int)QuestType.Repeatable, 71261 }, // Repeatable
        { (int)QuestType.Event, 71221 },      // ! (event)
    };

    private static string GetQuestTypeLabel(int questType) => questType switch
    {
        (int)QuestType.MSQ => Loc.S("Main Scenario"),
        (int)QuestType.SideQuest => Loc.S("Side Quest"),
        (int)QuestType.Unlock => Loc.S("Unlock / Class Quest"),
        (int)QuestType.BeastTribe => Loc.S("Beast Tribe"),
        (int)QuestType.Repeatable => Loc.S("Repeatable"),
        (int)QuestType.Event => Loc.S("Seasonal Event"),
        _ => Loc.S("Non-Quest Dialog"),
    };

    // ── Helpers ──────────────────────────────────────────────

    // KTK's TreeListNode renders category headers via an internal ToggleableHeaderNode whose label
    // defaults to ColorHelper.GetColor(1) (the native journal-header brown). We want them to match
    // the plugin's normal labels (LabelColor = GetColor(50)). The header nodes live in a private list
    // with no public accessor and the node type is internal, so reach them via reflection and set the
    // public LabelTextNode.TextColor. Fails soft (no-op) if the KTK internals ever change — headers
    // just keep their default color, no crash. Reapplied after every Options assignment since the
    // setter may rebuild the header pool.
    private static readonly System.Reflection.PropertyInfo? HeaderNodesProp =
        typeof(TreeListNode<VcRow, VoiceClipRowNode>).GetProperty(
            "HeaderNodes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    private static System.Reflection.PropertyInfo? _headerLabelProp;

    private static void RecolorTreeHeaders(TreeListNode<VcRow, VoiceClipRowNode> tree)
    {
        if (HeaderNodesProp?.GetValue(tree) is not System.Collections.IEnumerable headers) return;
        foreach (var header in headers)
        {
            if (header == null) continue;
            _headerLabelProp ??= header.GetType().GetProperty("LabelTextNode");
            if (_headerLabelProp?.GetValue(header) is TextNode label)
                label.TextColor = LabelColor;
        }
    }

    private void OpenNpcEdit(NpcMapData mapData)
    {
        // Close any existing edit popup before opening a new one.
        _npcEditWindow?.Dispose();
        _npcEditWindow = new NativeNpcEditWindow(mapData, _npcData, _log, _config, OnNpcEdited)
        {
            InternalName = "EKNpcEdit",
            Title = $"{Loc.S("Edit Character")}: {mapData.Name}",
            Size = new Vector2(420, 380),
        };
        _npcEditWindow.Open();
    }

    private void OnNpcEdited()
    {
        // The edited character's identity (Name+Gender+Race composite) may have changed,
        // so all caches keyed by that composite need to drop. Easiest: invalidate the active panel.
        _charIdCache.Clear();
        for (var i = 0; i < _panelDirty.Length; i++) _panelDirty[i] = true;
        _statusForceRecompute = true;
    }



    public override void Dispose()
    {
        // Close before dispose: NativeAddon.Close() is async (framework-thread queued),
        // so calling Close()→Dispose() explicitly gives the ATK detach a chance to start
        // before managed cleanup runs. Reduces dangling-pointer crashes on plugin unload.
        try { if (_npcEditWindow?.IsOpen == true) _npcEditWindow.Close(); } catch { }
        try { _npcEditWindow?.Dispose(); } catch { }
        _npcEditWindow = null;
        base.Dispose();
    }
}
