using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
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

    // Progressive loading state
    private const int RowsPerFrame = 15;

    // Pending build queues — when non-null, rows are being added progressively
    private List<NpcMapData>? _vsPendingNpcs;
    private List<NpcMapData>? _vsPendingPlayers;
    private List<NpcMapData>? _vsPendingBubbles;
    private List<EchokrautVoice>? _vsPendingVoices;
    private int _vsPendingIndex;
    private bool _vsPendingIsBubble;

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

    // Column widths
    private const float ColPlay   = 40f;
    private const float ColLock   = 40f;
    private const float ColUse    = 40f;
    private const float ColGender = 70f;
    private const float ColRace   = 90f;
    private const float ColName   = 140f;
    private const float ColVoice  = 180f;

    private float VsVolWidth => _contentWidth - ColPlay - ColLock - ColUse - ColGender - ColRace - ColName - ColVoice - 7 * 4;

    private void SetupVoiceSelection()
    {
        var w = _contentWidth;

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
        var dataYNoFilter = headerY + 20 + 8;
        var dataH = _innerContentSize.Y - (dataYWithFilter - _innerContentPos.Y);

        // Create header rows, filter rows, separators for NPCs/Players/Bubbles (indices 0-2)
        for (var i = 0; i < 3; i++)
        {
            _vsHeaders[i] = new HorizontalListNode { Size = new Vector2(w, 20), ItemSpacing = 4, Position = new Vector2(_innerContentPos.X, headerY) };
            _vsHeaders[i]!.AddNode(Label(Loc.S("Test"), ColPlay));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Lock"), ColLock));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Use"), ColUse));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Gender"), ColGender));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Race"), ColRace));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Name"), ColName));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Voice"), ColVoice));
            _vsHeaders[i]!.AddNode(Label(Loc.S("Volume"), VsVolWidth));

            _vsFilterRows[i] = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 4, Position = new Vector2(_innerContentPos.X, headerY + 20) };
            _vsFilterRows[i]!.AddNode(Spacer(ColPlay, 28));
            _vsFilterRows[i]!.AddNode(Spacer(ColLock, 28));
            _vsFilterRows[i]!.AddNode(Spacer(ColUse, 28));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColGender, 20, "", v => { _vsFilterGender = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColRace, 20, "", v => { _vsFilterRace = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColName, 40, "", v => { _vsFilterName = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Input(Loc.S("Filter"), ColVoice, 40, "", v => { _vsFilterVoice = v; TriggerActiveRebuild(); }));
            _vsFilterRows[i]!.AddNode(Spacer(VsVolWidth, 28));

            _vsHeaderSeps[i] = new HorizontalLineNode { Size = new Vector2(w, 4), Position = new Vector2(_innerContentPos.X, headerY + 20 + 28 + 2) };

            _vsDataLists[i] = Panel(new Vector2(_innerContentPos.X, dataYWithFilter), new Vector2(w, dataH));
        }

        // Voices tab — uses area below search + toggle + tab bar
        _vsDataLists[3] = Panel(new Vector2(_innerContentPos.X, headerY), new Vector2(w, _innerContentSize.Y - (headerY - _innerContentPos.Y)));

        _vsTabBar.AddTab(Loc.S("NPCs"),    () => ShowVsPanel(0));
        _vsTabBar.AddTab(Loc.S("Players"), () => ShowVsPanel(1));
        _vsTabBar.AddTab(Loc.S("Bubbles"), () => ShowVsPanel(2));
        _vsTabBar.AddTab(Loc.S("Voices"),  () => ShowVsPanel(3));
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

        // Start new builds
        if (_vsNpcNeedRebuild && _activeVsTab == 0)
        { _vsNpcNeedRebuild = false; _vsNpcBuilt = true; StartMappedBuild(_vsDataLists[0]!, _config.MappedNpcs, false, 0); }
        if (_vsPlayerNeedRebuild && _activeVsTab == 1)
        { _vsPlayerNeedRebuild = false; _vsPlayerBuilt = true; StartMappedBuild(_vsDataLists[1]!, _config.MappedPlayers, false, 1); }
        if (_vsBubbleNeedRebuild && _activeVsTab == 2)
        { _vsBubbleNeedRebuild = false; _vsBubbleBuilt = true; StartMappedBuild(_vsDataLists[2]!, _config.MappedNpcs, true, 2); }
        if (_vsVoicesNeedRebuild && _activeVsTab == 3)
        { _vsVoicesNeedRebuild = false; _vsVoicesBuilt = true; StartVoicesBuild(); }

        // Continue progressive builds
        ContinueMappedBuild();
        ContinueVoicesBuild();
    }

    // ── Progressive mapped list build ────────────────────────────────────────

    private void StartMappedBuild(ScrollingListNode panel, List<NpcMapData> source, bool isBubble, int tabIndex)
    {
        panel.Clear();

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

        // Store pending queue
        switch (tabIndex)
        {
            case 0: _vsPendingNpcs = entries; break;
            case 1: _vsPendingPlayers = entries; break;
            case 2: _vsPendingBubbles = entries; break;
        }
        _vsPendingIsBubble = isBubble;
        _vsPendingIndex = 0;

        if (entries.Count == 0)
        {
            panel.AddNode(Label(Loc.S("No entries found."), _contentWidth));
            panel.RecalculateLayout();
            // Clear pending
            switch (tabIndex) { case 0: _vsPendingNpcs = null; break; case 1: _vsPendingPlayers = null; break; case 2: _vsPendingBubbles = null; break; }
        }
    }

    private void ContinueMappedBuild()
    {
        var pending = _activeVsTab switch
        {
            0 => _vsPendingNpcs,
            1 => _vsPendingPlayers,
            2 => _vsPendingBubbles,
            _ => null,
        };
        if (pending == null) return;

        var panel = _vsDataLists[_activeVsTab];
        if (panel == null) return;

        var w = _contentWidth;
        var end = Math.Min(_vsPendingIndex + RowsPerFrame, pending.Count);

        for (var i = _vsPendingIndex; i < end; i++)
            panel.AddNode(BuildMappedRow(pending[i], w, _vsPendingIsBubble));

        _vsPendingIndex = end;
        panel.RecalculateLayout();

        // Done?
        if (_vsPendingIndex >= pending.Count)
        {
            switch (_activeVsTab)
            {
                case 0: _vsPendingNpcs = null; break;
                case 1: _vsPendingPlayers = null; break;
                case 2: _vsPendingBubbles = null; break;
            }
        }
    }

    private HorizontalListNode BuildMappedRow(NpcMapData npc, float w, bool isBubble)
    {
        var row = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };

        // Play/Stop toggle button
        var capturedNpcForPlay = npc;
        TextButtonNode? playBtn = null;
        playBtn = Button(Loc.S("Play"), ColPlay, () =>
        {
            if (_audioPlayback.IsPlaying)
            {
                StopVoice();
                playBtn!.String = Loc.S("Play");
            }
            else if (capturedNpcForPlay.Voice != null)
            {
                TestVoice(capturedNpcForPlay.Voice);
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

        IEnumerable<EchokrautVoice> voiceData = _config.EchokrautVoices;
        if (!string.IsNullOrEmpty(_vsUnifiedFilter))
        {
            var search = _vsUnifiedFilter;
            voiceData = voiceData.Where(v =>
                v.VoiceName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.Note.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        _vsPendingVoices = voiceData.OrderBy(v => v.VoiceName).ToList();
        _vsPendingIndex = 0;

        if (_vsPendingVoices.Count == 0)
        {
            panel.AddNode(Label(Loc.S("No voices configured."), w));
            panel.RecalculateLayout();
            _vsPendingVoices = null;
        }
    }

    private void ContinueVoicesBuild()
    {
        if (_vsPendingVoices == null || _activeVsTab != 3) return;

        var panel = _vsDataLists[3];
        if (panel == null) return;

        var w = _contentWidth;
        var end = Math.Min(_vsPendingIndex + RowsPerFrame, _vsPendingVoices.Count);

        for (var i = _vsPendingIndex; i < end; i++)
        {
            var voice = _vsPendingVoices[i];
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

        _vsPendingIndex = end;
        panel.RecalculateLayout();

        if (_vsPendingIndex >= _vsPendingVoices.Count)
            _vsPendingVoices = null;
    }

    private void OpenVoiceConfig(EchokrautVoice voice)
    {
        // Close existing window if open
        _voiceConfigWindow?.Dispose();

        _voiceConfigWindow = new NativeVoiceConfigWindow(
            voice, _config, _npcData, _audioPlayback, _volumeService,
            _gameObjects, _clientState, _backend, _log,
            () => _vsVoicesNeedRebuild = true)
        {
            InternalName = "EKVoiceConfig",
            Title = $"Voice: {voice.VoiceName}",
            Size = new System.Numerics.Vector2(500, 700),
        };
        _voiceConfigWindow.Open();
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

    // ── Voice testing ────────────────────────────────────────────────────────

    private void TestVoice(EchokrautVoice voice)
    {
        StopVoice();
        var eventId = _log.Start(nameof(TestVoice), TextSource.AddonTalk);
        var volume = _volumeService.GetVoiceVolume(eventId) * voice.Volume;
        var speaker = new NpcMapData(ObjectKind.None)
        {
            Gender = voice.AllowedGenders.Count > 0 ? voice.AllowedGenders[0] : Genders.Male,
            Race = voice.AllowedRaces.Count > 0 ? voice.AllowedRaces[0] : NpcRaces.Hyur,
            Name = voice.VoiceName,
        };
        speaker.Voices = _config.EchokrautVoices;
        speaker.Voice = voice;

        var voiceMessage = new VoiceMessage
        {
            SpeakerObj = null,
            Source = TextSource.VoiceTest,
            Speaker = speaker,
            Text = GetTestText(),
            OriginalText = GetTestText(),
            Language = _clientState.ClientLanguage,
            EventId = eventId,
            SpeakerFollowObj = _gameObjects.LocalPlayer,
            Volume = volume
        };

        if (volume > 0)
            _backend.ProcessVoiceMessage(voiceMessage);
        else
            _log.End(nameof(TestVoice), eventId);
    }

    private void StopVoice()
    {
        if (DialogState.CurrentVoiceMessage != null)
            _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
        _log.End(nameof(StopVoice), new EKEventId(0, TextSource.AddonTalk));
    }

    private string GetTestText() => _clientState.ClientLanguage switch
    {
        ClientLanguage.German  => Constants.TESTMESSAGEDE,
        ClientLanguage.French  => Constants.TESTMESSAGEFR,
        ClientLanguage.Japanese => Constants.TESTMESSAGEJP,
        _ => Constants.TESTMESSAGEEN,
    };
}
