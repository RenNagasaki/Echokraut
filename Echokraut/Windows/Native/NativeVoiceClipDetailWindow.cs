using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses.Database;
using Echotools.UI.Nodes;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

public sealed unsafe class NativeVoiceClipDetailWindow : NativeAddon
{
    private readonly IDatabaseService _db;
    private readonly IVoiceClipManagerService _voiceClipManager;
    private readonly IAudioPlaybackService _audioPlayback;

    private float _contentWidth;
    private ScrollingListNode? _panel;
    private TextNode? _titleLabel;

    // Column widths (measured at setup)
    private float _colPlay;
    private float _colSource;
    private const float ColTimestamp = 85f;
    private const float ColSaved = 20f;
    private float _colDel;
    private float _colText;

    // Pagination
    private const int PageSize = 100;
    private PaginationBar? _paginationBar;
    private bool _paginationPending;

    // Current data
    private List<VoiceClipEntity> _encounters = new();
    private string _npcKey = "";
    private int _characterId;
    private bool _needsRebuild;
    private int? _playingVoiceClipId;
    private readonly Dictionary<int, bool> _audioExistsCache = new();
    private readonly Dictionary<int, TextButtonNode> _playButtons = new();

    // Progressive loading
    private int _progressiveIndex;
    private bool _progressiveActive;
    private const int RowsPerFrame = 10;

    public NativeVoiceClipDetailWindow(
        IDatabaseService db,
        IVoiceClipManagerService voiceClipManager,
        IAudioPlaybackService audioPlayback)
    {
        _db = db;
        _voiceClipManager = voiceClipManager;
        _audioPlayback = audioPlayback;

        _voiceClipManager.VoiceClipUpdated += () =>
        {
            if (_characterId > 0)
                _encounters = _db.GetVoiceClipsForCharacter(_characterId, 10000);
            _needsRebuild = true;
            _audioExistsCache.Clear();
        };
        _db.VoiceClipLogged += () =>
        {
            if (_characterId > 0 && IsOpen)
            {
                _encounters = _db.GetVoiceClipsForCharacter(_characterId, 10000);
                _paginationBar?.SetTotalItems(_encounters.Count, PageSize);
                _needsRebuild = true;
                _audioExistsCache.Clear();
            }
        };
        _audioPlayback.CurrentMessageChanged += msg =>
        {
            if (msg == null) _playingVoiceClipId = null;
        };
    }

    protected override void OnSetup(AtkUnitBase* addon)
    {
        // Shift content up to remove empty tab bar space
        ContentPadding = new Vector2(8.0f, -20.0f);
        var pos = ContentStartPosition;
        var size = ContentSize;
        _contentWidth = size.X;

        _titleLabel = new TextNode
        {
            Position = pos,
            Size = new Vector2(size.X, 24),
            String = "",
            FontType = FontType.Axis,
            FontSize = 14,
        };
        AddNode(_titleLabel);

        // Action buttons row
        var btnY = pos.Y + 26;
        var btnRow = new HorizontalListNode
        {
            Position = new Vector2(pos.X, btnY),
            Size = new Vector2(size.X, 26),
            ItemSpacing = 4,
        };
        btnRow.AddNode(Button(Loc.S("Generate All Unsaved"), 160, () =>
        {
            _voiceClipManager.GenerateAllUnsaved(_encounters).ContinueWith(_ =>
            {
                _needsRebuild = true;
                _audioExistsCache.Clear();
            });
        }));
        btnRow.AddNode(Button(Loc.S("Delete All Saved"), 140, () =>
        {
            _audioPlayback.ClearQueue(TextSource.VoiceTest);
            _playingVoiceClipId = null;
            _voiceClipManager.DeleteAllSaved(_encounters);
            _needsRebuild = true;
            _audioExistsCache.Clear();
        }));
        AddNode(btnRow);

        // Measure column widths from localized text
        var measureBtn = new TextButtonNode { Size = new Vector2(40, 24), String = Loc.S("Play") };
        var playW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Play")).X + 36;
        var genW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Generate")).X + 36;
        _colPlay = Math.Max(40f, Math.Max(playW, genW));

        var delW = measureBtn.LabelNode.GetTextDrawSize(Loc.S("Del")).X + 36;
        _colDel = Math.Max(40f, delW);
        measureBtn.Dispose();

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
        _colText = hw - _colPlay - _colSource - ColTimestamp - _colDel - 3 * 4;
        if (_colText < 80) _colText = 80;

        // Column headers
        var headerY = btnY + 30;
        var headers = new HorizontalListNode
        {
            Position = new Vector2(pos.X, headerY),
            Size = new Vector2(hw, 20),
            ItemSpacing = 4,
        };
        headers.AddNode(Spacer(_colPlay, 20));
        headers.AddNode(HeaderLabel(Loc.S("Source"), _colSource));
        headers.AddNode(HeaderLabel(Loc.S("Timestamp"), ColTimestamp));
        headers.AddNode(HeaderLabel(Loc.S("Text"), _colText));
        headers.AddNode(Spacer(_colDel, 20));
        AddNode(headers);

        var sepY = headerY + 20;
        var sep = new HorizontalLineNode
        {
            Position = new Vector2(pos.X, sepY),
            Size = new Vector2(size.X, 4),
        };
        AddNode(sep);

        // Pagination controls (ListItemB arrow style from GatheringNoteBook)
        const float paginationH = 28f;
        var pagY = pos.Y + size.Y - paginationH;

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

        if (_paginationPending && _paginationBar != null)
        {
            _paginationPending = false;
            _paginationBar.SetTotalItems(_encounters.Count, PageSize);
        }

        _paginationBar?.Update();

        if (_needsRebuild)
        {
            _needsRebuild = false;
            RebuildPanel();
        }

        // Update play/stop button labels
        foreach (var (id, btn) in _playButtons)
        {
            var isPlaying = _playingVoiceClipId == id;
            var expectedLabel = isPlaying ? Loc.S("Stop") : Loc.S("Play");
            if (btn.LabelNode.Node != null && btn.LabelNode.String.ToString() != expectedLabel)
                btn.LabelNode.String = expectedLabel;
        }

        ContinueProgressiveBuild();
    }

    public void ShowEncounters(string title, List<VoiceClipEntity> encounters, string npcKey)
    {
        _encounters = encounters;
        _npcKey = npcKey;
        _characterId = encounters.Count > 0 ? encounters[0].CharacterId : 0;
        _audioExistsCache.Clear();
        if (_paginationBar != null)
            _paginationBar.SetTotalItems(encounters.Count, PageSize);
        else
            _paginationPending = true;

        if (_titleLabel != null)
            _titleLabel.String = title;

        _needsRebuild = true;

        if (!IsOpen)
            Toggle();
    }

    private void RebuildPanel()
    {
        if (_panel == null) return;
        _panel.Clear();
        _audioExistsCache.Clear();
        _playButtons.Clear();

        var pageStart = _paginationBar!.CurrentPage * PageSize;
        var pageEnd = Math.Min(pageStart + PageSize, _encounters.Count);
        var pageCount = pageEnd - pageStart;

        _progressiveIndex = 0;
        _progressiveActive = pageCount > 0;

        if (pageCount == 0)
        {
            _panel.AddNode(Label(Loc.S("No voice clips found."), _contentWidth));
            _panel.RecalculateLayout();
        }
    }

    private void ContinueProgressiveBuild()
    {
        if (!_progressiveActive || _panel == null) return;

        var w = _contentWidth - 16; // scrollbar
        var pageStart = _paginationBar!.CurrentPage * PageSize;
        var pageEnd = Math.Min(pageStart + PageSize, _encounters.Count);
        var pageCount = pageEnd - pageStart;

        var end = Math.Min(_progressiveIndex + RowsPerFrame, pageCount);

        for (var i = _progressiveIndex; i < end; i++)
        {
            var enc = _encounters[pageStart + i];
            _panel.AddNode(BuildEncounterRow(enc, w));
        }

        _progressiveIndex = end;
        _panel.RecalculateLayout();

        if (_progressiveIndex >= pageCount)
            _progressiveActive = false;
    }

    private HorizontalListNode BuildEncounterRow(VoiceClipEntity enc, float w)
    {
        var hasSaved = GetAudioExists(enc);
        var capturedEnc = enc;
        var capturedKey = _npcKey;

        // Create text node first to measure wrapped height
        var textNode = new TextNode
        {
            Size = new Vector2(_colText, 18),
            String = enc.OriginalText,
            FontType = FontType.Axis,
            FontSize = 12,
        };
        textNode.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
        var textHeight = textNode.GetTextDrawSize(false).Y;
        var rowHeight = Math.Max(26f, textHeight + 8);

        var row = new HorizontalListNode { Size = new Vector2(w, rowHeight), ItemSpacing = 4 };

        // Play/Stop/Generate toggle button
        var playLabel = hasSaved ? Loc.S("Play") : Loc.S("Generate");
        var encId = capturedEnc.Id;
        TextButtonNode? playBtn = null;
        playBtn = Button(playLabel, _colPlay, () =>
        {
            if (_playingVoiceClipId == encId)
            {
                _voiceClipManager.StopPlayback();
                _playingVoiceClipId = null;
                return;
            }

            _playingVoiceClipId = encId;
            if (GetAudioExists(capturedEnc))
                _voiceClipManager.PlayEncounter(capturedEnc);
            else
            {
                _voiceClipManager.GenerateForEncounter(capturedEnc).ContinueWith(t =>
                {
                    if (t.Result)
                        _voiceClipManager.PlayEncounter(capturedEnc);
                    else
                        _playingVoiceClipId = null;
                    _audioExistsCache.Clear();
                    _needsRebuild = true;
                });
            }
        });
        _playButtons[encId] = playBtn;
        row.AddNode(playBtn);

        // Source
        row.AddNode(Label(((TextSource)enc.TextSource).ToString(), _colSource));

        // Timestamp
        row.AddNode(Label(enc.Timestamp.ToLocalTime().ToString("MM/dd HH:mm"), ColTimestamp));

        // Text (word wrapped, dynamic height)
        textNode.Size = new Vector2(_colText, Math.Max(18, textHeight));
        row.AddNode(textNode);

        // Delete
        if (hasSaved)
        {
            row.AddNode(Button(Loc.S("Del"), _colDel, () =>
            {
                _audioPlayback.ClearQueue(TextSource.VoiceTest);
                _playingVoiceClipId = null;
                _voiceClipManager.DeleteAudioForEncounter(capturedEnc);
                _audioExistsCache.Clear();
                _needsRebuild = true;
            }));
        }

        return row;
    }

    private bool GetAudioExists(VoiceClipEntity enc)
    {
        if (_audioExistsCache.TryGetValue(enc.Id, out var exists))
            return exists;
        exists = _voiceClipManager.HasLocalAudio(enc);
        _audioExistsCache[enc.Id] = exists;
        return exists;
    }

    // ── Helpers ──────────────────────────────────────────────

    private static TextNode Label(string text, float width) => new()
    {
        Size = new Vector2(width, 18),
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
