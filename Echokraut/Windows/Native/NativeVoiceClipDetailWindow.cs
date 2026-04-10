using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Echokraut.DataClasses.Database;
using Echotools.UI.Nodes;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

public sealed unsafe class NativeVoiceClipDetailWindow : NativeAddon
{
    private readonly IDatabaseService _db;
    private readonly IVoiceClipManagerService _voiceClipManager;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IGameObjectService _gameObjects;

    private float _contentWidth;
    private ScrollingListNode? _panel;
    private TextButtonNode? _genToggleButton;
    private StatusProgressBar? _progressBar;
    private int _genDone;
    private int _genTotal;
    private CancellationTokenSource? _genCts;
    private bool _genRunning;

    // Column widths (measured at setup)
    private float _colPlay;
    private float _colSource;
    private const float ColTimestamp = 85f;
    private float _colText;

    // Pagination
    private const int PageSize = 100;
    private PaginationBar? _paginationBar;
    private bool _paginationPending;

    // Current data
    private List<VoiceClipEntity> _voiceClips = new();
    private HashSet<int>? _voiceClipIdFilter; // IDs from original filtered set
    private string _npcKey = "";
    private int _characterId;
    private bool _needsRebuild;
    private int? _playingVoiceClipId;
    private readonly Dictionary<int, bool> _audioExistsCache = new();
    private readonly Dictionary<int, (DynamicIconButtonNode playBtn, DynamicIconButtonNode genBtn, bool wasSaved)> _buttonImages = new();
    private Action? _pendingAction; // Deferred click action to avoid ATK use-after-free
    private int _progressUpdateCounter;


    public NativeVoiceClipDetailWindow(
        IDatabaseService db,
        IVoiceClipManagerService voiceClipManager,
        IAudioPlaybackService audioPlayback,
        IGameObjectService gameObjects)
    {
        _db = db;
        _voiceClipManager = voiceClipManager;
        _audioPlayback = audioPlayback;
        _gameObjects = gameObjects;

        _onVoiceClipUpdated = () => _audioExistsCache.Clear();
        _onVoiceClipLogged = () =>
        {
            if (_characterId > 0 && IsOpen)
            {
                ReloadFilteredVoiceClips();
                _paginationBar?.SetTotalItems(_voiceClips.Count, PageSize);
                _needsRebuild = true;
                _audioExistsCache.Clear();
            }
        };
        _onCurrentMessageChanged = msg =>
        {
            if (msg == null) _playingVoiceClipId = null;
        };
        _voiceClipManager.VoiceClipUpdated += _onVoiceClipUpdated;
        _db.VoiceClipLogged += _onVoiceClipLogged;
        _audioPlayback.CurrentMessageChanged += _onCurrentMessageChanged;
    }

    private readonly Action _onVoiceClipUpdated;
    private readonly Action _onVoiceClipLogged;
    private readonly Action<Echokraut.DataClasses.VoiceMessage?> _onCurrentMessageChanged;

    protected override void OnFinalize(AtkUnitBase* addon)
    {
        try { _voiceClipManager.VoiceClipUpdated -= _onVoiceClipUpdated; } catch { }
        try { _db.VoiceClipLogged -= _onVoiceClipLogged; } catch { }
        try { _audioPlayback.CurrentMessageChanged -= _onCurrentMessageChanged; } catch { }
    }

    protected override void OnSetup(AtkUnitBase* addon)
    {
        // Shift content up to remove empty tab bar space
        ContentPadding = new Vector2(8.0f, -20.0f);
        var pos = ContentStartPosition;
        var size = ContentSize;
        _contentWidth = size.X;

        // Action buttons row (offset from content top to avoid overlapping window title)
        var btnY = pos.Y + 24;
        var btnRow = new HorizontalListNode
        {
            Position = new Vector2(pos.X, btnY),
            Size = new Vector2(size.X, 26),
            ItemSpacing = 4,
        };
        _genToggleButton = Button(Loc.S("Generate All Unsaved"), 160, () =>
        {
            if (_genRunning)
            {
                _genCts?.Cancel();
                return;
            }

            _genRunning = true;
            _genCts?.Dispose();
            _genCts = new CancellationTokenSource();
            if (_genToggleButton != null) _genToggleButton.String = Loc.S("Stop");
            _genDone = 0;
            _genTotal = 0;
            ShowProgressBar(true);

            _voiceClipManager.GenerateAllUnsaved(_voiceClips,
                (done, total) =>
                {
                    _genDone = done;
                    _genTotal = total;
                    // Clear cache so throttled OnUpdate picks up new audio state
                    _audioExistsCache.Clear();
                },
                _genCts.Token).ContinueWith(_ =>
            {
                _genRunning = false;
                if (_genToggleButton != null) _genToggleButton.String = Loc.S("Generate All Unsaved");
                ShowProgressBar(false);
                _audioExistsCache.Clear();
            });
        });
        btnRow.AddNode(_genToggleButton);

        // Progress bar inline on button row
        var barOffset = 268f;
        var barWidth = size.X - barOffset - 34;
        _progressBar = new StatusProgressBar
        {
            Position = new Vector2(barOffset, 2),
            Size = new Vector2(barWidth, 28),
        };
        _progressBar.AttachNode(btnRow);

        AddNode(btnRow);

        // Circle buttons: play (28px) + regenerate (28px) in a group with 2px spacing
        _colPlay = 58f; // 28 + 2 + 28

        // Measure widest source name (only sources that actually appear in encounters)
        var measureTxt = new TextNode { FontType = FontType.Axis, FontSize = 12 };
        var sourceNames = new[] { "AddonTalk", "AddonBattleTalk", "AddonBubble", "Chat" };
        _colSource = 70f;
        foreach (var s in sourceNames)
        {
            var sw = measureTxt.GetTextDrawSize(s).X + 8;
            if (sw > _colSource) _colSource = sw;
        }
        measureTxt.Dispose();

        var hw = size.X - 16; // scrollbar offset
        _colText = hw - _colPlay - _colSource - ColTimestamp - 3 * 4;
        if (_colText < 80) _colText = 80;

        // Column headers
        var headerY = btnY + 30;
        var headers = new HorizontalListNode
        {
            Position = new Vector2(pos.X, headerY),
            Size = new Vector2(hw, 20),
            ItemSpacing = 4,
        };
        headers.AddNode(Spacer(_colPlay, 20));  // Play + Regenerate button group
        headers.AddNode(HeaderLabel(Loc.S("Source"), _colSource));
        headers.AddNode(HeaderLabel(Loc.S("Timestamp"), ColTimestamp));
        headers.AddNode(HeaderLabel(Loc.S("Text"), _colText));
        AddNode(headers);

        var sepY = headerY + 20;
        var sep = new HorizontalLineNode
        {
            Position = new Vector2(pos.X, sepY),
            Size = new Vector2(size.X, 4),
        };
        AddNode(sep);

        // Pagination controls (ListItemB arrow style from GatheringNoteBook)
        // ContentPadding.Y is negative to shift content up, which inflates ContentSize.Y.
        // Compensate so the pagination bar stays inside the window bottom border.
        const float paginationH = 28f;
        var bottomOffset = Math.Abs(ContentPadding.Y);
        var pagY = pos.Y + size.Y - paginationH - bottomOffset;

        _paginationBar = new PaginationBar(
            new Vector2(pos.X, pagY), size.X,
            page => _needsRebuild = true);
        foreach (var node in _paginationBar.Nodes)
            AddNode(node);

        // Data panel (above pagination)
        var dataY = sepY + 6;
        var dataH = pagY - dataY - 4;
        _panel = new ScrollingListNode
        {
            Position = new Vector2(pos.X, dataY),
            Size = new Vector2(size.X, dataH),
            FitWidth = true,
            ItemSpacing = 2,
        };
        AddNode(_panel);
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        ScreenClampHelper.ClampToScreen(addon, Size);

        // Process deferred actions (button clicks that would cause use-after-free if run immediately)
        if (_pendingAction != null)
        {
            var action = _pendingAction;
            _pendingAction = null;
            action();
            return;
        }

        if (_paginationPending && _paginationBar != null)
        {
            _paginationPending = false;
            _paginationBar.SetTotalItems(_voiceClips.Count, PageSize);
        }

        _paginationBar?.Update();

        if (_needsRebuild)
        {
            _needsRebuild = false;
            RebuildPanel();
            UpdateProgressBar();
        }

        // Throttled update: swap icons, tooltips, dim state, and progress bar
        _progressUpdateCounter++;
        if (_progressUpdateCounter >= 30)
        {
            _progressUpdateCounter = 0;
            UpdateProgressBar();

            var isGen = _voiceClipManager.IsGenerating;
            try
            {
                var updates = new Dictionary<int, (DynamicIconButtonNode playBtn, DynamicIconButtonNode genBtn, bool wasSaved)>(_buttonImages);
                foreach (var (id, (playBtn, genBtn, wasSaved)) in updates)
                {
                    if (playBtn == null || genBtn == null) continue;
                    var nowSaved = GetAudioExists(_voiceClips.Find(vc => vc.Id == id)!);

                    // Play/Stop icon + tooltip swap
                    var isPlaying = _playingVoiceClipId == id;
                    playBtn.Icon = isPlaying ? ButtonIcon.Mute : ButtonIcon.Volume;
                    playBtn.Tooltip = isPlaying ? Loc.S("Stop voice clip")
                        : nowSaved ? Loc.S("Play voice clip")
                        : Loc.S("Generate and play voice clip");

                    // Gen icon + tooltip swap
                    genBtn.Icon = nowSaved ? ButtonIcon.Refresh : ButtonIcon.MusicNote;
                    genBtn.Tooltip = nowSaved ? Loc.S("Generate again") : Loc.S("Generate");

                    // Dim gen buttons + unsaved play buttons during batch
                    genBtn.ImageNode.Alpha = isGen ? 178f / 255f : 1f;
                    genBtn.ImageNode.MultiplyColor = isGen ? new Vector3(0.5f, 0.5f, 0.5f) : new Vector3(1f, 1f, 1f);
                    if (!nowSaved)
                    {
                        playBtn.ImageNode.Alpha = isGen ? 178f / 255f : 1f;
                        playBtn.ImageNode.MultiplyColor = isGen ? new Vector3(0.5f, 0.5f, 0.5f) : new Vector3(1f, 1f, 1f);
                    }
                    else if (wasSaved != nowSaved)
                    {
                        // Clip just got generated — restore play button brightness
                        playBtn.ImageNode.Alpha = 1f;
                        playBtn.ImageNode.MultiplyColor = new Vector3(1f, 1f, 1f);
                    }

                    // Update tracked state
                    if (wasSaved != nowSaved)
                        _buttonImages[id] = (playBtn, genBtn, nowSaved);
                }
            }
            catch { /* Buttons may be disposed during rebuild */ }
        }
        else
        {
            // Play/stop icon needs per-frame update for responsive feel
            try
            {
                foreach (var (id, (playBtn, _, _)) in _buttonImages)
                {
                    if (playBtn == null) continue;
                    playBtn.Icon = _playingVoiceClipId == id ? ButtonIcon.Mute : ButtonIcon.Volume;
                }
            }
            catch { }
        }

    }

    private void ReloadFilteredVoiceClips()
    {
        var all = _db.GetVoiceClipsForCharacter(_characterId, 10000);
        if (_voiceClipIdFilter != null)
            all = all.FindAll(vc => _voiceClipIdFilter.Contains(vc.Id));
        _voiceClips = all
            .OrderBy(vc => vc.TextSource)
            .ThenBy(vc => vc.Timestamp)
            .ThenBy(vc => vc.OriginalText)
            .ToList();
    }

    public void ShowVoiceClips(string title, List<VoiceClipEntity> voiceClips, string npcKey)
    {
        _voiceClips = voiceClips
            .OrderBy(vc => vc.TextSource)
            .ThenBy(vc => vc.Timestamp)
            .ThenBy(vc => vc.OriginalText)
            .ToList();
        _voiceClipIdFilter = _voiceClips.Select(vc => vc.Id).ToHashSet();
        _npcKey = npcKey;
        _characterId = voiceClips.Count > 0 ? voiceClips[0].CharacterId : 0;
        _audioExistsCache.Clear();
        _buttonImages.Clear();
        if (_paginationBar != null)
            _paginationBar.SetTotalItems(voiceClips.Count, PageSize);
        else
            _paginationPending = true;

        _needsRebuild = true;

        if (!IsOpen)
            Toggle();

        Title = title;
    }

    private unsafe void RebuildPanel()
    {
        if (_panel == null) return;

        // Hide any stuck tooltip before disposing nodes
        AtkStage.Instance()->TooltipManager.HideTooltip(0);

        _panel.Clear();
        _buttonImages.Clear();
        _audioExistsCache.Clear();

        var pageStart = _paginationBar!.CurrentPage * PageSize;
        var pageEnd = Math.Min(pageStart + PageSize, _voiceClips.Count);
        var w = _contentWidth - 16; // scrollbar

        if (pageEnd <= pageStart)
        {
            _panel.AddNode(Label(Loc.S("No voice clips found."), _contentWidth));
        }
        else
        {
            for (var i = pageStart; i < pageEnd; i++)
                _panel.AddNode(BuildVoiceClipRow(_voiceClips[i], w));
        }

        _panel.RecalculateLayout();
    }

    private HorizontalListNode BuildVoiceClipRow(VoiceClipEntity vc, float w)
    {
        var hasSaved = GetAudioExists(vc);
        var capturedVoiceClip = vc;
        var capturedKey = _npcKey;

        // Create text node first to measure wrapped height
        var textNode = new TextNode
        {
            Size = new Vector2(_colText, 18),
            Position = new Vector2(0, 5),
            String = TalkTextHelper.SubstitutePlaceholders(vc.OriginalText, _gameObjects.LocalPlayerName, _gameObjects.LocalPlayerIsMale),
            FontType = FontType.Axis,
            FontSize = 12,
        };
        textNode.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
        var textHeight = textNode.GetTextDrawSize(false).Y;
        var rowHeight = Math.Max(26f, textHeight + 8);

        var row = new HorizontalListNode { Size = new Vector2(w, rowHeight), ItemSpacing = 4 };

        // Play/Stop or Generate circle button (CircleButtons texture)
        var vcId = capturedVoiceClip.Id;

        // Play/Stop button — icon swapped in OnUpdate
        var playTooltip = hasSaved ? Loc.S("Play voice clip") : Loc.S("Generate and play voice clip");
        var playBtn = new DynamicIconButtonNode { Size = new Vector2(28, 28) };
        playBtn.Icon = ButtonIcon.Volume;
        playBtn.Tooltip = playTooltip;
        playBtn.OnClick = () =>
        {
            _pendingAction = () =>
            {
                if (_playingVoiceClipId == vcId)
                {
                    _voiceClipManager.StopPlayback();
                    _playingVoiceClipId = null;
                    return;
                }

                // Block generation while batch is running (playback still allowed)
                if (_voiceClipManager.IsGenerating && !GetAudioExists(capturedVoiceClip))
                    return;

                _playingVoiceClipId = vcId;
                if (GetAudioExists(capturedVoiceClip))
                    _voiceClipManager.PlayVoiceClip(capturedVoiceClip);
                else
                {
                    _voiceClipManager.GenerateForVoiceClip(capturedVoiceClip).ContinueWith(t =>
                    {
                        if (t.Result)
                            _voiceClipManager.PlayVoiceClip(capturedVoiceClip);
                        else
                            _playingVoiceClipId = null;
                        _audioExistsCache.Clear();
                    });
                }
            };
        };

        // Generate / Regenerate button — icon swapped in OnUpdate
        var genTooltip = hasSaved ? Loc.S("Generate again") : Loc.S("Generate");
        var capturedHasSaved = hasSaved;
        var genBtn = new DynamicIconButtonNode { Size = new Vector2(28, 28) };
        genBtn.Icon = hasSaved ? ButtonIcon.Refresh : ButtonIcon.MusicNote;
        genBtn.Tooltip = genTooltip;
        genBtn.OnClick = () =>
        {
            if (_voiceClipManager.IsGenerating) return;
            // Defer to next frame to avoid ATK use-after-free (delete triggers rebuild)
            _pendingAction = () =>
            {
                _audioPlayback.ClearQueue(TextSource.VoiceTest);
                _playingVoiceClipId = null;
                if (capturedHasSaved)
                    _voiceClipManager.DeleteAudioForVoiceClip(capturedVoiceClip);
                _voiceClipManager.GenerateForVoiceClip(capturedVoiceClip).ContinueWith(_ =>
                {
                    _audioExistsCache.Clear();
                });
            };
        };

        // Wrap both buttons in a tight sub-row
        var btnGroup = new HorizontalListNode
        {
            Size = new Vector2(28 + 2 + 28, 28),
            ItemSpacing = 2,
            Position = new Vector2(0, 0),
        };
        btnGroup.AddNode(playBtn);
        btnGroup.AddNode(genBtn);
        _buttonImages[vcId] = (playBtn, genBtn, hasSaved);
        row.AddNode(btnGroup);

        // Source
        row.AddNode(Label(((TextSource)vc.TextSource).ToString(), _colSource));

        // Timestamp
        row.AddNode(Label(vc.Timestamp.ToLocalTime().ToString("MM/dd HH:mm"), ColTimestamp));

        // Text (word wrapped, dynamic height)
        textNode.Size = new Vector2(_colText, Math.Max(18, textHeight));
        row.AddNode(textNode);

        return row;
    }

    private void ShowProgressBar(bool generating)
    {
        if (_progressBar != null)
            _progressBar.ActionText = generating ? Loc.S("Generating voice clips...") : Loc.S("Generation progress");
    }

    private void UpdateProgressBar()
    {
        if (_progressBar == null) return;

        var total = _voiceClips.Count;
        var saved = _voiceClips.Count(vc => _voiceClipManager.HasLocalAudio(vc));
        var fraction = total > 0 ? (float)saved / total : 0f;

        _progressBar.SetProgress(fraction, $"{saved}/{total}");
    }

    private bool GetAudioExists(VoiceClipEntity vc)
    {
        if (_audioExistsCache.TryGetValue(vc.Id, out var exists))
            return exists;
        exists = _voiceClipManager.HasLocalAudio(vc);
        _audioExistsCache[vc.Id] = exists;
        return exists;
    }

    // ── Helpers ──────────────────────────────────────────────

    private static TextNode Label(string text, float width) => new()
    {
        Size = new Vector2(width, 18),
        Position = new Vector2(0, 5),
        String = text,
        FontType = FontType.Axis,
        FontSize = 12,
    };

    private static TextNode HeaderLabel(string text, float width)
    {
        var node = Label(text, width);
        node.AddTextFlags(TextFlags.Ellipsis);
        return node;
    }

    private static ResNode Spacer(float width, float height) => new()
    {
        Size = new Vector2(width, height),
        Alpha = 0,
    };

    private static TextButtonNode Button(string label, float minWidth, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(minWidth, 24), String = label };
        var textW = node.LabelNode.GetTextDrawSize(label).X + 36;
        if (textW > minWidth) node.Size = new Vector2(textW, 24);
        node.OnClick = onClick;
        return node;
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}
