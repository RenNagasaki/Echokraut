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
using KamiToolKit.Nodes;

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
    private const float ScrollbarWidth = 16f;

    // Filter
    private TextInputNode? _filterInput;
    private string _filterText = "";

    // Quest type filter
    private TextDropDownNode? _questTypeDropDown;
    private int _selectedQuestType = -1; // -1 = All
    private int _pendingQuestTypeSelection = -1;
    private string[]? _questTypeLabels;
    // Maps dropdown index → QuestType enum value (-1 = all, 0-6 = enum values)
    private static readonly int[] QuestTypeValues = { -1, 1, 2, 3, 4, 5, 6, 0 };

    // Tabs
    private TabBarNode? _tabBar;
    private int _activeTab;
    private ScrollingTreeNode?[] _treePanels = new ScrollingTreeNode?[2];
    private bool[] _panelDirty = { true, true };
    private bool[] _panelBuilt = { false, false };
    private readonly List<ScrollingTreeNode> _oldTrees = new();

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

    // Progressive load+build — each frame loads a batch from DB and creates nodes immediately
    private const int BatchSize = 5;
    private int _progressiveIndex = -1; // -1 = idle
    private int _progressiveTab = -1;
    private List<NpcMapData> _progressiveDataList = new();
    private int _progressiveVisibleCount; // NPCs that passed the quest type filter
    private int _progressivePageStart;
    private int _progressivePageEnd;
    private bool _firstFrame = true; // skip first OnUpdate to let OnSetup settle

    // Caches
    private readonly Dictionary<string, int> _vcCountCache = new();
    private readonly Dictionary<string, int> _savedCountCache = new();
    private readonly Dictionary<string, int> _charIdCache = new();
    private readonly Dictionary<long, string> _instanceZoneCache = new();
    private readonly Dictionary<string, List<(long npcBaseId, List<VoiceClipEntity> voiceClips)>> _npcInstanceCache = new();

    // Expanded state — survives rebuilds
    private readonly HashSet<string> _expandedNpcs = new();

    // Detail window reference
    private NativeVoiceClipDetailWindow? _detailWindow;
    private ListButtonNode? _selectedInstanceButton;

    // NPC edit popup reference (Race/Gender override)
    private NativeNpcEditWindow? _npcEditWindow;

    // Tracked nodes for text-only updates (no rebuild needed)
    private readonly Dictionary<string, TreeListCategoryNode> _npcCategoryNodes = new();
    private readonly List<(string npcKey, int questType, IconListItemNode node)> _subGroupNodes = new();
    private bool _countsNeedRefresh;

    private readonly Configuration _config;

    // Game-Data-Tools icon button "enabled" snapshot — gates NodeFlags toggles on
    // transitions only (CLAUDE.md: AddNodeFlags every frame can crash the game).
    private bool? _gameDataBtnEnabledState;

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
        _questTypeDropDown = new TextDropDownNode
        {
            Size = new Vector2(180, topRowH),
            Position = pos,
            Options = [],
        };
        _questTypeDropDown.OptionListNode.Options = new List<string>(_questTypeLabels);
        _questTypeDropDown.OptionListNode.SelectedOption = _questTypeLabels[0];
        if (_questTypeDropDown.LabelNode.Node != null)
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
            Icon = ButtonIcon.GearCog,
            Tooltip = Loc.S("Open configuration window"),
            OnClick = () => _toggleConfig(),
        };
        // Manual hover highlight + tooltip — in NativeAddon contexts only ImageNode events
        // fire reliably (same reason DynamicIconButtonNode reroutes OnClick to ImageNode).
        // The Tooltip setter registers MouseOver/MouseOut on the button itself, which never
        // fires here, so we drive ShowTooltip/HideTooltip manually from the ImageNode events
        // we know work.
        var normalTint = new Vector3(1f, 1f, 1f);
        var hoverTint = new Vector3(1.4f, 1.4f, 1.4f);
        _settingsButton.ImageNode.MultiplyColor = normalTint;
        _settingsButton.ImageNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (_settingsButton == null) return;
            _settingsButton.ImageNode.MultiplyColor = hoverTint;
            _settingsButton.ShowTooltip();
        });
        _settingsButton.ImageNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (_settingsButton == null) return;
            _settingsButton.ImageNode.MultiplyColor = normalTint;
            _settingsButton.HideTooltip();
        });
        AddNode(_settingsButton);

        // Game Data Tools button — opens the bulk-data window (harvest + voice starter set).
        // Same DynamicIconButtonNode pattern as the settings button; sits one slot to its right.
        // Icon UV (168, 84) on Character.tex matches ButtonIcon.GearCogWithChatBubble — a gear
        // wedded to a speech bubble, fitting for "data + dialog tooling".
        _gameDataToolsButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X + settingsBtnSize + settingsBtnGap, pagY),
            Size = new Vector2(settingsBtnSize, settingsBtnSize),
            Icon = ButtonIcon.GearCogWithChatBubble,
            Tooltip = Loc.S("Open Game Data Tools window"),
            OnClick = () => _toggleGameDataTools(),
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
        };
        AddNode(_backendStatusLabel);

        for (var i = 0; i < 2; i++)
        {
            _treePanels[i] = new ScrollingTreeNode
            {
                Position = _treePos,
                Size = _treeSize,
                CategoryVerticalSpacing = 2,
            };
            AddNode(_treePanels[i]!);

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

        // Bulk Generate All Unsaved button — dim in None mode (no backend to call). The
        // per-clip Generate buttons in the detail window are handled by that window's own
        // OnUpdate dim pass; here we only own the bulk button at the top of VCM.
        var liveGen = _config.Alltalk.HasLiveGeneration;
        if (_genAllToggleButton != null)
            _genAllToggleButton.Alpha = liveGen ? 1.0f : 0.4f;

        // Game Data Tools icon button: route through ATK's component-disabled state via
        // ButtonBase.IsEnabled. Plain alpha + NodeFlags-on-ImageNode left the underlying
        // AtkComponentButton alive (cursor still flipped, click animation still played,
        // tooltip still showed) because all of those live on the component, not the icon
        // node. SetEnabledState triggers the FFXIV-standard disabled timeline
        // (alpha ~0.7, multiplier 0.5) AND silences the cursor / click pipeline at ATK
        // level. Transition-tracked since SetEnabledState writes ATK struct fields.
        if (_gameDataToolsButton != null && _gameDataBtnEnabledState != liveGen)
        {
            _gameDataBtnEnabledState = liveGen;
            _gameDataToolsButton.IsEnabled = liveGen;
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

        // Refresh generated counts without rebuilding (e.g. after single clip generation)
        if (_countsNeedRefresh)
        {
            _countsNeedRefresh = false;
            RefreshCounts();
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

        _vcCountCache.Clear();
        _savedCountCache.Clear();
        _charIdCache.Clear();
        _instanceZoneCache.Clear();
        _npcInstanceCache.Clear();

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
        _progressiveVisibleCount = 0;
    }

    /// <summary>
    /// Page-only rebuild: data is already loaded, just rebuild the tree for the current page.
    /// </summary>
    private void StartProgressivePage(int tabIndex)
    {
        _npcInstanceCache.Clear();
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
        var w = _contentWidth - ScrollbarWidth;
        var batchEnd = Math.Min(_progressiveIndex + BatchSize, _progressivePageEnd);

        for (var idx = _progressiveIndex; idx < batchEnd; idx++)
        {
            var mapData = _progressiveDataList[idx];
            var npcKey = mapData.ToString();

            // Load DB data for this NPC if not cached
            if (!_vcCountCache.ContainsKey(npcKey))
            {
                var character = _db.FindCharacter(mapData.Name, mapData.Gender, mapData.Race, (int)mapData.Language);
                if (character != null)
                {
                    _charIdCache[npcKey] = character.Id;
                    var allClips = _db.GetVoiceClipsForCharacter(character.Id, 100000);
                    if (_selectedQuestType >= 0)
                        allClips = allClips.FindAll(vc => vc.QuestType == _selectedQuestType);
                    _vcCountCache[npcKey] = allClips.Count;
                    var playerId = (long)_gameObjects.LocalPlayerContentId;
                    _savedCountCache[npcKey] = _db.GetGeneratedCountForCharacter(character.Id, playerId);
                    foreach (var inst in _db.GetInstancesForCharacter(character.Id))
                        if (!string.IsNullOrEmpty(inst.ZoneName))
                            _instanceZoneCache[inst.NpcBaseId] = inst.ZoneName;
                }
                else
                    _vcCountCache[npcKey] = 0;
            }

            // Show every mapped NPC regardless of clip count — migrated configs and
            // unspoken-to NPCs still need to surface so users can edit voice / race /
            // gender. NPCs with zero clips just have an empty children list when expanded.
            // (When a quest-type filter is active the upstream filter in BuildPage already
            //  removes mismatching NPCs, so this scope is correct.)
            _vcCountCache.TryGetValue(npcKey, out var vcCount);
            var wasExpanded = _expandedNpcs.Contains(npcKey);
            var npcCategory = new TreeListCategoryNode
            {
                Size = new Vector2(w, 28),
                String = $"{mapData.Name}  |  {mapData.Gender}  |  {mapData.Race}",
                IsCollapsed = !wasExpanded,
            };

            var capturedKey = npcKey;
            var capturedMapData = mapData;
            var capturedIsPlayer = isPlayer;
            var capturedW = w;
            var capturedTree = tree;
            npcCategory.OnToggle = expanded =>
            {
                if (expanded)
                {
                    _expandedNpcs.Add(capturedKey);
                    PopulateNpcCategory(npcCategory, capturedKey, capturedMapData, capturedIsPlayer, capturedW, capturedTree);
                }
                else
                    _expandedNpcs.Remove(capturedKey);
            };

            if (wasExpanded)
                PopulateNpcCategory(npcCategory, npcKey, mapData, isPlayer, w, tree);

            tree.AddCategoryNode(npcCategory);
            _npcCategoryNodes[npcKey] = npcCategory;
            _progressiveVisibleCount++;
        }

        _progressiveIndex = batchEnd;
        tree.RecalculateLayout();

        // Done
        if (_progressiveIndex >= _progressivePageEnd)
        {
            _progressiveIndex = -1;
            _progressiveTab = -1;
        }
    }

    private void UpdateStatusBar()
    {
        if (_statusBar == null) return;

        // Backend reachability label (bottom-left). Cached for 30s in BackendService so this
        // is cheap to call repeatedly; if no cached value yet, kick off an async check.
        var reachability = _backend.CachedReachability;
        if (reachability == null)
            _ = _backend.IsBackendReachableAsync();
        if (_backendStatusLabel != null)
        {
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

    private void RefreshCounts()
    {
        var playerId = (long)_gameObjects.LocalPlayerContentId;

        // Update NPC category labels
        foreach (var (npcKey, node) in _npcCategoryNodes)
        {
            if (!_charIdCache.TryGetValue(npcKey, out var charId)) continue;
            var total = _vcCountCache.TryGetValue(npcKey, out var tc) ? tc : 0;
            var saved = _db.GetGeneratedCountForCharacter(charId, playerId);
            _savedCountCache[npcKey] = saved;

            var mapData = _npcData.MappedNpcs.Find(n => n.ToString() == npcKey)
                       ?? _npcData.MappedPlayers.Find(n => n.ToString() == npcKey);
            if (mapData != null)
                node.String = $"{mapData.Name}  |  {mapData.Gender}  |  {mapData.Race}";
        }

        // Update sub-group subtitles
        foreach (var (npcKey, questType, btn) in _subGroupNodes)
        {
            if (!_charIdCache.TryGetValue(npcKey, out var charId)) continue;
            var clips = _db.GetVoiceClipsForCharacter(charId, 100000);
            if (_selectedQuestType >= 0)
                clips = clips.FindAll(vc => vc.QuestType == _selectedQuestType);
            var typeClips = clips.FindAll(vc => vc.QuestType == questType);
            var savedCount = typeClips.Count(vc => _voiceClipManager.HasLocalAudio(vc));
            btn.Subtitle = $"{typeClips.Count} {Loc.S("Voice Clips")} | {savedCount} {Loc.S("Generated")}";
        }

        // Update stats headers
        _npcInstanceCache.Clear();
    }

    private void PrepareTreeForBuild(int tabIndex)
    {
        _npcCategoryNodes.Clear();
        _subGroupNodes.Clear();
        _selectedInstanceButton = null;

        var tree = _treePanels[tabIndex];
        if (tree == null) return;

        if (_panelBuilt[tabIndex])
        {
            tree.IsVisible = false;
            _oldTrees.Add(tree);

            tree = new ScrollingTreeNode
            {
                Position = _treePos,
                Size = _treeSize,
                CategoryVerticalSpacing = 2,
            };
            _treePanels[tabIndex] = tree;
            AddNode(tree);
        }

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

    private void PopulateNpcCategory(TreeListCategoryNode category, string npcKey, NpcMapData mapData, bool isPlayer, float w, ScrollingTreeNode tree)
    {
        var npcName = mapData.Name;
        // Only populate once (lazy load on expand)
        if (category.Children.Any()) return;

        // Stats header (journal separator style)
        _vcCountCache.TryGetValue(npcKey, out var totalClips);
        _savedCountCache.TryGetValue(npcKey, out var totalSaved);
        var statsHeader = new TreeListHeaderNode
        {
            Size = new Vector2(w - 24, 24),
            String = $"{totalClips} {Loc.S("Voice Clips")}  |  {totalSaved} {Loc.S("Generated")}",
        };
        category.AddNode(statsHeader);

        // Action buttons row (Generate All / Delete All)
        var capturedNpcKey = npcKey;
        var actionsRow = new HorizontalListNode { Size = new Vector2(w - 24, 26), ItemSpacing = 4 };
        var capturedQuestTypeFilter = _selectedQuestType;
        actionsRow.AddNode(Button(Loc.S("Generate All Unsaved"), 160, () =>
        {
            if (_voiceClipManager.IsGenerating) return;
            if (_charIdCache.TryGetValue(capturedNpcKey, out var genCharId))
            {
                var voiceClips = _db.GetVoiceClipsForCharacter(genCharId, 100000);
                if (capturedQuestTypeFilter >= 0)
                    voiceClips = voiceClips.FindAll(vc => vc.QuestType == capturedQuestTypeFilter);
                _voiceClipManager.GenerateAllUnsaved(voiceClips).ContinueWith(_ =>
                {
                    _npcInstanceCache.Remove(capturedNpcKey);
                    _panelDirty[_activeTab] = true;
                });
            }
        }));
        actionsRow.AddNode(Button(Loc.S("Delete All Saved"), 140, () =>
        {
            if (_voiceClipManager.IsGenerating) return;
            _audioPlayback.ClearQueue(TextSource.VoiceTest);

            if (_charIdCache.TryGetValue(capturedNpcKey, out var delCharId))
            {
                var voiceClips = _db.GetVoiceClipsForCharacter(delCharId, 100000);
                if (capturedQuestTypeFilter >= 0)
                    voiceClips = voiceClips.FindAll(vc => vc.QuestType == capturedQuestTypeFilter);
                _voiceClipManager.DeleteAllSaved(voiceClips);
                _npcInstanceCache.Remove(capturedNpcKey);
                _panelDirty[_activeTab] = true;
            }
        }));
        var capturedMapData = mapData;
        actionsRow.AddNode(Button(Loc.S("Edit Character"), 130, () => OpenNpcEdit(capturedMapData)));
        category.AddNode(actionsRow);

        // Group voice clips by quest type
        if (!_npcInstanceCache.TryGetValue(npcKey, out var questTypeGroups))
        {
            questTypeGroups = new List<(long npcBaseId, List<VoiceClipEntity> voiceClips)>();
            if (_charIdCache.TryGetValue(npcKey, out var charId))
            {
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
                // Sort: MSQ first, then by count descending
                foreach (var (qt, vcs) in byType.OrderBy(kvp => kvp.Key == (int)QuestType.MSQ ? 0 : kvp.Key))
                    questTypeGroups.Add((qt, vcs.OrderByDescending(vc => vc.Timestamp).ToList()));
            }
            _npcInstanceCache[npcKey] = questTypeGroups;
        }

        foreach (var (questTypeLong, voiceClips) in questTypeGroups)
        {
            var questTypeInt = (int)questTypeLong;
            var label = isPlayer ? Loc.S("Chat") : GetQuestTypeLabel(questTypeInt);
            var savedCount = voiceClips.Count(vc => _voiceClipManager.HasLocalAudio(vc));
            var subtitle = $"{voiceClips.Count} {Loc.S("Voice Clips")} | {savedCount} {Loc.S("Generated")}";

            var itemW = w - 48;
            const float itemH = 41f; // 36px content + 5px spacing

            QuestTypeIcons.TryGetValue(questTypeInt, out var iconId);
            var btn = new IconListItemNode
            {
                Size = new Vector2(itemW, itemH),
                Title = label,
                Subtitle = subtitle,
                IconId = iconId,
            };

            var capturedVoiceClips = voiceClips;
            var capturedTitle = $"{npcName} — {label}";
            var capturedKey = npcKey;
            var capturedBtn = btn;
            btn.OnClick = () =>
            {
                try { if (_selectedInstanceButton != null) _selectedInstanceButton.Selected = false; }
                catch { /* may be disposed */ }
                capturedBtn.Selected = true;
                _selectedInstanceButton = capturedBtn;
                _detailWindow?.ShowVoiceClips(capturedTitle, capturedVoiceClips, capturedKey);
            };

            category.AddNode(btn);
            _subGroupNodes.Add((npcKey, questTypeInt, btn));
        }

        category.RecalculateLayout();
        tree.RecalculateLayout();
    }

    // ── Helpers ──────────────────────────────────────────────

    private static TextNode Label(string text, float width) => new()
    {
        Size = new Vector2(width, 18),
        String = text,
        FontType = FontType.Axis,
        FontSize = 12,
    };

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
        _vcCountCache.Clear();
        _savedCountCache.Clear();
        _npcInstanceCache.Clear();
        _expandedNpcs.Clear();
        for (var i = 0; i < _panelDirty.Length; i++) _panelDirty[i] = true;
        _statusForceRecompute = true;
    }

    private static TextButtonNode Button(string label, float minWidth, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(minWidth, 24), String = label };
        var textW = node.LabelNode.GetTextDrawSize(label).X + 36;
        if (textW > minWidth) node.Size = new Vector2(textW, 24);
        node.OnClick = onClick;
        return node;
    }

    private static void SetVisible(NodeBase? node, bool visible)
    {
        if (node != null) node.IsVisible = visible;
    }

    public override void Dispose()
    {
        // Close before dispose: NativeAddon.Close() is async (framework-thread queued),
        // so calling Close()→Dispose() explicitly gives the ATK detach a chance to start
        // before managed cleanup runs. Reduces dangling-pointer crashes on plugin unload.
        try { if (_npcEditWindow?.IsOpen == true) _npcEditWindow.Close(); } catch { }
        try { _npcEditWindow?.Dispose(); } catch { }
        _npcEditWindow = null;
        foreach (var tree in _oldTrees)
        {
            try { tree.Dispose(); } catch { }
        }
        _oldTrees.Clear();
        base.Dispose();
    }
}
