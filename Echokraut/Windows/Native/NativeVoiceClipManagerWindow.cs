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
    private readonly IDialogHarvestService _dialogHarvest;
    private readonly IClientState _clientState;

    // Harvest
    private CancellationTokenSource? _harvestCts;
    private TextButtonNode? _harvestButton;
    private TextDropDownNode? _langDropDown;
    private TextNode? _harvestProgressNode;
    private ClientLanguage _selectedLanguage;
    private string _harvestProgress = "";
    private bool _pendingHarvestClick;
    private int _pendingLangSelection = -1;

    // Layout
    private float _contentWidth;
    private const float ScrollbarWidth = 16f;

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

    // Caches
    private readonly Dictionary<string, int> _encCountCache = new();
    private readonly Dictionary<string, int> _savedCountCache = new();
    private readonly Dictionary<string, int> _charIdCache = new();
    private readonly Dictionary<long, (string zone, float x, float y)> _instanceLocationCache = new();
    private readonly Dictionary<string, List<(long npcBaseId, List<VoiceClipEntity> encounters)>> _npcInstanceCache = new();

    // Expanded state — survives rebuilds
    private readonly HashSet<string> _expandedNpcs = new();

    // Detail window reference
    private NativeVoiceClipDetailWindow? _detailWindow;

    public NativeVoiceClipManagerWindow(
        IDatabaseService db,
        IVoiceClipManagerService voiceClipManager,
        IAudioPlaybackService audioPlayback,
        INpcDataService npcData,
        IDialogHarvestService dialogHarvest,
        IClientState clientState)
    {
        _db = db;
        _voiceClipManager = voiceClipManager;
        _audioPlayback = audioPlayback;
        _npcData = npcData;
        _dialogHarvest = dialogHarvest;
        _clientState = clientState;
        _selectedLanguage = clientState.ClientLanguage;

        _voiceClipManager.VoiceClipUpdated += () =>
        {
            _needsRebuild = true;
            _npcInstanceCache.Clear();
        };
        _db.VoiceClipLogged += () => _needsRebuild = true;
        _dialogHarvest.ProgressChanged += msg => _harvestProgress = msg;
    }

    public void SetDetailWindow(NativeVoiceClipDetailWindow detailWindow)
    {
        _detailWindow = detailWindow;
    }

    private static readonly string[] LangLabels = { "Japanese", "English", "German", "French" };

    protected override void OnSetup(AtkUnitBase* addon)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        _contentWidth = size.X;
        const float harvestRowH = 28f;

        // Harvest button + language dropdown row
        _harvestButton = new TextButtonNode
        {
            Size = new Vector2(120, harvestRowH),
            Position = pos,
            String = Loc.S("Start Harvest"),
            OnClick = () => _pendingHarvestClick = true,
        };
        AddNode(_harvestButton);

        _langDropDown = new TextDropDownNode
        {
            Size = new Vector2(130, harvestRowH),
            Position = pos + new Vector2(124, 0),
            Options = [],
        };
        _langDropDown.OptionListNode.Options = new List<string>(LangLabels);
        _langDropDown.OptionListNode.SelectedOption = LangLabels[(int)_selectedLanguage];
        if (_langDropDown.LabelNode.Node != null)
            _langDropDown.LabelNode.String = LangLabels[(int)_selectedLanguage];
        _langDropDown.OnOptionSelected = selected => _pendingLangSelection = Array.IndexOf(LangLabels, selected);
        AddNode(_langDropDown);

        _harvestProgressNode = new TextNode
        {
            Size = new Vector2(size.X - 260, harvestRowH),
            Position = pos + new Vector2(260, 0),
        };
        AddNode(_harvestProgressNode);

        var tabY = pos.Y + harvestRowH + 4;
        const float tabH = 32f;

        _tabBar = new TabBarNode { Size = new Vector2(size.X, tabH), Position = new Vector2(pos.X, tabY) };
        _tabBar.AddTab(Loc.S("NPCs"), () => ShowPanel(0));
        _tabBar.AddTab(Loc.S("Players"), () => ShowPanel(1));
        AddNode(_tabBar);

        const float paginationH = 28f;
        var treeTop = tabY + tabH + 2;
        _treePos = new Vector2(pos.X, treeTop);
        _treeSize = new Vector2(size.X, pos.Y + size.Y - treeTop - paginationH - 4);
        var pagY = pos.Y + size.Y - paginationH;

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

        // Process deferred harvest click
        if (_pendingHarvestClick)
        {
            _pendingHarvestClick = false;
            if (_dialogHarvest.IsRunning)
            {
                _harvestCts?.Cancel();
                if (_harvestButton != null) _harvestButton.String = Loc.S("Start Harvest");
            }
            else
            {
                _harvestCts?.Dispose();
                _harvestCts = new CancellationTokenSource();
                if (_harvestButton != null) _harvestButton.String = Loc.S("Stop Harvest");
                _ = _dialogHarvest.RunAsync(_selectedLanguage, _harvestCts.Token).ContinueWith(_ =>
                {
                    if (_harvestButton != null) _harvestButton.String = Loc.S("Start Harvest");
                });
            }
        }

        // Process deferred language selection
        if (_pendingLangSelection >= 0)
        {
            _selectedLanguage = (ClientLanguage)_pendingLangSelection;
            _pendingLangSelection = -1;
            _needsRebuild = true;
        }

        // Update harvest progress text
        if (_harvestProgressNode != null)
            _harvestProgressNode.String = _harvestProgress;

        if (_npcData.MappedNpcs.Count != _lastNpcCount || _npcData.MappedPlayers.Count != _lastPlayerCount)
            _needsRebuild = true;

        if (_needsRebuild)
        {
            LoadData();
            _panelDirty[_activeTab] = true;
        }

        _paginationBars[_activeTab]?.Update();

        if (_panelDirty[_activeTab])
        {
            _panelDirty[_activeTab] = false;
            RebuildPanel(_activeTab);
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
        _panelDirty[index] = true;
    }

    private void LoadData()
    {
        _needsRebuild = false;
        _npcList = _npcData.MappedNpcs.FindAll(n => n.Language == _selectedLanguage);
        _playerList = _npcData.MappedPlayers.FindAll(n => n.Language == _selectedLanguage);
        _lastNpcCount = _npcData.MappedNpcs.Count;
        _lastPlayerCount = _npcData.MappedPlayers.Count;

        _encCountCache.Clear();
        _savedCountCache.Clear();
        _charIdCache.Clear();
        _instanceLocationCache.Clear();
        
        foreach (var mapData in _npcList.Concat(_playerList))
        {
            var key = mapData.ToString();
            if (_encCountCache.ContainsKey(key)) continue;
            var character = _db.FindCharacter(mapData.Name, mapData.Gender, mapData.Race, (int)mapData.Language);
            if (character != null)
            {
                _charIdCache[key] = character.Id;
                _encCountCache[key] = _db.GetVoiceClipCountForCharacter(character.Id);
                _savedCountCache[key] = _db.GetSavedVoiceClipCountForCharacter(character.Id);
                foreach (var inst in _db.GetInstancesForCharacter(character.Id))
                    _instanceLocationCache[inst.NpcBaseId] = (inst.ZoneName, inst.MapX, inst.MapY);
            }
            else
                _encCountCache[key] = 0;
        }

        // Update pagination totals
        _paginationBars[0]?.SetTotalItems(_npcList.Count, PageSize);
        _paginationBars[1]?.SetTotalItems(_playerList.Count, PageSize);
    }

    private void RebuildPanel(int tabIndex)
    {
        var tree = _treePanels[tabIndex];
        if (tree == null) return;

        // Clear caches so stale data doesn't persist
        _npcInstanceCache.Clear();

        // If panel was built before, schedule old tree for deferred disposal
        // and create a fresh one. Don't dispose during OnUpdate — causes ATK crashes.
        if (_panelBuilt[tabIndex])
        {
            tree.IsVisible = false;
            _oldTrees.Add(tree);

            var pos = tree.Position;
            var size = tree.Size;
            tree = new ScrollingTreeNode
            {
                Position = pos,
                Size = size,
                CategoryVerticalSpacing = 2,
            };
            _treePanels[tabIndex] = tree;
            AddNode(tree);
        }

        _panelBuilt[tabIndex] = true;

        // Hide the other panel
        for (var i = 0; i < 2; i++)
            SetVisible(_treePanels[i], i == tabIndex);

        var dataList = tabIndex == 0 ? _npcList : _playerList;
        var isPlayer = tabIndex == 1;
        var w = _contentWidth - ScrollbarWidth;

        // Paginate
        var currentPage = _paginationBars[tabIndex]?.CurrentPage ?? 0;
        var pageStart = currentPage * PageSize;
        var pageEnd = Math.Min(pageStart + PageSize, dataList.Count);

        for (var idx = pageStart; idx < pageEnd; idx++)
        {
            var mapData = dataList[idx];
            var npcKey = mapData.ToString();
            _encCountCache.TryGetValue(npcKey, out var encCount);

            // NPC category node — restore expanded state from previous rebuild
            var wasExpanded = _expandedNpcs.Contains(npcKey);
            var npcCategory = new TreeListCategoryNode
            {
                Size = new Vector2(w, 28),
                String = $"{mapData.Name}  |  {mapData.Gender}  |  {mapData.Race}  |  {encCount} {Loc.S("Voice Clips")} | {(_savedCountCache.TryGetValue(npcKey, out var sc) ? sc : 0)} {Loc.S("Generated")}",
                IsCollapsed = !wasExpanded,
            };

            // Lazy-load instance groups on expand
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
                    PopulateNpcCategory(npcCategory, capturedKey, capturedIsPlayer, capturedW, capturedTree);
                }
                else
                    _expandedNpcs.Remove(capturedKey);
            };

            // If restoring expanded state, populate immediately
            if (wasExpanded)
                PopulateNpcCategory(npcCategory, npcKey, isPlayer, w, tree);

            tree.AddCategoryNode(npcCategory);
        }

        tree.RecalculateLayout();
    }

    private void PopulateNpcCategory(TreeListCategoryNode category, string npcKey, bool isPlayer, float w, ScrollingTreeNode tree)
    {
        // Only populate once
        if (category.Children.Any()) return;

        // Action buttons row (Generate All / Delete All)
        var capturedNpcKey = npcKey;
        var actionsRow = new HorizontalListNode { Size = new Vector2(w - 24, 26), ItemSpacing = 4 };
        actionsRow.AddNode(Button(Loc.S("Generate All Unsaved"), 160, () =>
        {
            if (_charIdCache.TryGetValue(capturedNpcKey, out var genCharId))
            {
                var encounters = _db.GetVoiceClipsForCharacter(genCharId, 10000);
                _voiceClipManager.GenerateAllUnsaved(encounters).ContinueWith(_ =>
                {
                    _npcInstanceCache.Remove(capturedNpcKey);
                                        _panelDirty[_activeTab] = true;
                });
            }
        }));
        actionsRow.AddNode(Button(Loc.S("Delete All Saved"), 140, () =>
        {
            _audioPlayback.ClearQueue(TextSource.VoiceTest);

            if (_charIdCache.TryGetValue(capturedNpcKey, out var delCharId))
            {
                var encounters = _db.GetVoiceClipsForCharacter(delCharId, 10000);
                _voiceClipManager.DeleteAllSaved(encounters);
                _npcInstanceCache.Remove(capturedNpcKey);
                                _panelDirty[_activeTab] = true;
            }
        }));
        category.AddNode(actionsRow);

        // Load instance groups
        if (!_npcInstanceCache.TryGetValue(npcKey, out var instanceGroups))
        {
            instanceGroups = new List<(long npcBaseId, List<VoiceClipEntity> encounters)>();
            if (_charIdCache.TryGetValue(npcKey, out var charId))
            {
                var allEncounters = _db.GetVoiceClipsForCharacter(charId, 500);
                var grouped = allEncounters
                    .GroupBy(e => e.NpcBaseId)
                    .OrderByDescending(g => g.Max(e => e.Timestamp));
                foreach (var group in grouped)
                    instanceGroups.Add((group.Key, group.OrderByDescending(e => e.Timestamp).ToList()));
            }
            _npcInstanceCache[npcKey] = instanceGroups;
        }

        foreach (var (npcBaseId, encounters) in instanceGroups)
        {
            // Instance label
            string instanceLabel;
            if (isPlayer)
                instanceLabel = Loc.S("Chat");
            else if (_instanceLocationCache.TryGetValue(npcBaseId, out var loc) && !string.IsNullOrEmpty(loc.zone))
                instanceLabel = $"{loc.zone} ({loc.x:F1}, {loc.y:F1})";
            else if (npcBaseId > 0)
                instanceLabel = $"ID: {npcBaseId}";
            else
                instanceLabel = Loc.S("Unknown");

            // Instance as nested collapsible TreeListCategoryNode
            // Crafting-log style clickable sub-category button
            var capturedEncounters = encounters;
            var capturedInstLabel = instanceLabel;
            var capturedInstKey = npcKey;
            var instanceButton = new ListButtonNode
            {
                Size = new Vector2(w - 48, 24),
                String = $"{instanceLabel}  —  {encounters.Count} {Loc.S("Voice Clips")} | {encounters.Count(e => e.SavedToDisk)} {Loc.S("Generated")}",
            };
            instanceButton.OnClick = () =>
            {
                _detailWindow?.ShowEncounters(capturedInstLabel, capturedEncounters, capturedInstKey);
            };

            category.AddNode(instanceButton);
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
        foreach (var tree in _oldTrees)
        {
            try { tree.Dispose(); } catch { }
        }
        _oldTrees.Clear();
        base.Dispose();
    }
}
