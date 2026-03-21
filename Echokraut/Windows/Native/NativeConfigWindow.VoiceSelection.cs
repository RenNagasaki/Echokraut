using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

public sealed unsafe partial class NativeConfigWindow
{
    // ── Voice Selection fields ───────────────────────────────────────────────
    private TextInputNode? _vsUnifiedSearch;
    private TextButtonNode? _vsAdvancedToggle;
    private bool _vsAdvancedFiltersVisible;
    private string _vsUnifiedFilter = "";
    private TabBarNode? _vsTabBar;

    // Static header area per tab (filter inputs + column headers) — never cleared
    private HorizontalListNode?[] _vsHeaders = new HorizontalListNode?[3]; // NPCs, Players, Bubbles
    private HorizontalListNode?[] _vsFilterRows = new HorizontalListNode?[3];
    private HorizontalLineNode?[] _vsHeaderSeps = new HorizontalLineNode?[3];

    // Data rows area per tab — cleared and rebuilt on filter/data change
    private ScrollingListNode?[] _vsDataLists = new ScrollingListNode?[4]; // 0-2 = mapped, 3 = voices

    // Filter state (shared across NPC/Player/Bubble tabs, reset on tab switch)
    private string _vsFilterName   = "";
    private string _vsFilterGender = "";
    private string _vsFilterRace   = "";
    private string _vsFilterVoice  = "";

    // Pagination state
    private const int VsPageSize = 100;
    private const int VsRowsPerFrame = 10;
    private readonly List<NpcMapData>?[] _vsFilteredData = new List<NpcMapData>?[3]; // cached filtered results per tab
    private List<EchokrautVoice>? _vsFilteredVoices;
    private readonly int[] _vsPage = new int[4]; // current page per tab (0-indexed)
    private readonly bool[] _vsIsBubble = { false, false, true, false };

    // Progressive loading within current page
    private int _vsProgressiveIndex; // how many rows of the current page have been added
    private int _vsProgressiveTab = -1; // which tab is being progressively built (-1 = idle)
    private bool _vsProgressiveIsVoices; // true when building voices tab

    // Deferred page change (set by button click, processed in UpdateVoiceSelection)
    private int _vsPendingPageDelta;
    private int _vsPendingPageTab = -1;

    // Pagination nodes (individually positioned per tab)
    private TextButtonNode?[] _vsPrevButtons = new TextButtonNode?[4];
    private TextButtonNode?[] _vsNextButtons = new TextButtonNode?[4];
    private TextNode?[] _vsPageLabels = new TextNode?[4];

    // Rebuild flags — only set by filter changes, NOT by tab switches
    private bool _vsNpcNeedRebuild;
    private bool _vsPlayerNeedRebuild;
    private bool _vsBubbleNeedRebuild;
    private bool _vsVoicesNeedRebuild;

    // Track whether each list has been built at least once
    private bool _vsNpcBuilt;
    private bool _vsPlayerBuilt;
    private bool _vsBubbleBuilt;
    private bool _vsVoicesBuilt;

    private int _activeVsTab;

    // Voice test playback tracking (button node only — state lives in IVoiceTestService)
    private TextButtonNode? _vsTestingButton;

    // ScrollingListNode reserves 16px for the scrollbar (ScrollingAreaNode.OnSizeChanged)
    private const float ScrollbarWidth = 16f;

    // Column widths (ColPlay computed at setup from localized Play/Stop text)
    private float _colPlay = 40f;
    private const float ColLock   = 40f;
    private const float ColUse    = 40f;
    private const float ColGender = 70f;
    private const float ColRace   = 90f;
    private const float ColName   = 140f;
    private const float ColVoice  = 180f;

    private float VsVolWidth => _contentWidth - _colPlay - ColLock - ColUse - ColGender - ColRace - ColName - ColVoice - 7 * 4;

    private void SetupVoiceSelection()
    {
        var w = _contentWidth;

        // Measure Play/Stop button width from localized text (auto-size to longest variant)
        var measureBtn = new TextButtonNode { Size = new Vector2(40, 24), String = Loc.S("Play") };
        var playW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Play")).X + 36;
        var stopW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Stop")).X + 36;
        _colPlay = Math.Max(40f, Math.Max(playW, stopW));
        measureBtn.Dispose();

        // Unified search bar above the tab bar
        _vsUnifiedSearch = Input(Loc.S("Search..."), w, 80, "", v =>
        {
            _vsUnifiedFilter = v;
            TriggerActiveRebuild();
        });
        _vsUnifiedSearch.Position = _topContentPos;

        // Advanced filters toggle
        _vsAdvancedToggle = Button(Loc.S("[+] Advanced Filters"), w, () =>
        {
            _vsAdvancedFiltersVisible = !_vsAdvancedFiltersVisible;
            _vsAdvancedToggle!.String = _vsAdvancedFiltersVisible ? Loc.S("[-] Advanced Filters") : Loc.S("[+] Advanced Filters");
            for (var j = 0; j < 3; j++)
                SetVisible(_vsFilterRows[j], _vsAdvancedFiltersVisible && j == _activeVsTab);
        });
        _vsAdvancedToggle.Position = _topContentPos + new Vector2(0, 30);

        _vsTabBar = new TabBarNode { Size = new Vector2(w, 32), Position = _topContentPos + new Vector2(0, 56) };

        // Position for headers (below tab bar + search bar + toggle)
        var headerY = _innerContentPos.Y + 56;
        // Position for data list (below headers: header 20 + filter 28 + sep 4 + gap)
        var dataYWithFilter = headerY + 20 + 28 + 8;
        var paginationH = 28f;
        var dataH = _innerContentSize.Y - (dataYWithFilter - _innerContentPos.Y) - paginationH - 4;

        // Header/filter rows must match the ScrollingListNode content width (minus scrollbar)
        var hw = w - ScrollbarWidth;
        var headerVolWidth = hw - _colPlay - ColLock - ColUse - ColGender - ColRace - ColName - ColVoice - 7 * 4;

        // Create header rows, filter rows, separators, pagination bars for NPCs/Players/Bubbles (indices 0-2)
        for (var i = 0; i < 3; i++)
        {
            _vsHeaders[i] = new HorizontalListNode { Size = new Vector2(hw, 20), ItemSpacing = 4, Position = new Vector2(_innerContentPos.X, headerY) };
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Test"), _colPlay));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Lock"), ColLock));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Use"), ColUse));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Gender"), ColGender));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Race"), ColRace));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Name"), ColName));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Voice"), ColVoice));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Volume"), headerVolWidth));

            _vsFilterRows[i] = new HorizontalListNode { Size = new Vector2(hw, 28), ItemSpacing = 4, Position = new Vector2(_innerContentPos.X, headerY + 20) };
            _vsFilterRows[i]!.AddNode(Spacer(_colPlay, 28));
            _vsFilterRows[i]!.AddNode(Spacer(ColLock, 28));
            _vsFilterRows[i]!.AddNode(Spacer(ColUse, 28));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColGender, 20, "", v => { _vsFilterGender = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColRace, 20, "", v => { _vsFilterRace = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColName, 40, "", v => { _vsFilterName = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColVoice, 40, "", v => { _vsFilterVoice = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Spacer(headerVolWidth, 28));

            _vsHeaderSeps[i] = new HorizontalLineNode { Size = new Vector2(w, 4), Position = new Vector2(_innerContentPos.X, headerY + 20 + 28 + 2) };

            _vsDataLists[i] = Panel(new Vector2(_innerContentPos.X, dataYWithFilter), new Vector2(w, dataH));

            CreateVsPaginationNodes(i, w, dataYWithFilter + dataH + 4);
        }

        // Voices tab — uses area below search + toggle + tab bar
        var voicesDataH = _innerContentSize.Y - (headerY - _innerContentPos.Y) - paginationH - 4;
        _vsDataLists[3] = Panel(new Vector2(_innerContentPos.X, headerY), new Vector2(w, voicesDataH));
        CreateVsPaginationNodes(3, w, headerY + voicesDataH + 4);

        _vsTabBar.AddTab(Loc.S("NPCs"),    () => ShowVsPanel(0));
        _vsTabBar.AddTab(Loc.S("Players"), () => ShowVsPanel(1));
        _vsTabBar.AddTab(Loc.S("Bubbles"), () => ShowVsPanel(2));
        _vsTabBar.AddTab(Loc.S("Voices"),  () => ShowVsPanel(3));
    }

    private void CreateVsPaginationNodes(int index, float w, float y)
    {
        var idx = index;
        const float btnW = 30f;
        const float labelW = 150f;

        _vsPrevButtons[index] = Button("<", btnW, () => VsChangePage(idx, -1));
        _vsNextButtons[index] = Button(">", btnW, () => VsChangePage(idx, 1));
        _vsPageLabels[index] = Label("", labelW);

        // Buttons centered in the window
        const float btnGap = 20f;
        var centerX = _innerContentPos.X + (w - btnW * 2 - btnGap) / 2f;
        _vsPrevButtons[index]!.Position = new Vector2(centerX, y);
        _vsNextButtons[index]!.Position = new Vector2(centerX + btnW + btnGap, y);

        // Label right-aligned
        _vsPageLabels[index]!.Position = new Vector2(_innerContentPos.X + w - labelW, y + 4);
    }

    private void AddVoiceSelectionNodes()
    {
        AddNode(_vsUnifiedSearch!);
        AddNode(_vsAdvancedToggle!);
        AddNode(_vsTabBar!);
        for (var i = 0; i < 3; i++)
        {
            AddNode(_vsHeaders[i]!);
            AddNode(_vsFilterRows[i]!);
            AddNode(_vsHeaderSeps[i]!);
        }
        foreach (var dl in _vsDataLists)
            if (dl != null) AddNode(dl);
        for (var i = 0; i < 4; i++)
        {
            if (_vsPrevButtons[i] != null) AddNode(_vsPrevButtons[i]!);
            if (_vsNextButtons[i] != null) AddNode(_vsNextButtons[i]!);
            if (_vsPageLabels[i] != null) AddNode(_vsPageLabels[i]!);
        }
    }

    private void ShowVoiceSelectionSection(bool visible)
    {
        SetVisible(_vsUnifiedSearch, visible);
        SetVisible(_vsAdvancedToggle, visible);
        SetVisible(_vsTabBar, visible);
        if (visible)
        {
            // Always restore the active sub-tab (SelectTab only highlights, doesn't fire callback)
            ShowVsPanel(_activeVsTab);
        }
        else
        {
            HideAllVsPanels();
        }
    }

    private void HideAllVsPanels()
    {
        for (var i = 0; i < 3; i++)
        {
            SetVisible(_vsHeaders[i], false);
            SetVisible(_vsFilterRows[i], false);
            SetVisible(_vsHeaderSeps[i], false);
        }
        foreach (var dl in _vsDataLists)
            SetVisible(dl, false);
        for (var i = 0; i < 4; i++)
        {
            SetVisible(_vsPrevButtons[i], false);
            SetVisible(_vsNextButtons[i], false);
            SetVisible(_vsPageLabels[i], false);
        }
    }

    private void ShowVsPanel(int index)
    {
        _activeVsTab = index;
        HideAllVsPanels();

        if (index < 3)
        {
            SetVisible(_vsHeaders[index], true);
            SetVisible(_vsFilterRows[index], _vsAdvancedFiltersVisible);
            SetVisible(_vsHeaderSeps[index], true);
            SetVisible(_vsDataLists[index], true);
        }
        else
        {
            SetVisible(_vsDataLists[3], true);
        }

        SetVisible(_vsPrevButtons[index], true);
        SetVisible(_vsNextButtons[index], true);
        SetVisible(_vsPageLabels[index], true);

        // Reset filters on tab switch
        _vsFilterName = "";
        _vsFilterGender = "";
        _vsFilterRace = "";
        _vsFilterVoice = "";

        // Only build on first view — subsequent switches reuse existing nodes
        switch (index)
        {
            case 0: if (!_vsNpcBuilt) _vsNpcNeedRebuild = true; break;
            case 1: if (!_vsPlayerBuilt) _vsPlayerNeedRebuild = true; break;
            case 2: if (!_vsBubbleBuilt) _vsBubbleNeedRebuild = true; break;
            case 3: if (!_vsVoicesBuilt) _vsVoicesNeedRebuild = true; break;
        }
    }

    private void UpdateVoiceSelection()
    {
        if (_activeTopTab != 1) return;

        // Reset play button when playback finishes naturally
        if (_vsTestingButton != null && _voiceTest.TestingVoice == null)
        {
            _vsTestingButton.String = Loc.S("Play");
            _vsTestingButton = null;
        }

        // Process deferred page changes (queued by button click handlers)
        ProcessVsPageChange();

        // Start new builds
        if (_vsNpcNeedRebuild && _activeVsTab == 0)
        { _vsNpcNeedRebuild = false; _vsNpcBuilt = true; StartMappedBuild(_vsDataLists[0]!, _config.MappedNpcs, false, 0); }
        if (_vsPlayerNeedRebuild && _activeVsTab == 1)
        { _vsPlayerNeedRebuild = false; _vsPlayerBuilt = true; StartMappedBuild(_vsDataLists[1]!, _config.MappedPlayers, false, 1); }
        if (_vsBubbleNeedRebuild && _activeVsTab == 2)
        { _vsBubbleNeedRebuild = false; _vsBubbleBuilt = true; StartMappedBuild(_vsDataLists[2]!, _config.MappedNpcs, true, 2); }
        if (_vsVoicesNeedRebuild && _activeVsTab == 3)
        { _vsVoicesNeedRebuild = false; _vsVoicesBuilt = true; StartVoicesBuild(); }

        // Continue progressive page builds
        ContinueVsPageBuild();
    }

    // ── Paginated mapped list build ──────────────────────────────────────────

    private void StartMappedBuild(ScrollingListNode panel, List<NpcMapData> source, bool isBubble, int tabIndex)
    {
        IEnumerable<NpcMapData> data = isBubble
            ? source.Where(n => n.HasBubbles)
            : source.Where(n => !n.Name.StartsWith("BB-"));

        // Unified search filter
        if (!string.IsNullOrEmpty(_vsUnifiedFilter))
        {
            var search = _vsUnifiedFilter;
            data = data.Where(n =>
                n.Gender.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                n.Race.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (n.Voice != null && n.Voice.VoiceName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        // Per-column filters (advanced)
        if (!string.IsNullOrEmpty(_vsFilterName))
            data = data.Where(n => n.Name.Contains(_vsFilterName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_vsFilterGender))
            data = data.Where(n => n.Gender.ToString().Contains(_vsFilterGender, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_vsFilterRace))
            data = data.Where(n => n.Race.ToString().Contains(_vsFilterRace, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_vsFilterVoice))
            data = data.Where(n => n.Voice != null && n.Voice.VoiceName.Contains(_vsFilterVoice, StringComparison.OrdinalIgnoreCase));

        var entries = data
            .OrderBy(n => n.Gender.ToString())
            .ThenBy(n => n.Race.ToString())
            .ThenBy(n => n.Name)
            .ToList();

        _vsFilteredData[tabIndex] = entries;
        _vsPage[tabIndex] = 0;

        BuildMappedPage(panel, tabIndex);
    }

    private void BuildMappedPage(ScrollingListNode panel, int tabIndex)
    {
        panel.Clear();

        var entries = _vsFilteredData[tabIndex];
        if (entries == null || entries.Count == 0)
        {
            panel.AddNode(Label(Loc.S("No entries found."), _contentWidth));
            panel.RecalculateLayout();
            UpdateVsPaginationLabel(tabIndex, 0);
            _vsProgressiveTab = -1;
            return;
        }

        // Start progressive build — rows added in ContinueVsPageBuild()
        _vsProgressiveTab = tabIndex;
        _vsProgressiveIndex = 0;
        _vsProgressiveIsVoices = false;
        UpdateVsPaginationLabel(tabIndex, entries.Count);
    }

    private void ContinueVsPageBuild()
    {
        if (_vsProgressiveTab < 0) return;

        if (_vsProgressiveIsVoices)
        {
            ContinueVoicesPageBuild();
            return;
        }

        var tabIndex = _vsProgressiveTab;
        var panel = _vsDataLists[tabIndex];
        var entries = _vsFilteredData[tabIndex];
        if (panel == null || entries == null) { _vsProgressiveTab = -1; return; }

        var page = _vsPage[tabIndex];
        var pageStart = page * VsPageSize;
        var pageEnd = Math.Min(pageStart + VsPageSize, entries.Count);
        var pageCount = pageEnd - pageStart;

        var start = _vsProgressiveIndex;
        var end = Math.Min(start + VsRowsPerFrame, pageCount);
        var isBubble = _vsIsBubble[tabIndex];
        var w = _contentWidth;

        for (var i = start; i < end; i++)
            panel.AddNode(BuildMappedRow(entries[pageStart + i], w, isBubble));

        _vsProgressiveIndex = end;
        panel.RecalculateLayout();

        if (_vsProgressiveIndex >= pageCount)
            _vsProgressiveTab = -1;
    }

    private HorizontalListNode BuildMappedRow(NpcMapData npc, float w, bool isBubble)
    {
        var row = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };

        // Play/Stop toggle button — sized to _colPlay (measured from longest localized variant)
        var capturedNpcForPlay = npc;
        TextButtonNode? playBtn = null;
        playBtn = Button(Loc.S("Play"), _colPlay, () =>
        {
            if (capturedNpcForPlay.Voice != null && _voiceTest.IsTestingVoice(capturedNpcForPlay.Voice))
            {
                ResetTestingButton();
                _voiceTest.StopVoice();
            }
            else if (capturedNpcForPlay.Voice != null)
            {
                ResetTestingButton();
                _voiceTest.TestVoice(capturedNpcForPlay.Voice);
                _vsTestingButton = playBtn;
                playBtn!.String = Loc.S("Stop");
            }
        });

        var lockCheck = Check("   ", ColLock, npc.DoNotDelete, v =>
        { npc.DoNotDelete = v; _config.Save(); });

        var isEnabled = isBubble ? npc.IsEnabledBubble : npc.IsEnabled;
        var useCheck = Check("   ", ColUse, isEnabled, v =>
        {
            if (isBubble) npc.IsEnabledBubble = v;
            else npc.IsEnabled = v;
            _config.Save();
        });

        var genderLabel = Label(npc.Gender.ToString(), ColGender);
        var raceLabel   = Label(npc.Race.ToString(), ColRace);
        var nameLabel   = Label(npc.Name, ColName);

        // Voice dropdown
        var voices = _config.EchokrautVoices
            .FindAll(f => f.IsSelectable(npc.Name, npc.Gender, npc.Race, npc.IsChild));
        var voiceNames = voices.ConvertAll(v => v.VoiceName);
        var currentVoice = npc.Voice?.VoiceName ?? "";

        var voiceDropDown = new TextDropDownNode { Size = new Vector2(ColVoice, 24), Options = [] };
        voiceDropDown.OptionListNode.Options = voiceNames;
        voiceDropDown.OptionListNode.SelectedOption = currentVoice;
        if (voiceDropDown.LabelNode.Node != null)
            voiceDropDown.LabelNode.String = currentVoice;

        var capturedNpc = npc;
        voiceDropDown.OnOptionSelected = option =>
        {
            var newVoice = voices.Find(v => v.VoiceName == option);
            if (newVoice != null)
            {
                capturedNpc.Voice = newVoice;
                capturedNpc.DoNotDelete = true;
                capturedNpc.RefreshSelectable();
                _config.Save();
            }
        };

        // Volume slider (0..2)
        var vol = isBubble ? npc.VolumeBubble : npc.Volume;
        var volSlider = new SliderNode
        {
            Size = new Vector2(VsVolWidth, 20),
            Range = 0..200,
            DecimalPlaces = 2,
            Value = (int)(vol * 100),
        };
        volSlider.OnValueChanged = v =>
        {
            if (isBubble) capturedNpc.VolumeBubble = v / 100.0f;
            else capturedNpc.Volume = v / 100.0f;
            capturedNpc.DoNotDelete = true;
            _config.Save();
        };

        row.AddNode(playBtn);
        row.AddNode(lockCheck);
        row.AddNode(useCheck);
        row.AddNode(genderLabel);
        row.AddNode(raceLabel);
        row.AddNode(nameLabel);
        row.AddNode(voiceDropDown);
        row.AddNode(volSlider);
        return row;
    }

    // ── Voices tab ───────────────────────────────────────────────────────────

    private NativeVoiceConfigWindow? _voiceConfigWindow;

    private void StartVoicesBuild()
    {
        IEnumerable<EchokrautVoice> voiceData = _config.EchokrautVoices;
        if (!string.IsNullOrEmpty(_vsUnifiedFilter))
        {
            var search = _vsUnifiedFilter;
            voiceData = voiceData.Where(v =>
                v.VoiceName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Note.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        _vsFilteredVoices = voiceData.OrderBy(v => v.VoiceName).ToList();
        _vsPage[3] = 0;

        BuildVoicesPage();
    }

    private void BuildVoicesPage()
    {
        var panel = _vsDataLists[3];
        if (panel == null) return;

        var w = _contentWidth;
        panel.Clear();

        // Header
        var header = new HorizontalListNode { Size = new Vector2(w, 20), ItemSpacing = 4 };
        header.AddNode(Label(Loc.S("En"), 30));
        header.AddNode(Label(Loc.S("Voice Name"), 200));
        header.AddNode(Label(Loc.S("Note"), 200));
        header.AddNode(Label(Loc.S("Volume"), 150));
        header.AddNode(Label("", 80));
        panel.AddNode(header);
        panel.AddNode(Separator(w));

        if (_vsFilteredVoices == null || _vsFilteredVoices.Count == 0)
        {
            panel.AddNode(Label(Loc.S("No voices configured."), w));
            panel.RecalculateLayout();
            UpdateVsPaginationLabel(3, 0);
            _vsProgressiveTab = -1;
            return;
        }

        // Start progressive build — rows added in ContinueVoicesPageBuild()
        _vsProgressiveTab = 3;
        _vsProgressiveIndex = 0;
        _vsProgressiveIsVoices = true;
        UpdateVsPaginationLabel(3, _vsFilteredVoices.Count);
    }

    private void ContinueVoicesPageBuild()
    {
        var panel = _vsDataLists[3];
        if (panel == null || _vsFilteredVoices == null) { _vsProgressiveTab = -1; return; }

        var page = _vsPage[3];
        var pageStart = page * VsPageSize;
        var pageEnd = Math.Min(pageStart + VsPageSize, _vsFilteredVoices.Count);
        var pageCount = pageEnd - pageStart;

        var start = _vsProgressiveIndex;
        var end = Math.Min(start + VsRowsPerFrame, pageCount);
        var w = _contentWidth;

        for (var i = start; i < end; i++)
        {
            var voice = _vsFilteredVoices[pageStart + i];
            var row = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 4 };

            row.AddNode(Check("   ", 30, voice.IsEnabled, v =>
            { voice.IsEnabled = v; _config.Save(); }));
            row.AddNode(Label(voice.VoiceName, 200));
            row.AddNode(Input(Loc.S("Note"), 200, 80, voice.Note, v =>
            { voice.Note = v; _config.Save(); }));

            var volSlider = new SliderNode
            {
                Size = new Vector2(150, 20),
                Range = 0..200,
                DecimalPlaces = 2,
                Value = (int)(voice.Volume * 100),
            };
            volSlider.OnValueChanged = v => { voice.Volume = v / 100.0f; _config.Save(); };
            row.AddNode(volSlider);

            var capturedVoice = voice;
            row.AddNode(Button(Loc.S("Configure"), 80, () => OpenVoiceConfig(capturedVoice)));
            panel.AddNode(row);
        }

        _vsProgressiveIndex = end;
        panel.RecalculateLayout();

        if (_vsProgressiveIndex >= pageCount)
            _vsProgressiveTab = -1;
    }

    private void OpenVoiceConfig(EchokrautVoice voice)
    {
        // Close existing window if open
        _voiceConfigWindow?.Dispose();

        _voiceConfigWindow = new NativeVoiceConfigWindow(
            voice, _config, _npcData, _voiceTest, _log,
            () => _vsVoicesNeedRebuild = true)
        {
            InternalName = "EKVoiceConfig",
            Title = $"Voice: {voice.VoiceName}",
            Size = new System.Numerics.Vector2(500, 700),
        };
        _voiceConfigWindow.Open();
    }

    // ── Pagination ───────────────────────────────────────────────────────────

    private void VsChangePage(int tabIndex, int delta)
    {
        // Defer to UpdateVoiceSelection — never clear/rebuild nodes inside ATK event handlers.
        _vsPendingPageTab = tabIndex;
        _vsPendingPageDelta = delta;
    }

    private void ProcessVsPageChange()
    {
        if (_vsPendingPageTab < 0) return;

        var tabIndex = _vsPendingPageTab;
        var delta = _vsPendingPageDelta;
        _vsPendingPageTab = -1;

        var total = tabIndex == 3
            ? (_vsFilteredVoices?.Count ?? 0)
            : (_vsFilteredData[tabIndex]?.Count ?? 0);
        var maxPage = Math.Max(0, (total - 1) / VsPageSize);
        var newPage = Math.Clamp(_vsPage[tabIndex] + delta, 0, maxPage);
        if (newPage == _vsPage[tabIndex]) return;

        _vsPage[tabIndex] = newPage;

        if (tabIndex == 3)
            BuildVoicesPage();
        else if (_vsDataLists[tabIndex] != null)
            BuildMappedPage(_vsDataLists[tabIndex]!, tabIndex);
    }

    private void UpdateVsPaginationLabel(int tabIndex, int total)
    {
        if (_vsPageLabels[tabIndex] == null) return;

        var maxPage = Math.Max(0, (total - 1) / VsPageSize);
        Dim(_vsPrevButtons[tabIndex], _vsPage[tabIndex] > 0);
        Dim(_vsNextButtons[tabIndex], _vsPage[tabIndex] < maxPage);

        if (total == 0)
        {
            _vsPageLabels[tabIndex]!.String = $"0 / 0";
        }
        else
        {
            var start = _vsPage[tabIndex] * VsPageSize + 1;
            var end = Math.Min(start + VsPageSize - 1, total);
            _vsPageLabels[tabIndex]!.String = $"{start}-{end} / {total}";
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void TriggerActiveRebuild()
    {
        switch (_activeVsTab)
        {
            case 0: _vsNpcNeedRebuild = true; break;
            case 1: _vsPlayerNeedRebuild = true; break;
            case 2: _vsBubbleNeedRebuild = true; break;
            case 3: _vsVoicesNeedRebuild = true; break;
        }
    }

    private void ResetTestingButton()
    {
        if (_vsTestingButton != null)
        {
            _vsTestingButton.String = Loc.S("Play");
            _vsTestingButton = null;
        }
    }
}
