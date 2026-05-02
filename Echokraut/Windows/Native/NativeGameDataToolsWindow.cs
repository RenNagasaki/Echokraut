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

    // ── Quest Dialog Harvest section ─────────────────────────────────────────
    private TextButtonNode? _harvestButton;
    private CancellationTokenSource? _harvestCts;
    private TextInputNode? _debugQuestIdInput;
    private TextButtonNode? _debugExportButton;

    // ── Voice Starter Set section ────────────────────────────────────────────
    private SliderNode? _samplesSlider;
    private TextNode? _samplesValueLabel;
    private TextButtonNode? _starterSetButton;
    private CancellationTokenSource? _starterCts;
    private TextNode? _starterStatusLabel;

    private string _starterStatusText = string.Empty;
    private bool _starterStatusDirty;
    private readonly object _statusLock = new();

    public NativeGameDataToolsWindow(
        IDialogHarvestService dialogHarvest,
        IVoiceSampleExtractorService voiceExtract,
        IClientState clientState,
        ILogService log)
    {
        _dialogHarvest = dialogHarvest;
        _voiceExtract = voiceExtract;
        _clientState = clientState;
        _log = log;

        _voiceExtract.ProgressChanged += OnExtractProgress;
    }

    public override void Dispose()
    {
        _voiceExtract.ProgressChanged -= OnExtractProgress;
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

        var list = new ScrollingListNode
        {
            Position = pos,
            Size = size,
            FitWidth = true,
            ItemSpacing = 4,
        };

        // ── Quest Dialog Harvest ─────────────────────────────────────────────
        var questDesc = WrappedLabel(
            Loc.S("Scan game data for NPC dialog text and persist linked entries to the database. " +
                  "Use this to populate or refresh your voice clip catalog after a game patch."),
            w);

        _harvestButton = Button(Loc.S("Start Harvest"), 160, OnHarvestClick);
        _debugQuestIdInput = Input("Quest ID", 120, 10, "65614", _ => { });
        _debugExportButton = Button(Loc.S("Export Quest Lua Debug"), 200, OnDebugExportClick);

        var harvestRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 6 };
        harvestRow.AddNode(_harvestButton);
        harvestRow.AddNode(_debugQuestIdInput);
        harvestRow.AddNode(_debugExportButton);

        CreateCollapsibleSection(list, Loc.S("Quest Dialog Harvest"), w, false,
            [questDesc, harvestRow]);

        list.AddNode(Separator(w));

        // ── Voice Starter Set ────────────────────────────────────────────────
        var starterDesc = WrappedLabel(
            Loc.S("Extract voice samples from the game's built-in voice acting and write them " +
                  "to FF14-Voices/ inside your local save location. AllTalk-compatible (22050 Hz). " +
                  "Always overwrites previous output."),
            w);

        _samplesSlider = new SliderNode
        {
            Size = new Vector2(w - 120, 22),
            Range = 1..5,
            Value = 3,
            DecimalPlaces = 0,
        };
        _samplesValueLabel = Label("3", 30);
        _samplesSlider.OnValueChanged = v =>
        {
            if (_samplesValueLabel != null)
                _samplesValueLabel.String = ((int)v).ToString();
        };

        var sliderLabel = Label(Loc.S("Samples per NPC (1-5):"), 180);
        var sliderRow = new HorizontalListNode { Size = new Vector2(w, 24), ItemSpacing = 6 };
        sliderRow.AddNode(sliderLabel);
        sliderRow.AddNode(_samplesSlider);
        sliderRow.AddNode(_samplesValueLabel);

        _starterSetButton = Button(Loc.S("Build Starter Set"), 200, OnStarterSetClick);
        _starterStatusLabel = Label(string.Empty, w);

        CreateCollapsibleSection(list, Loc.S("Voice Starter Set"), w, false,
            [starterDesc, sliderRow, _starterSetButton, _starterStatusLabel]);

        list.AddNode(Separator(w));

        // ── Import / Export (placeholder) ────────────────────────────────────
        var importDesc = WrappedLabel(
            Loc.S("Coming soon: backup / restore of database + configuration, and import/export " +
                  "of community voice sets."),
            w);
        CreateCollapsibleSection(list, Loc.S("Import / Export"), w, true, [importDesc]);

        AddNode(list);
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        _log.UpdateMainThreadLogs();

        // Drain pending status text into the label on the main thread.
        if (_starterStatusDirty)
        {
            string text;
            lock (_statusLock)
            {
                text = _starterStatusText;
                _starterStatusDirty = false;
            }
            if (_starterStatusLabel != null)
                _starterStatusLabel.String = text;
        }

        ClampToScreen(addon);
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
            _ = _dialogHarvest.RunAsync(_clientState.ClientLanguage, _harvestCts.Token).ContinueWith(_ =>
            {
                if (_harvestButton != null)
                    _harvestButton.String = Loc.S("Start Harvest");
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

        var samples = _samplesSlider != null ? Math.Clamp((int)_samplesSlider.Value, 1, 5) : 3;
        _starterCts?.Dispose();
        _starterCts = new CancellationTokenSource();

        if (_starterSetButton != null)
            _starterSetButton.String = Loc.S("Stop");
        SetStatus(Loc.S("Starting..."));

        _ = _voiceExtract.RunAsync(_clientState.ClientLanguage, samples, _starterCts.Token).ContinueWith(t =>
        {
            if (_starterSetButton != null)
                _starterSetButton.String = Loc.S("Build Starter Set");
            if (t.IsFaulted)
                SetStatus(Loc.S("Failed — see logs"));
            else if (t.IsCanceled)
                SetStatus(Loc.S("Cancelled"));
            else
                SetStatus(Loc.S("Done"));
        });
    }

    private void OnExtractProgress(string label, int current, int total)
    {
        var text = total > 0 ? $"{label}  {current}/{total}" : label;
        SetStatus(text);
    }

    private void SetStatus(string text)
    {
        lock (_statusLock)
        {
            _starterStatusText = text;
            _starterStatusDirty = true;
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
