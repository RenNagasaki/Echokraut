using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Services;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Enums;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

using static Echokraut.Windows.Native.NativeNodeFactory;
namespace Echokraut.Windows.Native;

/// <summary>
/// Bulk data-pipeline window: quest dialog harvest, voice pack download, and (later)
/// import/export. Lives separately from <see cref="NativeConfigWindow"/> so Settings stays
/// focused on per-feature toggles. See <c>plans/game-data-tools-window.md</c>.
/// </summary>
public sealed unsafe class NativeGameDataToolsWindow : NativeAddon
{
    // Terminal status texts shared by every section's completion handler. They are also the
    // Loc keys — see Localization/Loc.cs.
    private const string StatusCancelled = "Cancelled";
    private const string StatusFailed = "Failed — see logs";
    private const string StatusDone = "Done";

    private readonly IDialogHarvestService _dialogHarvest;
    private readonly IVoicePackService _voicePack;
    private readonly IAudioPackageService _audioPackage;
    private readonly INpcAttributionRepairService _repair;
    private readonly IDatabaseService _db;
    private readonly IGameObjectService _gameObjects;
    private readonly IClientState _clientState;
    private readonly ILogService _log;
    private readonly Configuration _config;

    // ── Quest Dialog Harvest section ─────────────────────────────────────────
    private TextButtonNode? _harvestButton;
    private CancellationTokenSource? _harvestCts;
    private TextInputNode? _debugQuestIdInput;
    private TextButtonNode? _debugExportButton;
    private StringDropDownNode? _questTypeDropDown;
    private string[]? _questTypeLabels;
    // Maps dropdown index → questTypeFilter passed to RunAsync.
    //   index 0 (All)      → null  (everything)
    //   index 1..6         → 1..6  (specific QuestType)
    //   index 7 (Non-Quest)→ 0     (QuestType.None — DefaultTalk etc.)
    private int _selectedQuestTypeIndex;
    private int _pendingQuestTypeSelection = -1;

    // ── Voice Pack section ───────────────────────────────────────────────────
    private TextButtonNode? _voicePackButton;
    private CancellationTokenSource? _voicePackCts;

    // ── Shared progress bar (harvest + voice pack) ───────────────────────────
    // Mirrors the StatusProgressBar pattern from NativeVoiceClipManagerWindow but lives
    // separately: VCM's bar is now reserved for generation progress only, this one drives
    // harvest + voice-pack progress and idle status.
    private StatusProgressBar? _progressBar;

    // ── NPC Attribution Repair section ───────────────────────────────────────
    // Walks character_instances + Lumina to detect mis-attributed instances (e.g. Alisaie's
    // dialog landing under "???" or another NPC because the live alias resolver picked the
    // wrong candidate before the BaseId-first fix). Dry-run renders a report; apply executes
    // the moves and merges.
    private TextButtonNode? _repairDryRunButton;
    private TextButtonNode? _repairApplyButton;
    private TextNode? _repairOutput;
    private NpcAttributionRepairReport? _lastRepairReport;
    private CancellationTokenSource? _repairCts;
    private volatile bool _repairBusy;
    private string _repairOutputText = string.Empty;
    private volatile bool _repairOutputDirty;

    // ── Player Name Cleanup section ──────────────────────────────────────────
    // Re-tokenises voice_clips rows that were written with the literal player name (live path
    // before the tokenisation fix) and merges them into their harvested twin. Shares the repair
    // section's busy flag / output plumbing shape but keeps its own state so both can be read
    // side by side.
    private TextButtonNode? _retokenizeDryRunButton;
    private TextButtonNode? _retokenizeApplyButton;
    private TextNode? _retokenizeOutput;
    private CancellationTokenSource? _retokenizeCts;
    private volatile bool _retokenizeBusy;
    private volatile bool _retokenizePreviewHasWork;
    private string _retokenizeOutputText = string.Empty;
    private volatile bool _retokenizeOutputDirty;

    // ── Import / Export section (audio packages, *.ekpack) ───────────────────
    // Export writes every generated WAV plus a manifest into one zip; import reads such a
    // package back, creating the characters / dialog lines it describes. Shares the window's
    // progress bar and the busy/output plumbing shape of the two cleanup sections.
    private TextInputNode? _packagePathInput;
    private string[]? _packageLanguageLabels;
    private int _selectedPackageLanguageIndex;
    private int _pendingPackageLanguageSelection = -1;
    private TextButtonNode? _packageExportButton;
    private TextButtonNode? _packageImportButton;
    private TextNode? _packageOutput;
    private CancellationTokenSource? _packageCts;
    private string _packageOutputText = string.Empty;
    private volatile bool _packageOutputDirty;
    private string _packageLabel = string.Empty;
    private volatile int _packageCurrent;
    private volatile int _packageTotal = 1;

    // ── Bottom row: shortcuts to other plugin windows ────────────────────────
    private DynamicIconButtonNode? _configButton;
    private DynamicIconButtonNode? _voiceClipManagerButton;
    private readonly Action _toggleConfig;
    private readonly Action _toggleVoiceClipManager;

    // Volatile snapshots written by event handlers (any thread) and drained in OnUpdate.
    private string _harvestLabel = string.Empty;
    private volatile int _harvestCurrent;
    private volatile int _harvestTotal = 1;
    private string _voicePackLabel = string.Empty;
    private volatile int _voicePackCurrent;
    private volatile int _voicePackTotal = 1;
    /// <summary>One-shot terminal status (Done/Cancelled/Failed) shown once the active run ends.</summary>
    private string _terminalStatus = string.Empty;
    private bool _terminalStatusDirty;
    private readonly object _statusLock = new();

    public NativeGameDataToolsWindow(
        IDialogHarvestService dialogHarvest,
        IVoicePackService voicePack,
        IAudioPackageService audioPackage,
        INpcAttributionRepairService repair,
        IDatabaseService db,
        IGameObjectService gameObjects,
        IClientState clientState,
        ILogService log,
        Configuration config,
        Action toggleConfig,
        Action toggleVoiceClipManager)
    {
        _dialogHarvest = dialogHarvest;
        _voicePack = voicePack;
        _audioPackage = audioPackage;
        _repair = repair;
        _db = db;
        _gameObjects = gameObjects;
        _clientState = clientState;
        _log = log;
        _config = config;
        _toggleConfig = toggleConfig;
        _toggleVoiceClipManager = toggleVoiceClipManager;

        _voicePack.ProgressChanged += OnVoicePackProgress;
        _audioPackage.ProgressChanged += OnPackageProgress;
        _dialogHarvest.ProgressChanged += OnHarvestLabel;
        _dialogHarvest.ProgressCountChanged += OnHarvestCount;
    }

    public override void Dispose()
    {
        try { _voicePack.ProgressChanged -= OnVoicePackProgress; } catch { }
        // Unsubscribing a handler that is already gone must never block teardown.
        try { _audioPackage.ProgressChanged -= OnPackageProgress; } catch (Exception) { /* ignored */ }
        try { _dialogHarvest.ProgressChanged -= OnHarvestLabel; } catch { }
        try { _dialogHarvest.ProgressCountChanged -= OnHarvestCount; } catch { }
        _harvestCts?.Cancel();
        _harvestCts?.Dispose();
        _voicePackCts?.Cancel();
        _voicePackCts?.Dispose();
        _repairCts?.Cancel();
        _repairCts?.Dispose();
        _retokenizeCts?.Cancel();
        _retokenizeCts?.Dispose();
        _packageCts?.Cancel();
        _packageCts?.Dispose();
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
        _questTypeDropDown = new StringDropDownNode
        {
            Size = new Vector2(180, 28),
            MaxListOptions = 8,
            Options = new System.Collections.Generic.List<string>(_questTypeLabels),
        };
        _questTypeDropDown.SelectedOption = _questTypeLabels[0];
        _questTypeDropDown.LabelNode.String = _questTypeLabels[0];
        _questTypeDropDown.OnOptionSelected = selected => _pendingQuestTypeSelection = Array.IndexOf(_questTypeLabels, selected);

        var harvestRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        harvestRow.AddNode(_harvestButton);
        harvestRow.AddNode(_questTypeDropDown);

        CreateCollapsibleSection(list, Loc.S("Quest Dialog Harvest"), w, false,
            [questDesc, harvestRow]);

        list.AddNode(Separator(w));

        // ── Voice Pack ───────────────────────────────────────────────────────
        var voicePackDesc = WrappedLabel(
            Loc.S("Download a curated set of freely-licensed reference voices (male, female and " +
                  "child) from the internet and install them into your TTS engine's voice folder. " +
                  "Every voice comes from a source whose license permits redistribution; the pack " +
                  "ships the license and attribution files alongside the samples."),
            w);

        _voicePackButton = Button(Loc.S("Download Voice Pack"), 220, OnVoicePackClick);

        CreateCollapsibleSection(list, Loc.S("Voice Pack"), w, false,
            [voicePackDesc, _voicePackButton]);

        list.AddNode(Separator(w));

        // ── NPC Attribution Repair ───────────────────────────────────────────
        var repairDesc = WrappedLabel(
            Loc.S("Walk the database and fix NPC dialogues that ended up under the wrong character " +
                  "(e.g. anonymous \"???\" speakers attributed to the first match instead of the real NPC). " +
                  "Dry-Run shows what would change; Apply moves the dialogues to the correct character " +
                  "and deletes empty character rows. No audio files are deleted."),
            w);

        _repairDryRunButton = Button(Loc.S("Dry Run"), 140, OnRepairDryRunClick);
        _repairApplyButton = Button(Loc.S("Apply Repair"), 160, OnRepairApplyClick);
        // Apply stays disabled until a dry-run produced a non-empty report.
        _repairApplyButton.IsEnabled = false;

        var repairRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        repairRow.AddNode(_repairDryRunButton);
        repairRow.AddNode(_repairApplyButton);

        _repairOutput = new TextNode
        {
            Size = new Vector2(w, 140),
            String = Loc.S("Press Dry Run to scan the database."),
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = LabelColor,
        };
        _repairOutput.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);

        CreateCollapsibleSection(list, Loc.S("NPC Attribution Repair"), w, true,
            [repairDesc, repairRow, _repairOutput]);

        list.AddNode(Separator(w));

        // ── Player Name Cleanup ──────────────────────────────────────────────
        var retokenizeDesc = WrappedLabel(
            Loc.S("Dialogues stored before this version could contain your character's real name " +
                  "instead of the -PlayerName- placeholder, which created a duplicate entry next to " +
                  "the harvested one. This puts the placeholders back and merges the duplicates. " +
                  "No audio file is deleted."),
            w);

        _retokenizeDryRunButton = Button(Loc.S("Dry Run"), 140, OnRetokenizeDryRunClick);
        _retokenizeApplyButton = Button(Loc.S("Apply Cleanup"), 160, OnRetokenizeApplyClick);
        _retokenizeApplyButton.IsEnabled = false;

        var retokenizeRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        retokenizeRow.AddNode(_retokenizeDryRunButton);
        retokenizeRow.AddNode(_retokenizeApplyButton);

        _retokenizeOutput = new TextNode
        {
            Size = new Vector2(w, 100),
            String = Loc.S("Press Dry Run to scan the database."),
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = LabelColor,
        };
        _retokenizeOutput.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);

        CreateCollapsibleSection(list, Loc.S("Player Name Cleanup"), w, true,
            [retokenizeDesc, retokenizeRow, _retokenizeOutput]);

        list.AddNode(Separator(w));

        // ── Import / Export ──────────────────────────────────────────────────
        // Audio packages (*.ekpack): a zip holding every generated WAV plus a manifest that
        // names the character, language and voice of each file, so a receiving install can
        // attribute them without guessing from file names.
        var importDesc = WrappedLabel(
            Loc.S("Pack all your generated audio into a single file you can pass on, or read such " +
                  "a package from someone else. The package carries the NPC, language and voice of " +
                  "every file, so the receiving installation assigns them correctly. Clips that " +
                  "speak your own character's name are never exported — their shareable " +
                  "\"Adventurer\" variants are."),
            w);

        _packagePathInput = Input(Loc.S("Package file"), w - 16, 260, DefaultPackagePath(), _ => { });

        _packageLanguageLabels = new[]
        {
            Loc.S("All languages"), Loc.S("Japanese"), Loc.S("English"),
            Loc.S("German"), Loc.S("French")
        };
        var packageLanguageDropDown = new StringDropDownNode
        {
            Size = new Vector2(180, 28),
            MaxListOptions = 5,
            Options = new System.Collections.Generic.List<string>(_packageLanguageLabels),
        };
        packageLanguageDropDown.SelectedOption = _packageLanguageLabels[0];
        packageLanguageDropDown.LabelNode.String = _packageLanguageLabels[0];
        packageLanguageDropDown.OnOptionSelected =
            selected => _pendingPackageLanguageSelection = Array.IndexOf(_packageLanguageLabels, selected);

        _packageExportButton = Button(Loc.S("Export Package"), 160, OnPackageExportClick);
        _packageImportButton = Button(Loc.S("Import Package"), 160, OnPackageImportClick);

        var packageRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        packageRow.AddNode(_packageExportButton);
        packageRow.AddNode(_packageImportButton);
        packageRow.AddNode(packageLanguageDropDown);

        _packageOutput = new TextNode
        {
            Size = new Vector2(w, 60),
            String = Loc.S("No package operation run yet."),
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = LabelColor,
        };
        _packageOutput.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);

        CreateCollapsibleSection(list, Loc.S("Import / Export"), w, true,
            [importDesc, _packagePathInput, packageRow, _packageOutput]);

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

        // ── Progress bar (harvest + voice pack) — pinned at the very top ─────
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
        _configButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = CircleButtonIcon.GearCog,
            Tooltip = Loc.S("Open configuration window"),
            OnClick = () => _toggleConfig(),
        };
        WireIconButtonHover(_configButton, () => _configButton != null,
            _configButton.ShowTooltip, _configButton.HideTooltip);
        AddNode(_configButton);

        // Voice Clip Manager button — UV (112, 28) on Character.tex = CircleButtonIcon.MusicNote.
        _voiceClipManagerButton = new DynamicIconButtonNode
        {
            Position = new Vector2(pos.X + bottomBtnSize + gap, bottomRowY),
            Size = new Vector2(bottomBtnSize, bottomBtnSize),
            Icon = CircleButtonIcon.MusicNote,
            Tooltip = Loc.S("Open Voice Clip Manager"),
            OnClick = () => _toggleVoiceClipManager(),
        };
        WireIconButtonHover(_voiceClipManagerButton, () => _voiceClipManagerButton != null,
            _voiceClipManagerButton.ShowTooltip, _voiceClipManagerButton.HideTooltip);
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

        if (_pendingPackageLanguageSelection >= 0)
        {
            _selectedPackageLanguageIndex = _pendingPackageLanguageSelection;
            _pendingPackageLanguageSelection = -1;
        }

        UpdateProgressBar();
        UpdateRepairOutput();
        UpdateRetokenizeOutput();
        UpdatePackageOutput();
        ClampToScreen(addon);
    }

    private void UpdatePackageOutput()
    {
        if (_packageOutputDirty && _packageOutput != null)
        {
            string text;
            lock (_statusLock)
            {
                text = _packageOutputText;
                _packageOutputDirty = false;
            }
            _packageOutput.String = text ?? string.Empty;
            var measured = _packageOutput.GetTextDrawSize(false).Y;
            if (measured > 0)
                _packageOutput.Size = new Vector2(_packageOutput.Size.X, measured + 6);
        }

        // Both buttons drive the same service, so a run of either blocks both.
        var idle = !_audioPackage.IsRunning;
        if (_packageExportButton != null) Dim(_packageExportButton, idle);
        if (_packageImportButton != null) Dim(_packageImportButton, idle);
    }

    private void UpdateRepairOutput()
    {
        if (!_repairOutputDirty || _repairOutput == null) return;
        string text;
        lock (_statusLock)
        {
            text = _repairOutputText;
            _repairOutputDirty = false;
        }
        _repairOutput.String = text ?? string.Empty;
        // Resize the node so wrapped multi-line content stays fully visible.
        var measured = _repairOutput.GetTextDrawSize(false).Y;
        if (measured > 0)
            _repairOutput.Size = new Vector2(_repairOutput.Size.X, measured + 6);

        // Enable/disable Apply based on whether the latest report has actionable rows.
        if (_repairApplyButton != null)
            _repairApplyButton.IsEnabled = !_repairBusy && _lastRepairReport != null && _lastRepairReport.Actions.Count > 0;
        if (_repairDryRunButton != null)
            _repairDryRunButton.IsEnabled = !_repairBusy;
    }

    private void UpdateRetokenizeOutput()
    {
        if (_retokenizeOutputDirty && _retokenizeOutput != null)
        {
            string text;
            lock (_statusLock)
            {
                text = _retokenizeOutputText;
                _retokenizeOutputDirty = false;
            }
            _retokenizeOutput.String = text ?? string.Empty;
            var measured = _retokenizeOutput.GetTextDrawSize(false).Y;
            if (measured > 0)
                _retokenizeOutput.Size = new Vector2(_retokenizeOutput.Size.X, measured + 6);
        }

        if (_retokenizeApplyButton != null)
            _retokenizeApplyButton.IsEnabled = !_retokenizeBusy && _retokenizePreviewHasWork;
        if (_retokenizeDryRunButton != null)
            _retokenizeDryRunButton.IsEnabled = !_retokenizeBusy;
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

        string harvestLabel, voicePackLabel, packageLabel;
        lock (_statusLock)
        {
            harvestLabel = _harvestLabel;
            voicePackLabel = _voicePackLabel;
            packageLabel = _packageLabel;
        }

        if (ShowRunProgress(_dialogHarvest.IsRunning, harvestLabel, _harvestCurrent, _harvestTotal)) return;
        if (ShowRunProgress(_voicePack.IsRunning, voicePackLabel, _voicePackCurrent, _voicePackTotal)) return;
        if (ShowRunProgress(_audioPackage.IsRunning, packageLabel, _packageCurrent, _packageTotal)) return;

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

    /// <summary>
    /// Drives the bar for one run. Returns true when that run owns the bar, so the caller can stop
    /// at the first running operation instead of repeating the same block per section.
    /// </summary>
    private bool ShowRunProgress(bool isRunning, string label, int current, int total)
    {
        if (!isRunning || _progressBar == null) return false;

        _progressBar.ActionText = label;
        var fraction = total > 0 ? (float)current / total : 0f;
        _progressBar.SetProgress(fraction, total > 0 ? $"{current}/{total}" : string.Empty);
        return true;
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
                SetTerminalStatus(t.IsFaulted ? Loc.S(StatusFailed)
                    : t.IsCanceled ? Loc.S(StatusCancelled)
                    : Loc.S(StatusDone));
            });
        }
    }

    private void OnDebugExportClick()
    {
        var questIdStr = _debugQuestIdInput?.String ?? string.Empty;
        if (uint.TryParse(questIdStr, out var qid))
            _dialogHarvest.ExportQuestLuaDebug(qid);
    }

    // ── Audio package (Import / Export) handlers ─────────────────────────────

    /// <summary>
    /// Default target for a new package: next to the user's audio, so the file lands somewhere
    /// they already know. Falls back to the plain file name when no save location is configured
    /// (export then fails with a readable message instead of writing to the game folder).
    /// </summary>
    private string DefaultPackagePath()
    {
        var name = $"echokraut-audio{AudioPackageFormat.Extension}";
        var root = _config.LocalSaveLocation;
        return string.IsNullOrWhiteSpace(root) ? name : System.IO.Path.Combine(root, name);
    }

    /// <summary>Dropdown index → <c>ClientLanguage</c> value (0=JP, 1=EN, 2=DE, 3=FR), 0 = all.</summary>
    private int? GetPackageLanguageFilter()
        => _selectedPackageLanguageIndex <= 0 ? null : _selectedPackageLanguageIndex - 1;

    private void OnPackageExportClick()
    {
        if (_audioPackage.IsRunning) return;

        var path = (_packagePathInput?.String.ToString() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetPackageOutput(Loc.S("Enter a target file for the package first."));
            return;
        }

        _packageCts?.Dispose();
        _packageCts = new CancellationTokenSource();
        SetPackageProgress(Loc.S("Exporting audio package"), 0, 0);
        SetPackageOutput(Loc.S("Export running..."));

        var language = GetPackageLanguageFilter();
        _ = _audioPackage.ExportAsync(path, language, _packageCts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
            {
                SetPackageOutput(Loc.S(StatusCancelled));
                SetTerminalStatus(Loc.S(StatusCancelled));
                return;
            }
            if (t.IsFaulted || t.Result.Error != null)
            {
                SetPackageOutput($"{Loc.S("Export failed")}: {t.Result?.Error ?? t.Exception?.GetBaseException().Message}");
                SetTerminalStatus(Loc.S(StatusFailed));
                return;
            }

            var r = t.Result;
            SetPackageOutput(
                $"{Loc.S("Package written")}: {r.PackagePath}\n" +
                $"{Loc.S("Files")}: {r.Exported} · {Loc.S("Missing on disk")}: {r.SkippedMissingFile} · " +
                $"{Loc.S("Personal clips skipped")}: {r.SkippedPersonal} · " +
                $"{Loc.S("Size")}: {r.BytesWritten / (1024 * 1024)} MB");
            SetTerminalStatus(Loc.S(StatusDone));
        });
    }

    private void OnPackageImportClick()
    {
        if (_audioPackage.IsRunning) return;

        var path = (_packagePathInput?.String.ToString() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetPackageOutput(Loc.S("Enter the package file to import first."));
            return;
        }

        _packageCts?.Dispose();
        _packageCts = new CancellationTokenSource();
        SetPackageProgress(Loc.S("Importing audio package"), 0, 0);
        SetPackageOutput(Loc.S("Import running..."));

        _ = _audioPackage.ImportAsync(path, _packageCts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled)
            {
                SetPackageOutput(Loc.S(StatusCancelled));
                SetTerminalStatus(Loc.S(StatusCancelled));
                return;
            }
            if (t.IsFaulted || t.Result.Error != null)
            {
                SetPackageOutput($"{Loc.S("Import failed")}: {t.Result?.Error ?? t.Exception?.GetBaseException().Message}");
                SetTerminalStatus(Loc.S(StatusFailed));
                return;
            }

            var r = t.Result;
            SetPackageOutput(
                $"{Loc.S("Package imported")}\n" +
                $"{Loc.S("Clips")}: {r.Imported} · {Loc.S("New characters")}: {r.CreatedCharacters} · " +
                $"{Loc.S("New dialogues")}: {r.CreatedClips} · " +
                $"{Loc.S("Already present")}: {r.SkippedExistingFile} · {Loc.S("Failed")}: {r.Failed}");
            SetTerminalStatus(Loc.S(StatusDone));
        });
    }

    private void OnPackageProgress(string label, int current, int total)
        => SetPackageProgress(label, current, total);

    private void SetPackageProgress(string label, int current, int total)
    {
        lock (_statusLock) _packageLabel = label;
        _packageCurrent = current;
        _packageTotal = total > 0 ? total : 1;
    }

    private void SetPackageOutput(string text)
    {
        lock (_statusLock)
        {
            _packageOutputText = text;
            _packageOutputDirty = true;
        }
    }

    // ── Voice Pack handlers ──────────────────────────────────────────────────

    private void OnVoicePackClick()
    {
        if (_voicePack.IsRunning)
        {
            _voicePackCts?.Cancel();
            if (_voicePackButton != null)
                _voicePackButton.String = Loc.S("Download Voice Pack");
            return;
        }

        _voicePackCts?.Dispose();
        _voicePackCts = new CancellationTokenSource();

        if (_voicePackButton != null)
            _voicePackButton.String = Loc.S("Stop");
        // Reset the bar at run start; live progress events take over from here.
        SetVoicePackProgress(Loc.S("Starting..."), 0, 0);

        // When the active engine runs as a Local Instance, unpack straight into its voice
        // folder (the pack service wipes it first — see IVoicePackService for the
        // wipe-on-override semantic). Remote / no-instance setups fall back to
        // <LocalSaveLocation>/Voices/ which the user can copy over manually.
        var (overrideRoot, subfolder) = ResolveVoicePackTarget();

        _ = _voicePack.DownloadAsync(_voicePackCts.Token, overrideRoot, subfolder)
            .ContinueWith(t =>
        {
            if (_voicePackButton != null)
                _voicePackButton.String = Loc.S("Download Voice Pack");
            SetTerminalStatus(t.IsFaulted ? Loc.S(StatusFailed)
                : t.IsCanceled ? Loc.S(StatusCancelled)
                : Loc.S(StatusDone));
        });
    }

    /// <summary>
    /// Picks where the downloaded pack lands: the ACTIVE engine's local voice folder when that
    /// engine runs locally, otherwise <c>&lt;LocalSaveLocation&gt;/Voices/</c>.
    /// </summary>
    private (string? Root, string Subfolder) ResolveVoicePackTarget()
    {
        if (string.IsNullOrWhiteSpace(_config.TtsInstallRoot)
            || _config.ActiveInstanceType != AlltalkInstanceType.Local)
            return (null, "Voices");

        return _config.BackendSelection == TTSBackends.EchokrauTTS
            ? (TtsPaths.EchokrauTtsRoot(_config.TtsInstallRoot), TtsPaths.EchokrauTtsSamplesFolder)
            : (TtsPaths.AllTalkRoot(_config.TtsInstallRoot), "voices");
    }

    private void OnVoicePackProgress(string label, int current, int total)
        => SetVoicePackProgress(label, current, total);

    private void SetVoicePackProgress(string label, int current, int total)
    {
        lock (_statusLock) _voicePackLabel = label;
        _voicePackCurrent = current;
        _voicePackTotal = total > 0 ? total : 1;
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

    // ── NPC Attribution Repair handlers ──────────────────────────────────────

    private void OnRepairDryRunClick()
    {
        RunRepairOperation(
            Loc.S("Scanning database — please wait..."),
            Loc.S("Dry run cancelled."),
            Loc.S("Dry run failed: {0}"),
            ct =>
            {
                var report = _repair.BuildDryRunReport(ct);
                _lastRepairReport = report;
                SetRepairOutput(FormatRepairReport(report));
            });
    }

    private void OnRepairApplyClick()
    {
        var report = _lastRepairReport;
        if (report == null || report.Actions.Count == 0) return;

        RunRepairOperation(
            string.Format(Loc.S("Applying repair to {0} entries..."), report.Actions.Count),
            Loc.S("Apply cancelled."),
            Loc.S("Apply failed: {0}"),
            ct =>
            {
                var result = _repair.Apply(report, ct);
                SetRepairOutput(FormatRepairResult(result));
                // Clear the report — applying it consumes it; a fresh dry-run is required
                // to surface anything that's now drifted.
                _lastRepairReport = null;
            });
    }

    /// <summary>
    /// Shared scaffolding for the repair handlers: guards against re-entrancy, shows the start
    /// message, runs <paramref name="body"/> on a background task with a fresh cancellation token,
    /// and reports cancellation/failure uniformly. <paramref name="failFormat"/> is a "{0}"
    /// format string for the exception message.
    /// </summary>
    private void RunRepairOperation(string startMessage, string cancelMessage, string failFormat, Action<CancellationToken> body)
    {
        if (_repairBusy) return;
        _repairBusy = true;
        SetRepairOutput(startMessage);
        _repairCts?.Dispose();
        _repairCts = new CancellationTokenSource();
        var ct = _repairCts.Token;

        Task.Run(() =>
        {
            try
            {
                body(ct);
            }
            catch (OperationCanceledException)
            {
                SetRepairOutput(cancelMessage);
            }
            catch (Exception ex)
            {
                SetRepairOutput(string.Format(failFormat, ex.Message));
            }
            finally
            {
                _repairBusy = false;
            }
        }, ct);
    }

    private void SetRepairOutput(string text)
    {
        lock (_statusLock)
        {
            _repairOutputText = text;
            _repairOutputDirty = true;
        }
    }

    private static string FormatRepairReport(NpcAttributionRepairReport report)
    {
        if (report.Actions.Count == 0)
        {
            return string.Format(
                Loc.S("Scanned {0} instances. No repairs needed — every dialog is attributed to its canonical NPC. Skipped: {1} without Lumina row, {2} without a canonical character row in the DB."),
                report.TotalInstancesScanned, report.SkippedNoLuminaRow, report.SkippedNoCanonicalInDb);
        }

        // Cap the per-action preview so the multi-line text stays readable; surface aggregate
        // counts for the rest. Apply still processes the full list.
        const int previewCount = 15;
        var preview = string.Join("\n", report.Actions
            .Take(previewCount)
            .Select(a => string.Format(
                "  {0} #{1} (clips~{2}) -> {3} #{4} [baseId={5}, lang={6}]",
                a.OldCharacterName, a.OldCharacterId, a.VoiceClipCount,
                a.CanonicalName, a.NewCharacterId, a.NpcBaseId, a.Language)));

        var rest = report.Actions.Count - previewCount;
        var more = rest > 0 ? "\n" + string.Format(Loc.S("...and {0} more."), rest) : string.Empty;

        return string.Format(
            Loc.S("Found {0} mis-attributed instances out of {1} scanned. Skipped: {2} without Lumina row, {3} without canonical character row.\n\n{4}{5}\n\nPress Apply Repair to move the dialogues to the correct characters."),
            report.Actions.Count, report.TotalInstancesScanned, report.SkippedNoLuminaRow, report.SkippedNoCanonicalInDb,
            preview, more);
    }

    private static string FormatRepairResult(NpcAttributionRepairResult result)
    {
        return string.Format(
            Loc.S("Repair applied. Reassigned {0} instances, moved {1} voice clips, merged-and-deleted {2} duplicate clips, deleted {3} emptied character rows. Re-open the Voice Clip Manager to see the cleaned view."),
            result.InstancesReassigned, result.VoiceClipsMoved, result.VoiceClipsMergedAndDeleted, result.CharactersDeleted);
    }

    // ── Player Name Cleanup handlers ─────────────────────────────────────────

    private void OnRetokenizeDryRunClick() => RunRetokenize(dryRun: true);

    private void OnRetokenizeApplyClick()
    {
        if (!_retokenizePreviewHasWork) return;
        RunRetokenize(dryRun: false);
    }

    private void RunRetokenize(bool dryRun)
    {
        if (_retokenizeBusy) return;

        var playerName = _gameObjects.LocalPlayerName;
        if (string.IsNullOrWhiteSpace(playerName))
        {
            SetRetokenizeOutput(Loc.S("Your character name isn't known yet — log in and try again."));
            return;
        }

        _retokenizeBusy = true;
        SetRetokenizeOutput(dryRun
            ? Loc.S("Scanning database — please wait...")
            : Loc.S("Applying cleanup — please wait..."));
        _retokenizeCts?.Dispose();
        _retokenizeCts = new CancellationTokenSource();
        var ct = _retokenizeCts.Token;

        Task.Run(() =>
        {
            try
            {
                var result = _db.RetokenizePlayerNames(playerName, dryRun, ct);
                _retokenizePreviewHasWork = dryRun && result.HasWork;
                SetRetokenizeOutput(FormatRetokenizeResult(result, dryRun));
            }
            catch (OperationCanceledException)
            {
                SetRetokenizeOutput(Loc.S("Cleanup cancelled."));
            }
            catch (Exception ex)
            {
                SetRetokenizeOutput(string.Format(Loc.S("Cleanup failed: {0}"), ex.Message));
            }
            finally
            {
                _retokenizeBusy = false;
            }
        }, ct);
    }

    private void SetRetokenizeOutput(string text)
    {
        lock (_statusLock)
        {
            _retokenizeOutputText = text;
            _retokenizeOutputDirty = true;
        }
    }

    private static string FormatRetokenizeResult(PlayerNameRetokenizeResult result, bool dryRun)
    {
        if (!result.HasWork)
        {
            return string.Format(
                Loc.S("Scanned {0} dialogues. Nothing to clean up — no stored dialogue contains your character's name."),
                result.Scanned);
        }

        var samples = result.Samples.Count > 0
            ? "\n\n" + string.Join("\n", result.Samples.Select(s => "  " + s))
            : string.Empty;

        return dryRun
            ? string.Format(
                Loc.S("Found {0} dialogues to restore and {1} duplicates to merge (out of {2} scanned). Press Apply Cleanup to write the changes.{3}"),
                result.Rewritten, result.MergedAndDeleted, result.Scanned, samples)
            : string.Format(
                Loc.S("Cleanup applied. Restored {0} dialogues, merged {1} duplicates, moved {2} generated audio records, dropped {3} redundant ones. Re-open the Voice Clip Manager to see the cleaned view."),
                result.Rewritten, result.MergedAndDeleted, result.GenerationsMoved, result.GenerationsDropped);
    }

    // ── Local node factories ─────────────────────────────────────────────────


    /// <summary>Multi-line word-wrapped label sized for a row width.</summary>
    private static TextNode WrappedLabel(string text, float width)
    {
        var node = new TextNode
        {
            Size = new Vector2(width, 36),
            String = text,
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = LabelColor,
        };
        node.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
        // Estimate height after wrapping; KamiToolKit doesn't auto-resize TextNode height.
        var measured = node.GetTextDrawSize(false).Y;
        if (measured > 0)
            node.Size = new Vector2(width, measured + 6);
        return node;
    }




}
