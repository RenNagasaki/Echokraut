using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.UI.Nodes;
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

    // Pagination bar for Voices tab
    private PaginationBar? _vsPaginationBar;

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

    // Per-row delete state (single-delete confirmation, like ImGui's double-click pattern)
    private float _colDel = 60f; // measured at setup from localized text
    private float _colConfigure = 80f;
    private bool _vsDelAudioArmed;
    private bool _vsDelMappingArmed;
    private NpcMapData? _vsDelTarget;
    private TextButtonNode? _vsDelAudioButton;
    private TextButtonNode? _vsDelMappingButton;
    private DateTime _vsLastDelClick = DateTime.MinValue;
    private NpcMapData? _vsNpcToRemove; // deferred mapping removal (processed in UpdateVoiceSelection)

    // ScrollingListNode reserves 16px for the scrollbar (ScrollingAreaNode.OnSizeChanged)
    private const float ScrollbarWidth = 16f;

    // Column widths (ColPlay computed at setup from localized Play/Stop text)
    private float _colPlay = 40f;
    private const float ColUse    = 40f;
    private const float ColGender = 70f;
    private const float ColRace   = 90f;
    private const float ColName   = 140f;
    private const float ColVoice  = 180f;

    private float VsVolWidth => _contentWidth - ScrollbarWidth - _colPlay - ColUse - ColGender - ColRace - ColName - ColVoice - 2 * _colDel - 8 * 4;

    private void SetupVoiceSelection()
    {
        var w = _contentWidth;

        // Measure Play/Stop button width from localized text (auto-size to longest variant)
        var measureBtn = new TextButtonNode { Size = new Vector2(40, 24), String = Loc.S("Play") };
        var playW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Play")).X + 36;
        var stopW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Stop")).X + 36;
        _colPlay = Math.Max(40f, Math.Max(playW, stopW));
        measureBtn.Dispose();

        // Measure delete button width from localized text (audio/mapping labels + "OK?" confirmation)
        var delMeasure = new TextButtonNode { Size = new Vector2(60, 24), String = Loc.S("Delete audio") };
        var delAudioW = delMeasure.LabelNode.GetTextDrawSize(Loc.S("Delete audio")).X + 36;
        var delMapW = delMeasure.LabelNode.GetTextDrawSize(Loc.S("Delete mapping")).X + 36;
        var okW = delMeasure.LabelNode.GetTextDrawSize(Loc.S("OK?")).X + 36;
        _colDel = Math.Max(60f, Math.Max(okW, Math.Max(delAudioW, delMapW)));
        var configW = delMeasure.LabelNode.GetTextDrawSize(Loc.S("Configure")).X + 36;
        _colConfigure = Math.Max(80f, configW);
        delMeasure.Dispose();

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
        // Position for headers (below search bar, no inner tab bar)
        var headerY = _topContentPos.Y + 30;
        // Position for data list (below headers: header 20 + filter 28 + sep 4 + gap)
        var dataYWithFilter = headerY + 20 + 28 + 8;
        var paginationH = 28f;
        var dataH = _innerContentSize.Y - (dataYWithFilter - _innerContentPos.Y) - paginationH - 4;

        // Header/filter rows must match the ScrollingListNode content width (minus scrollbar)
        var hw = w - ScrollbarWidth;
        var headerVolWidth = hw - _colPlay - ColUse - ColGender - ColRace - ColName - ColVoice - 2 * _colDel - 8 * 4;

        // Create header rows, filter rows, separators, pagination bars for NPCs/Players/Bubbles (indices 0-2)
        for (var i = 0; i < 3; i++)
        {
            _vsHeaders[i] = new HorizontalListNode { Size = new Vector2(hw, 20), ItemSpacing = 4, Position = new Vector2(_innerContentPos.X, headerY) };
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Test"), _colPlay));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Use"), ColUse));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Gender"), ColGender));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Race"), ColRace));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Name"), ColName));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Voice"), ColVoice));
            _vsHeaders[i]!.AddNode(HeaderLabel(Loc.S("Volume"), headerVolWidth));
            _vsHeaders[i]!.AddNode(HeaderLabel("", _colDel));
            _vsHeaders[i]!.AddNode(HeaderLabel("", _colDel));

            _vsFilterRows[i] = new HorizontalListNode { Size = new Vector2(hw, 28), ItemSpacing = 4, Position = new Vector2(_innerContentPos.X, headerY + 20) };
            _vsFilterRows[i]!.AddNode(Spacer(_colPlay, 28));
            _vsFilterRows[i]!.AddNode(Spacer(ColUse, 28));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColGender, 20, "", v => { _vsFilterGender = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColRace, 20, "", v => { _vsFilterRace = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColName, 40, "", v => { _vsFilterName = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColVoice, 40, "", v => { _vsFilterVoice = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Spacer(headerVolWidth, 28));
            _vsFilterRows[i]!.AddNode(Spacer(_colDel, 28));
            _vsFilterRows[i]!.AddNode(Spacer(_colDel, 28));

            _vsHeaderSeps[i] = new HorizontalLineNode { Size = new Vector2(w, 4), Position = new Vector2(_innerContentPos.X, headerY + 20 + 28 + 2) };

            _vsDataLists[i] = Panel(new Vector2(_innerContentPos.X, dataYWithFilter), new Vector2(w, dataH));
        }

        // Voices data list — directly below search (no inner tab bar)
        var voicesDataH = _topContentSize.Y - (headerY - _topContentPos.Y) - paginationH - 4;
        _vsDataLists[3] = Panel(new Vector2(_topContentPos.X, headerY), new Vector2(w, voicesDataH));

        _vsPaginationBar = new PaginationBar(
            new Vector2(_topContentPos.X, headerY + voicesDataH + 4), w,
            page =>
            {
                _vsPage[3] = page;
                BuildVoicesPage();
            });
    }

    private void AddVoiceSelectionNodes()
    {
        AddNode(_vsUnifiedSearch!);
        for (var i = 0; i < 3; i++)
        {
            AddNode(_vsHeaders[i]!);
            AddNode(_vsFilterRows[i]!);
            AddNode(_vsHeaderSeps[i]!);
        }
        foreach (var dl in _vsDataLists)
            if (dl != null) AddNode(dl);
        if (_vsPaginationBar != null)
            foreach (var node in _vsPaginationBar.Nodes)
                AddNode(node);
    }

    private void ShowVoiceSelectionSection(bool visible)
    {
        SetVisible(_vsUnifiedSearch, visible);
        if (visible)
        {
            ShowVsPanel(3); // Always show Voices panel directly
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
        if (_vsPaginationBar != null)
            foreach (var node in _vsPaginationBar.Nodes)
                SetVisible(node, false);
    }

    private void ShowVsPanel(int index)
    {
        _activeVsTab = index;
        HideAllVsPanels();

        if (index == 3)
        {
            SetVisible(_vsDataLists[3], true);
            if (_vsPaginationBar != null)
                foreach (var node in _vsPaginationBar.Nodes)
                    SetVisible(node, true);
        }

        // Reset filters on tab switch
        _vsFilterName = "";
        _vsFilterGender = "";
        _vsFilterRace = "";
        _vsFilterVoice = "";

        // Only build on first view — subsequent switches reuse existing nodes
        if (index == 3 && !_vsVoicesBuilt)
            _vsVoicesNeedRebuild = true;
    }

    private void ResetVsDeleteState()
    {
        if (_vsDelAudioArmed && _vsDelAudioButton != null)
            _vsDelAudioButton.String = Loc.S("Delete audio");
        if (_vsDelMappingArmed && _vsDelMappingButton != null)
            _vsDelMappingButton.String = Loc.S("Delete mapping");
        _vsDelAudioArmed = false;
        _vsDelMappingArmed = false;
        _vsDelTarget = null;
        _vsDelAudioButton = null;
        _vsDelMappingButton = null;
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

        // Reset delete confirmations after 5 seconds
        if ((_vsDelAudioArmed || _vsDelMappingArmed) && _vsLastDelClick.AddSeconds(5) <= DateTime.Now)
            ResetVsDeleteState();

        // Process deferred mapping removal
        if (_vsNpcToRemove != null)
        {
            var npc = _vsNpcToRemove;
            _vsNpcToRemove = null;
            _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, npc.Name);
            _npcData.RemoveCharacter(npc);
            if (npc.Name.StartsWith("BB"))
                _npcData.MappedNpcs.Remove(npc);
            else if (npc.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
                _npcData.MappedPlayers.Remove(npc);
            else
                _npcData.MappedNpcs.Remove(npc);
            TriggerActiveRebuild();
        }

        // Update pagination bar
        _vsPaginationBar?.Update();

        // Start new builds
        if (_vsNpcNeedRebuild && _activeVsTab == 0)
        { _vsNpcNeedRebuild = false; _vsNpcBuilt = true; StartMappedBuild(_vsDataLists[0]!, _npcData.MappedNpcs, false, 0); }
        if (_vsPlayerNeedRebuild && _activeVsTab == 1)
        { _vsPlayerNeedRebuild = false; _vsPlayerBuilt = true; StartMappedBuild(_vsDataLists[1]!, _npcData.MappedPlayers, false, 1); }
        if (_vsBubbleNeedRebuild && _activeVsTab == 2)
        { _vsBubbleNeedRebuild = false; _vsBubbleBuilt = true; StartMappedBuild(_vsDataLists[2]!, _npcData.MappedNpcs, true, 2); }
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
            _vsProgressiveTab = -1;
            return;
        }

        // Start progressive build — rows added in ContinueVsPageBuild()
        _vsProgressiveTab = tabIndex;
        _vsProgressiveIndex = 0;
        _vsProgressiveIsVoices = false;
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
        var w = _contentWidth - ScrollbarWidth;

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
        var voices = _npcData.GetEchokrautVoices()
            .FindAll(f => f.IsSelectable(npc.Name, npc.Gender, npc.Race, npc.BodyType));
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
            _config.Save();
        };

        // Delete audio files button (always visible)
        var capturedNpcForDelAudio = npc;
        TextButtonNode? delAudioBtn = null;
        delAudioBtn = Button(Loc.S("Delete audio"), _colDel, () =>
        {
            if (_vsDelAudioArmed && _vsDelTarget == capturedNpcForDelAudio)
            {
                _vsDelAudioArmed = false;
                _vsDelTarget = null;
                _vsDelAudioButton = null;
                _audioFiles.RemoveSavedNpcFiles(_config.LocalSaveLocation, capturedNpcForDelAudio.Name);
                delAudioBtn!.String = Loc.S("Delete audio");
            }
            else
            {
                ResetVsDeleteState();
                _vsLastDelClick = DateTime.Now;
                _vsDelAudioArmed = true;
                _vsDelTarget = capturedNpcForDelAudio;
                _vsDelAudioButton = delAudioBtn;
                delAudioBtn!.String = Loc.S("OK?");
            }
        });

        // Delete mapping button
        var capturedNpcForDelMap = npc;
        TextButtonNode? delMapBtn = null;
        delMapBtn = Button(Loc.S("Delete mapping"), _colDel, () =>
        {
            if (_vsDelMappingArmed && _vsDelTarget == capturedNpcForDelMap)
            {
                _vsDelMappingArmed = false;
                _vsDelTarget = null;
                _vsDelMappingButton = null;
                _vsNpcToRemove = capturedNpcForDelMap;
            }
            else
            {
                ResetVsDeleteState();
                _vsLastDelClick = DateTime.Now;
                _vsDelMappingArmed = true;
                _vsDelTarget = capturedNpcForDelMap;
                _vsDelMappingButton = delMapBtn;
                delMapBtn!.String = Loc.S("OK?");
            }
        });

        row.AddNode(playBtn);
        row.AddNode(useCheck);
        row.AddNode(genderLabel);
        row.AddNode(raceLabel);
        row.AddNode(nameLabel);
        row.AddNode(voiceDropDown);
        row.AddNode(volSlider);
        row.AddNode(delAudioBtn);
        row.AddNode(delMapBtn);
        return row;
    }

    // ── Voices tab ───────────────────────────────────────────────────────────

    private NativeVoiceConfigWindow? _voiceConfigWindow;

    private void StartVoicesBuild()
    {
        IEnumerable<EchokrautVoice> voiceData = _npcData.GetEchokrautVoices();
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
        var hw = w - ScrollbarWidth;
        const float noteFixedW = 300f;
        var volW = hw - 30 - 200 - noteFixedW - _colConfigure - 4 * 4 - 16;
        var header = new HorizontalListNode { Size = new Vector2(hw, 20), ItemSpacing = 4 };
        header.AddNode(Label(Loc.S("En"), 30));
        header.AddNode(Label(Loc.S("Voice Name"), 200));
        header.AddNode(Label(Loc.S("Note"), noteFixedW));
        header.AddNode(Label(Loc.S("Volume"), volW));
        header.AddNode(Label("", _colConfigure));
        panel.AddNode(header);
        panel.AddNode(Separator(w));

        if (_vsFilteredVoices == null || _vsFilteredVoices.Count == 0)
        {
            panel.AddNode(Label(Loc.S("No voices configured."), w));
            panel.RecalculateLayout();
            _vsPaginationBar?.SetTotalItems(0, VsPageSize);
            _vsProgressiveTab = -1;
            return;
        }

        _vsPaginationBar?.SetTotalItems(_vsFilteredVoices.Count, VsPageSize);

        // Start progressive build — rows added in ContinueVoicesPageBuild()
        _vsProgressiveTab = 3;
        _vsProgressiveIndex = 0;
        _vsProgressiveIsVoices = true;
    }

    private void ContinueVoicesPageBuild()
    {
        var panel = _vsDataLists[3];
        if (panel == null || _vsFilteredVoices == null) { _vsProgressiveTab = -1; return; }

        var page = _vsPaginationBar?.CurrentPage ?? 0;
        var pageStart = page * VsPageSize;
        var pageEnd = Math.Min(pageStart + VsPageSize, _vsFilteredVoices.Count);
        var pageCount = pageEnd - pageStart;

        var start = _vsProgressiveIndex;
        var end = Math.Min(start + VsRowsPerFrame, pageCount);
        var w = _contentWidth;

        var rw = w - ScrollbarWidth;
        const float noteFixedW2 = 300f;
        var volWidth = rw - 30 - 200 - noteFixedW2 - _colConfigure - 4 * 4 - 16;

        for (var i = start; i < end; i++)
        {
            var voice = _vsFilteredVoices[pageStart + i];
            var row = new HorizontalListNode { Size = new Vector2(rw, 28), ItemSpacing = 4 };

            row.AddNode(Check("   ", 30, voice.IsEnabled, v =>
            { voice.IsEnabled = v; _config.Save(); }));
            row.AddNode(Label(voice.VoiceName, 200));
            row.AddNode(Input(Loc.S("Note"), noteFixedW2, 80, voice.Note, v =>
            { voice.Note = v; _config.Save(); }));

            var volSlider = new SliderNode
            {
                Size = new Vector2(volWidth, 20),
                Range = 0..200,
                DecimalPlaces = 2,
                Value = (int)(voice.Volume * 100),
            };
            volSlider.OnValueChanged = v => { voice.Volume = v / 100.0f; _config.Save(); };
            row.AddNode(volSlider);

            var capturedVoice = voice;
            row.AddNode(Button(Loc.S("Configure"), _colConfigure, () => OpenVoiceConfig(capturedVoice)));
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
