using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

public sealed unsafe partial class NativeConfigWindow
{
    // ── Logs fields ──────────────────────────────────────────────────────────

    // Log sub-tab sources (matches ImGui version order)
    private static readonly (string Label, TextSource Source)[] LogTabs =
    {
        ("General",    TextSource.None),
        ("Dialogue",   TextSource.AddonTalk),
        ("Battle",     TextSource.AddonBattleTalk),
        ("Chat",       TextSource.Chat),
        ("Bubbles",    TextSource.AddonBubble),
        ("Cutscene",   TextSource.AddonCutsceneSelectString),
        ("Choice",     TextSource.AddonSelectString),
        ("Backend",    TextSource.Backend),
    };

    private TabBarNode? _logsTabBar;
    private readonly ScrollingListNode?[] _logsPanels = new ScrollingListNode?[LogTabs.Length];
    private readonly bool[] _logsDirty = new bool[LogTabs.Length];

    private int _activeLogTab;

    // Filter state — persists across full panel rebuilds
    private string _logsFilterMethod  = "";
    private string _logsFilterMessage = "";
    private string _logsFilterId      = "";
    private bool _logsFilterExpanded;  // tracks collapsible open/closed state

    // Column widths
    private const float LogColTimestamp = 85f;
    private const float LogColMethod   = 120f;
    private const float LogColId       = 40f;

    private float LogColMessage => _contentWidth - LogColTimestamp - LogColMethod - LogColId - 3 * 4 - 20;

    private void SetupLogs()
    {
        var w = _contentWidth;

        _logsTabBar = new TabBarNode { Size = new Vector2(w, 32), Position = _topContentPos };

        for (var i = 0; i < LogTabs.Length; i++)
        {
            _logsPanels[i] = Panel(_innerContentPos, _innerContentSize);
            _logsDirty[i] = true;
        }

        for (var i = 0; i < LogTabs.Length; i++)
        {
            var idx = i;
            _logsTabBar.AddTab(LogTabs[i].Label, () => ShowLogPanel(idx));
        }

        _log.LogUpdated += OnLogUpdated;
    }

    private void AddLogsNodes()
    {
        AddNode(_logsTabBar!);
        foreach (var p in _logsPanels)
            if (p != null) AddNode(p);
    }

    private void ShowLogsSection(bool visible)
    {
        SetVisible(_logsTabBar, visible);
        if (visible)
        {
            ShowLogPanel(_activeLogTab);
        }
        else
        {
            foreach (var p in _logsPanels)
                SetVisible(p, false);
        }
    }

    private void ShowLogPanel(int index)
    {
        _activeLogTab = index;
        for (var i = 0; i < _logsPanels.Length; i++)
            SetVisible(_logsPanels[i], i == index);

        _logsDirty[index] = true;
    }

    private void OnLogUpdated(TextSource source)
    {
        for (var i = 0; i < LogTabs.Length; i++)
        {
            if (LogTabs[i].Source == source)
                _logsDirty[i] = true;
        }
    }

    private void UpdateLogs()
    {
        if (_activeTopTab != 3) return;

        _log.UpdateMainThreadLogs();

        if (_logsDirty[_activeLogTab])
        {
            _logsDirty[_activeLogTab] = false;
            RebuildLogPanel(_activeLogTab);
        }
    }

    /// <summary>
    /// Full rebuild of a log panel: options section, column headers, filter row, data rows.
    /// Filter input values persist in fields so they survive rebuilds.
    /// </summary>
    private void RebuildLogPanel(int index)
    {
        var panel = _logsPanels[index];
        if (panel == null) return;

        var w = _contentWidth;
        var source = LogTabs[index].Source;

        panel.Clear();

        // ── Filter options (collapsible) ─────────────────────────────────
        var showDebug = GetShowDebug(source);
        var showError = GetShowError(source);
        var showId0 = GetShowId0(source);

        var debugCheck = Check(Loc.S("Show debug logs"), w, showDebug, v =>
        {
            SetShowDebug(source, v);
            _config.Save();
            _logsDirty[index] = true;
        });

        var errorCheck = Check(Loc.S("Show error logs"), w, showError, v =>
        {
            SetShowError(source, v);
            _config.Save();
            _logsDirty[index] = true;
        });

        var clearButton = Button(Loc.S("Clear logs"), 100, () =>
        {
            _log.ClearLogs(source);
            _logsDirty[index] = true;
        });
        var clearRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        clearRow.AddNode(clearButton);

        var id0Check = Check(Loc.S("Show ID 0 entries"), w, showId0, v =>
        {
            SetShowId0(source, v);
            _config.Save();
            _logsDirty[index] = true;
        });

        var jumpToBottom = GetJumpToBottom(source);
        var jumpCheck = Check(Loc.S("Always jump to bottom"), w, jumpToBottom, v =>
        {
            SetJumpToBottom(source, v);
            _config.Save();
        });

        var filterContent = new NodeBase[] { debugCheck, errorCheck, id0Check, jumpCheck, clearRow };
        var filterToggle = CreateCollapsibleSection(panel, Loc.S("Filter Options"), w, !_logsFilterExpanded, filterContent);

        // Track expand/collapse state across rebuilds
        var prevOnClick = filterToggle.OnClick;
        filterToggle.OnClick = () =>
        {
            prevOnClick?.Invoke();
            _logsFilterExpanded = filterContent.Length > 0 && filterContent[0].IsVisible;
        };

        // ── Column headers ───────────────────────────────────────────────
        var headerRow = new HorizontalListNode { Size = new Vector2(w, 20), ItemSpacing = 4 };
        headerRow.AddNode(Label(Loc.S("Timestamp"), LogColTimestamp));
        headerRow.AddNode(Label(Loc.S("Method"), LogColMethod));
        headerRow.AddNode(Label(Loc.S("Message"), LogColMessage));
        headerRow.AddNode(Label(Loc.S("ID"), LogColId));
        panel.AddNode(headerRow);

        // ── Filter inputs ────────────────────────────────────────────────
        var filterRow = new HorizontalListNode { Size = new Vector2(w, 28), ItemSpacing = 4 };
        filterRow.AddNode(Spacer(LogColTimestamp, 28));
        filterRow.AddNode(Input(Loc.S("Filter"), LogColMethod, 40, _logsFilterMethod, v =>
        {
            _logsFilterMethod = v;
            _logsDirty[_activeLogTab] = true;
        }));
        filterRow.AddNode(Input(Loc.S("Filter"), LogColMessage, 80, _logsFilterMessage, v =>
        {
            _logsFilterMessage = v;
            _logsDirty[_activeLogTab] = true;
        }));
        filterRow.AddNode(Input(Loc.S("Filter"), LogColId, 10, _logsFilterId, v =>
        {
            _logsFilterId = v;
            _logsDirty[_activeLogTab] = true;
        }));
        panel.AddNode(filterRow);

        panel.AddNode(Separator(w));

        // ── Data rows ────────────────────────────────────────────────────
        IEnumerable<LogMessage> filtered = _log.GetLogsForSource(source);

        // Visibility filters
        if (!showDebug) filtered = filtered.Where(log => log.type != LogType.Debug);
        if (!showError) filtered = filtered.Where(log => log.type != LogType.Error);
        if (!showId0) filtered = filtered.Where(log => log.eventId?.Id != 0);

        // Text filters
        if (!string.IsNullOrEmpty(_logsFilterMethod))
            filtered = filtered.Where(log => log.method.Contains(_logsFilterMethod, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_logsFilterMessage))
            filtered = filtered.Where(log => log.message.Contains(_logsFilterMessage, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_logsFilterId))
            filtered = filtered.Where(log => log.eventId != null && log.eventId.Id.ToString().Contains(_logsFilterId));

        var list = filtered.OrderBy(log => log.timeStamp).ToList();
        var toShow = list.Count > 200 ? list.Skip(list.Count - 200).ToList() : list;

        foreach (var log in toShow)
        {
            var hasColor = log.color != Vector4.Zero;

            var methodLabel = Label(log.method, LogColMethod);
            methodLabel.FontSize = 11;
            methodLabel.AddTextFlags(TextFlags.WordWrap, TextFlags.MultiLine);
            methodLabel.Size = new Vector2(LogColMethod, 14);
            if (hasColor) methodLabel.TextColor = log.color;

            var msgLabel = Label(log.message, LogColMessage);
            msgLabel.FontSize = 11;
            msgLabel.AddTextFlags(TextFlags.WordWrap, TextFlags.MultiLine);
            msgLabel.Size = new Vector2(LogColMessage, 14);
            if (hasColor) msgLabel.TextColor = log.color;

            // Measure wrapped text height to size the row correctly
            var methodH = methodLabel.GetTextDrawSize(false).Y;
            var msgH = msgLabel.GetTextDrawSize(false).Y;
            var rowH = Math.Max(16f, Math.Max(methodH, msgH) + 2);

            methodLabel.Size = new Vector2(LogColMethod, rowH);
            msgLabel.Size = new Vector2(LogColMessage, rowH);

            var row = new HorizontalListNode { Size = new Vector2(w, rowH), ItemSpacing = 4 };

            var tsLabel = Label(log.timeStamp.ToString("HH:mm:ss"), LogColTimestamp);
            tsLabel.FontSize = 11;
            tsLabel.Size = new Vector2(LogColTimestamp, rowH);
            if (hasColor) tsLabel.TextColor = log.color;
            row.AddNode(tsLabel);

            row.AddNode(methodLabel);
            row.AddNode(msgLabel);

            var idLabel = Label(log.eventId?.Id.ToString() ?? "", LogColId);
            idLabel.FontSize = 11;
            idLabel.Size = new Vector2(LogColId, rowH);
            if (hasColor) idLabel.TextColor = log.color;
            row.AddNode(idLabel);

            panel.AddNode(row);
        }

        if (toShow.Count == 0)
            panel.AddNode(Label(Loc.S("No log entries."), w));

        panel.RecalculateLayout();

        if (GetJumpToBottom(source))
            panel.ScrollPosition = int.MaxValue;
    }

    // ── LogConfig accessor helpers ───────────────────────────────────────────

    private bool GetShowDebug(TextSource source) => source switch
    {
        TextSource.None                    => _config.logConfig.ShowGeneralDebugLog,
        TextSource.Chat                    => _config.logConfig.ShowChatDebugLog,
        TextSource.AddonTalk               => _config.logConfig.ShowTalkDebugLog,
        TextSource.AddonBattleTalk         => _config.logConfig.ShowBattleTalkDebugLog,
        TextSource.AddonBubble             => _config.logConfig.ShowBubbleDebugLog,
        TextSource.AddonCutsceneSelectString => _config.logConfig.ShowCutsceneSelectStringDebugLog,
        TextSource.AddonSelectString       => _config.logConfig.ShowSelectStringDebugLog,
        TextSource.Backend                 => _config.logConfig.ShowBackendDebugLog,
        _ => true,
    };

    private void SetShowDebug(TextSource source, bool value)
    {
        switch (source)
        {
            case TextSource.None:                    _config.logConfig.ShowGeneralDebugLog = value; break;
            case TextSource.Chat:                    _config.logConfig.ShowChatDebugLog = value; break;
            case TextSource.AddonTalk:               _config.logConfig.ShowTalkDebugLog = value; break;
            case TextSource.AddonBattleTalk:         _config.logConfig.ShowBattleTalkDebugLog = value; break;
            case TextSource.AddonBubble:             _config.logConfig.ShowBubbleDebugLog = value; break;
            case TextSource.AddonCutsceneSelectString: _config.logConfig.ShowCutsceneSelectStringDebugLog = value; break;
            case TextSource.AddonSelectString:       _config.logConfig.ShowSelectStringDebugLog = value; break;
            case TextSource.Backend:                 _config.logConfig.ShowBackendDebugLog = value; break;
        }
    }

    private bool GetShowError(TextSource source) => source switch
    {
        TextSource.None                    => _config.logConfig.ShowGeneralErrorLog,
        TextSource.Chat                    => _config.logConfig.ShowChatErrorLog,
        TextSource.AddonTalk               => _config.logConfig.ShowTalkErrorLog,
        TextSource.AddonBattleTalk         => _config.logConfig.ShowBattleTalkErrorLog,
        TextSource.AddonBubble             => _config.logConfig.ShowBubbleErrorLog,
        TextSource.AddonCutsceneSelectString => _config.logConfig.ShowCutsceneSelectStringErrorLog,
        TextSource.AddonSelectString       => _config.logConfig.ShowSelectStringErrorLog,
        TextSource.Backend                 => _config.logConfig.ShowBackendErrorLog,
        _ => true,
    };

    private void SetShowError(TextSource source, bool value)
    {
        switch (source)
        {
            case TextSource.None:                    _config.logConfig.ShowGeneralErrorLog = value; break;
            case TextSource.Chat:                    _config.logConfig.ShowChatErrorLog = value; break;
            case TextSource.AddonTalk:               _config.logConfig.ShowTalkErrorLog = value; break;
            case TextSource.AddonBattleTalk:         _config.logConfig.ShowBattleTalkErrorLog = value; break;
            case TextSource.AddonBubble:             _config.logConfig.ShowBubbleErrorLog = value; break;
            case TextSource.AddonCutsceneSelectString: _config.logConfig.ShowCutsceneSelectStringErrorLog = value; break;
            case TextSource.AddonSelectString:       _config.logConfig.ShowSelectStringErrorLog = value; break;
            case TextSource.Backend:                 _config.logConfig.ShowBackendErrorLog = value; break;
        }
    }

    private bool GetShowId0(TextSource source) => source switch
    {
        TextSource.None                    => _config.logConfig.ShowGeneralId0,
        TextSource.Chat                    => _config.logConfig.ShowChatId0,
        TextSource.AddonTalk               => _config.logConfig.ShowTalkId0,
        TextSource.AddonBattleTalk         => _config.logConfig.ShowBattleTalkId0,
        TextSource.AddonBubble             => _config.logConfig.ShowBubbleId0,
        TextSource.AddonCutsceneSelectString => _config.logConfig.ShowCutsceneSelectStringId0,
        TextSource.AddonSelectString       => _config.logConfig.ShowSelectStringId0,
        TextSource.Backend                 => _config.logConfig.ShowBackendId0,
        _ => true,
    };

    private void SetShowId0(TextSource source, bool value)
    {
        switch (source)
        {
            case TextSource.None:                    _config.logConfig.ShowGeneralId0 = value; break;
            case TextSource.Chat:                    _config.logConfig.ShowChatId0 = value; break;
            case TextSource.AddonTalk:               _config.logConfig.ShowTalkId0 = value; break;
            case TextSource.AddonBattleTalk:         _config.logConfig.ShowBattleTalkId0 = value; break;
            case TextSource.AddonBubble:             _config.logConfig.ShowBubbleId0 = value; break;
            case TextSource.AddonCutsceneSelectString: _config.logConfig.ShowCutsceneSelectStringId0 = value; break;
            case TextSource.AddonSelectString:       _config.logConfig.ShowSelectStringId0 = value; break;
            case TextSource.Backend:                 _config.logConfig.ShowBackendId0 = value; break;
        }
    }

    private bool GetJumpToBottom(TextSource source) => source switch
    {
        TextSource.None                    => _config.logConfig.GeneralJumpToBottom,
        TextSource.Chat                    => _config.logConfig.ChatJumpToBottom,
        TextSource.AddonTalk               => _config.logConfig.TalkJumpToBottom,
        TextSource.AddonBattleTalk         => _config.logConfig.BattleTalkJumpToBottom,
        TextSource.AddonBubble             => _config.logConfig.BubbleJumpToBottom,
        TextSource.AddonCutsceneSelectString => _config.logConfig.CutsceneSelectStringJumpToBottom,
        TextSource.AddonSelectString       => _config.logConfig.SelectStringJumpToBottom,
        TextSource.Backend                 => _config.logConfig.BackendJumpToBottom,
        _ => true,
    };

    private void SetJumpToBottom(TextSource source, bool value)
    {
        switch (source)
        {
            case TextSource.None:                    _config.logConfig.GeneralJumpToBottom = value; break;
            case TextSource.Chat:                    _config.logConfig.ChatJumpToBottom = value; break;
            case TextSource.AddonTalk:               _config.logConfig.TalkJumpToBottom = value; break;
            case TextSource.AddonBattleTalk:         _config.logConfig.BattleTalkJumpToBottom = value; break;
            case TextSource.AddonBubble:             _config.logConfig.BubbleJumpToBottom = value; break;
            case TextSource.AddonCutsceneSelectString: _config.logConfig.CutsceneSelectStringJumpToBottom = value; break;
            case TextSource.AddonSelectString:       _config.logConfig.SelectStringJumpToBottom = value; break;
            case TextSource.Backend:                 _config.logConfig.BackendJumpToBottom = value; break;
        }
    }
}
