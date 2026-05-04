using System;
using System.Numerics;
using System.Threading;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Services;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Bulk data-pipeline window: quest dialog harvest, voice starter-set extraction, and (later)
/// import/export. Lives separately from <see cref="NativeConfigWindow"/> so Settings stays
/// focused on per-feature toggles. See <c>plans/game-data-tools-window.md</c>.
/// </summary>
public sealed unsafe class NativeGameDataToolsWindow : NativeAddon
{
    private readonly IDialogHarvestService _dialogHarvest;
    private readonly IVoiceSampleExtractorService _voiceExtract;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly Configuration _config;

    // ── Quest Dialog Harvest section ─────────────────────────────────────────
    private TextButtonNode? _harvestButton;
    private CancellationTokenSource? _harvestCts;
    private TextInputNode? _debugQuestIdInput;
    private TextButtonNode? _debugExportButton;
    private TextDropDownNode? _questTypeDropDown;
    private string[]? _questTypeLabels;
    // Maps dropdown index → questTypeFilter passed to RunAsync.
    //   index 0 (All)      → null  (everything)
    //   index 1..6         → 1..6  (specific QuestType)
    //   index 7 (Non-Quest)→ 0     (QuestType.None — DefaultTalk etc.)
    private int _selectedQuestTypeIndex;
    private int _pendingQuestTypeSelection = -1;

    // ── Voice Starter Set section ────────────────────────────────────────────
    private SliderNode? _samplesSlider;
    private TextNode? _samplesValueLabel;
    private TextButtonNode? _starterSetButton;
    private CancellationTokenSource? _starterCts;

    // ── Shared progress bar (harvest + starter set) ──────────────────────────
    // Mirrors the StatusProgressBar pattern from NativeVoiceClipManagerWindow but lives
    // separately: VCM's bar is now reserved for generation progress only, this one drives
    // harvest + voice-extractor progress and idle status.
    private StatusProgressBar? _progressBar;

    // ── Bottom row: shortcuts to other plugin windows ────────────────────────
    private DynamicIconButtonNode? _configButton;
    private DynamicIconButtonNode? _voiceClipManagerButton;
    private readonly Action _toggleConfig;
    private readonly Action _toggleVoiceClipManager;

    // Volatile snapshots written by event handlers (any thread) and drained in OnUpdate.
    private string _harvestLabel = string.Empty;
    private volatile int _harvestCurrent;
    private volatile int _harvestTotal = 1;
    private string _extractLabel = string.Empty;
    private volatile int _extractCurrent;
    private volatile int _extractTotal = 1;
    /// <summary>One-shot terminal status (Done/Cancelled/Failed) shown once the active run ends.</summary>
    private string _terminalStatus = string.Empty;
    private bool _terminalStatusDirty;
    private readonly object _statusLock = new();

    public NativeGameDataToolsWindow(
        IDialogHarvestService dialogHarvest,
        IVoiceSampleExtractorService voiceExtract,
        IClientState clientState,
        ILogService log,
        Configuration config,
        Action toggleConfig,
        Action toggleVoiceClipManager)
    {
        _dialogHarvest = dialogHarvest;
        _voiceExtract = voiceExtract;
        _clientState = clientState;
        _log = log;
        _config = config;
        _toggleConfig = toggleConfig;
        _toggleVoiceClipManager = toggleVoiceClipManager;

        _voiceExtract.ProgressChanged += OnExtractProgress;
        _dialogHarvest.ProgressChanged += OnHarvestLabel;
        _dialogHarvest.ProgressCountChanged += OnHarvestCount;
    }

    public override void Dispose()
    {
        try { _voiceExtract.ProgressChanged -= OnExtractProgress; } catch { }
        try { _dialogHarvest.ProgressChanged -= OnHarvestLabel; } catch { }
        try { _dialogHarvest.ProgressCountChanged -= OnHarvestCount; } catch { }
        _harvestCts?.Cancel();
        _harvestCts?.Dispose();
        _starterCts?.Cancel();
        _starterCts?.Dispose();
        base.Dispose();
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        var w = size.X;

        // Layout: progress bar pinned at the very top, action sections in a scrollable
        // middle area, shortcut buttons pinned at the bottom-left. The progress bar is
        // primary feedback, so it gets the most prominent slot.
        const float progressBarHeight = 28f;
        const float gap = 4f;
        const float bottomBtnSize = 28f;
        var progressBarY = pos.Y;
        var listY = pos.Y + progressBarHeight + gap;
        var bottomRowY = pos.Y + size.Y - bottomBtnSize;
        var listHeight = size.Y - progressBarHeight - gap - bottomBtnSize - gap;

        var list = new ScrollingListNode
        {
            Position = new Vector2(pos.X, listY),
            Size = new Vector2(size.X, listHeight),
            FitWidth = true,
            ItemSpacing = 4,
        };

        // ── Quest Dialog Harvest ─────────────────────────────────────────────
        var questDesc = WrappedLabel(
            Loc.S("Scan game data for NPC dialog text and persist linked entries to the database. " +
                  "Use this to populate or refresh your voice clip catalog after a game patch."),
            w);

        _harvestButton = Button(Loc.S("Start Harvest"), 160, OnHarvestClick);

        // Quest type filter — mirrors the dropdown in the VC Manager so the user can scope
        // the harvest to a single quest category (or non-quest dialog only).
        _questTypeLabels = new[]
        {
            Loc.S("All"), Loc.S("Main Scenario"), Loc.S("Side Quest"),
            Loc.S("Unlock / Class Quest"), Loc.S("Beast Tribe"),
            Loc.S("Repeatable"), Loc.S("Seasonal Event"), Loc.S("Non-Quest Dialog")
        };
        _questTypeDropDown = new TextDropDownNode
        {
            Size = new Vector2(180, 28),
            Options = [],
        };
        _questTypeDropDown.OptionListNode.Options = new System.Collections.Generic.List<string>(_questTypeLabels);
        _questTypeDropDown.OptionListNode.SelectedOption = _questTypeLabels[0];
        if (_questTypeDropDown.LabelNode.Node != null)
            _questTypeDropDown.LabelNode.String = _questTypeLabels[0];
        _questTypeDropDown.OnOptionSelected = selected => _pendingQuestTypeSelection = Array.IndexOf(_questTypeLabels, selected);

        var harvestRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        harvestRow.AddNode(_harvestButton);
        harvestRow.AddNode(_questTypeDropDown);

        CreateCollapsibleSection(list, Loc.S("Quest Dialog Harvest"), w, false,
            [questDesc, harvestRow]);

        list.AddNode(Separator(w));

        // ── Voice Starter Set ────────────────────────────────────────────────
        var starterDesc = WrappedLabel(
            Loc.S("Extract voice samples from the game's built-in voice acting and write them " +
                  "to FF14-Voices/ inside your local save location. AllTalk-compatible (22050 Hz). " +
                  "Always overwrites previous output."),
            w);

        const int defaultSamples = 1;
        _samplesSlider = new SliderNode
        {
            Size = new Vector2(w - 260, 22),
            Range = 1..5,
            Value = defaultSamples,
            DecimalPlaces = 0,
        };
        // Combined label that shows what the slider does + what's currently selected.
        // Updated in OnValueChanged so the count stays in sync with the slider thumb.
        _samplesValueLabel = Label(string.Format(Loc.S("Samples per NPC: {0} (1-5)"), defaultSamples), 240);
        _samplesSlider.OnValueChanged = v =>
        {
            if (_samplesValueLabel != null)
                _samplesValueLabel.String = string.Format(Loc.S("Samples per NPC: {0} (1-5)"), (int)v);
        };

        var sliderRow = new HorizontalListNode { Size = new Vector2(w, 24), ItemSpacing = 6 };
        sliderRow.AddNode(_samplesValueLabel);
        sliderRow.AddNode(_samplesSlider);

        _starterSetButton = Button(Loc.S("Build Starter Set"), 200, OnStarterSetClick);

        CreateCollapsibleSection(list, Loc.S("Voice Starter Set"), w, false,
            [starterDesc, sliderRow, _starterSetButton]);

        list.AddNode(Separator(w));

        // ── Import / Export (placeholder) ────────────────────────────────────
        var importDesc = WrappedLabel(
            Loc.S("Coming soon: backup / restore of database + configuration, and import/export " +
                  "of community voice sets."),
            w);
        CreateCollapsibleSection(list, Loc.S("Import / Export"), w, true, [importDesc]);

        // ── Quest Lua Debug (subgroup under Import / Export) ─────────────────
        // Developer / power-user tool — emits the disassembled Lua script + bytecode trace
        // for a single quest so its ACTOR→text mapping can be inspected. Lives below
        // Import/Export as a sibling collapsible section, collapsed by default.
        var luaDebugDesc = WrappedLabel(
            Loc.S("Export the disassembled Lua script and bytecode trace for a single quest. " +
                  "Used to investigate why a quest's NPC dialog isn't getting matched."),
            w);

        _debugQuestIdInput = Input("Quest ID", 120, 10, "65614", _ => { });
        _debugExportButton = Button(Loc.S("Export Quest Lua Debug"), 200, OnDebugExportClick);

        var luaDebugRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        luaDebugRow.AddNode(_debugQuestIdInput);
        luaDebugRow.AddNode(_debugExportButton);

        CreateCollapsibleSection(list, Loc.S("Quest Lua Debug"), w, true,
            [luaDebugDesc, luaDebugRow]);

        AddNode(list);

        // ── Progress bar (harvest + starter set) — pinned at the very top ────
        _progressBar = new StatusProgressBar
        {
            Position = new Vector2(pos.X, progressBarY),
            Size = new Vector2(size.X, progressBarHeight),
        };
        _progressBar.ActionText = Loc.S("Idle");
        _progressBar.SetProgress(0f, string.Empty);
        AddNode(_progressBar);

        // ── Bottom row: Config + Voice Clip Manager shortcuts ────────────────
        // Same DynamicIconButtonNode pattern as the VCM window so the visual language is
        // shared across plugin windows. ImageNode-routed events are mandatory in NativeAddon
        // contexts (only those fire reliably; ButtonClick is silent here).
        var normalTint = new Vector3(1f, 1f, 1f);
        var hoverTint = new Vector3(1.4f, 1.4f, 1.4f);

        _configButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = ButtonIcon.GearCog,
            Tooltip = Loc.S("Open configuration window"),
            OnClick = () => _toggleConfig(),
        };
        _configButton.ImageNode.MultiplyColor = normalTint;
        _configButton.ImageNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (_configButton == null) return;
            _configButton.ImageNode.MultiplyColor = hoverTint;
            _configButton.ShowTooltip();
        });
        _configButton.ImageNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (_configButton == null) return;
            _configButton.ImageNode.MultiplyColor = normalTint;
            _configButton.HideTooltip();
        });
        AddNode(_configButton);

        // Voice Clip Manager button — UV (112, 28) on Character.tex = ButtonIcon.MusicNote.
        _voiceClipManagerButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X + bottomBtnSize + gap, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = ButtonIcon.MusicNote,
            Tooltip = Loc.S("Open Voice Clip Manager"),
            OnClick = () => _toggleVoiceClipManager(),
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
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        _log.UpdateMainThreadLogs();

        // Process deferred quest-type dropdown selection (TextDropDownNode crashes if we read
        // it inside its own OnOptionSelected callback — defer to the next frame).
        if (_pendingQuestTypeSelection >= 0)
        {
            _selectedQuestTypeIndex = _pendingQuestTypeSelection;
            _pendingQuestTypeSelection = -1;
        }

        UpdateProgressBar();
        ClampToScreen(addon);
    }

    /// <summary>Translates the dropdown selection into the <c>questTypeFilter</c> argument
    /// of <see cref="IDialogHarvestService.RunAsync"/>.</summary>
    private int? GetQuestTypeFilter()
    {
        // 0 → All (no filter), 1..6 → specific QuestType, 7 → None / non-quest only
        var idx = _selectedQuestTypeIndex;
        if (idx <= 0) return null;        // All
        if (idx == 7) return 0;           // QuestType.None
        return idx;                       // 1..6
    }

    /// <summary>
    /// Drive the shared progress bar from the active run state. Harvest takes priority over
    /// extractor (they shouldn't run concurrently in practice but if both flags are set we
    /// favour harvest since its events are more granular). Idle state shows the last terminal
    /// status (Done/Cancelled/Failed) until the next run starts.
    /// </summary>
    private void UpdateProgressBar()
    {
        if (_progressBar == null) return;

        if (_dialogHarvest.IsRunning)
        {
            string label;
            lock (_statusLock) label = _harvestLabel;
            _progressBar.ActionText = label;
            var hc = _harvestCurrent;
            var ht = _harvestTotal;
            var hf = ht > 0 ? (float)hc / ht : 0f;
            _progressBar.SetProgress(hf, ht > 0 ? $"{hc}/{ht}" : string.Empty);
            return;
        }

        if (_voiceExtract.IsRunning)
        {
            string label;
            lock (_statusLock) label = _extractLabel;
            _progressBar.ActionText = label;
            var ec = _extractCurrent;
            var et = _extractTotal;
            var ef = et > 0 ? (float)ec / et : 0f;
            _progressBar.SetProgress(ef, et > 0 ? $"{ec}/{et}" : string.Empty);
            return;
        }

        // Idle: surface the most recent terminal status (or default Idle text) and freeze the
        // bar at its last value so the user sees what the last run accomplished.
        if (_terminalStatusDirty)
        {
            string text;
            lock (_statusLock)
            {
                text = _terminalStatus;
                _terminalStatusDirty = false;
            }
            _progressBar.ActionText = string.IsNullOrEmpty(text) ? Loc.S("Idle") : text;
        }
    }

    private static unsafe void ClampToScreen(AtkUnitBase* addon)
    {
        if (addon == null) return;
        var stage = FFXIVClientStructs.FFXIV.Component.GUI.AtkStage.Instance();
        if (stage == null) return;
        var screen = stage->ScreenSize;
        var w = addon->GetScaledWidth(true);
        var h = addon->GetScaledHeight(true);
        var minX = (short)(-w / 2);
        var maxX = (short)(screen.Width - w / 2);
        var minY = (short)(-h / 2);
        var maxY = (short)(screen.Height - h / 2);
        if (addon->X < minX) addon->X = minX;
        if (addon->X > maxX) addon->X = maxX;
        if (addon->Y < minY) addon->Y = minY;
        if (addon->Y > maxY) addon->Y = maxY;
    }

    // ── Quest Harvest handlers ───────────────────────────────────────────────

    private void OnHarvestClick()
    {
        if (_dialogHarvest.IsRunning)
        {
            _harvestCts?.Cancel();
            if (_harvestButton != null)
                _harvestButton.String = Loc.S("Start Harvest");
        }
        else
        {
            _harvestCts?.Dispose();
            _harvestCts = new CancellationTokenSource();
            if (_harvestButton != null)
                _harvestButton.String = Loc.S("Stop Harvest");
            // Reset bar so we don't start with stale extract counts.
            lock (_statusLock) _harvestLabel = Loc.S("Starting...");
            _harvestCurrent = 0;
            _harvestTotal = 1;
            var filter = GetQuestTypeFilter();
            _ = _dialogHarvest.RunAsync(_clientState.ClientLanguage, _harvestCts.Token, filter).ContinueWith(t =>
            {
                if (_harvestButton != null)
                    _harvestButton.String = Loc.S("Start Harvest");
                SetTerminalStatus(t.IsFaulted ? Loc.S("Failed — see logs")
                    : t.IsCanceled ? Loc.S("Cancelled")
                    : Loc.S("Done"));
            });
        }
    }

    private void OnDebugExportClick()
    {
        var questIdStr = _debugQuestIdInput?.String ?? string.Empty;
        if (uint.TryParse(questIdStr, out var qid))
            _dialogHarvest.ExportQuestLuaDebug(qid);
    }

    // ── Voice Starter Set handlers ───────────────────────────────────────────

    private void OnStarterSetClick()
    {
        if (_voiceExtract.IsRunning)
        {
            _starterCts?.Cancel();
            if (_starterSetButton != null)
                _starterSetButton.String = Loc.S("Build Starter Set");
            return;
        }

        var samples = _samplesSlider != null ? Math.Clamp((int)_samplesSlider.Value, 1, 5) : 1;
        _starterCts?.Dispose();
        _starterCts = new CancellationTokenSource();

        if (_starterSetButton != null)
            _starterSetButton.String = Loc.S("Stop");
        // Reset the bar at run start; live progress events take over from here.
        SetExtractProgress(Loc.S("Starting..."), 0, 0);

        // When AllTalk is configured as a Local Instance, route the output directly into
        // its voices folder (and let the extractor wipe it first — see VoiceSampleExtractor
        // for the wipe-on-override semantic). Same target as the First-Time install flow:
        // <LocalInstallPath>/alltalk_tts/voices/. Remote / no-instance setups fall back to
        // <LocalSaveLocation>/FF14-Voices/ which the user can copy manually.
        string? overrideRoot = null;
        string subfolder = "FF14-Voices";
        if (_config.Alltalk.InstanceType == AlltalkInstanceType.Local
            && !string.IsNullOrWhiteSpace(_config.Alltalk.LocalInstallPath))
        {
            overrideRoot = System.IO.Path.Combine(_config.Alltalk.LocalInstallPath, "alltalk_tts");
            subfolder = "voices";
        }

        _ = _voiceExtract.RunAsync(_clientState.ClientLanguage, samples, _starterCts.Token, overrideRoot, subfolder)
            .ContinueWith(t =>
        {
            if (_starterSetButton != null)
                _starterSetButton.String = Loc.S("Build Starter Set");
            SetTerminalStatus(t.IsFaulted ? Loc.S("Failed — see logs")
                : t.IsCanceled ? Loc.S("Cancelled")
                : Loc.S("Done"));
        });
    }

    private void OnExtractProgress(string label, int current, int total)
        => SetExtractProgress(label, current, total);

    private void SetExtractProgress(string label, int current, int total)
    {
        lock (_statusLock) _extractLabel = label;
        _extractCurrent = current;
        _extractTotal = total > 0 ? total : 1;
    }

    private void OnHarvestLabel(string label)
    {
        lock (_statusLock) _harvestLabel = label;
    }

    private void OnHarvestCount(int current, int total)
    {
        _harvestCurrent = current;
        _harvestTotal = total > 0 ? total : 1;
    }

    private void SetTerminalStatus(string text)
    {
        lock (_statusLock)
        {
            _terminalStatus = text;
            _terminalStatusDirty = true;
        }
    }

    // ── Local node factories ─────────────────────────────────────────────────

    private static TextNode Label(string text, float width) => new()
    {
        Size = new Vector2(width, 18),
        String = text,
        FontType = FontType.Axis,
        FontSize = 12,
    };

    /// <summary>Multi-line word-wrapped label sized for a row width.</summary>
    private static TextNode WrappedLabel(string text, float width)
    {
        var node = new TextNode
        {
            Size = new Vector2(width, 36),
            String = text,
            FontType = FontType.Axis,
            FontSize = 12,
        };
        node.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
        // Estimate height after wrapping; KamiToolKit doesn't auto-resize TextNode height.
        var measured = node.GetTextDrawSize(false).Y;
        if (measured > 0)
            node.Size = new Vector2(width, measured + 6);
        return node;
    }

    private static HorizontalLineNode Separator(float width) => new()
    {
        Size = new Vector2(width, 4),
    };

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
            Size = new Vector2(width, 28),
            MaxCharacters = maxChars,
            PlaceholderString = placeholder,
            String = initial,
        };
        node.OnInputReceived = s => onComplete(s.ToString());
        return node;
    }

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

        if (startCollapsed)
            foreach (var n in contentNodes)
                n.IsVisible = false;

        list.AddNode(toggle);
        foreach (var n in contentNodes)
            list.AddNode(n);
        return toggle;
    }
}
