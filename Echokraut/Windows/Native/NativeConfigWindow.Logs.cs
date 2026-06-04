using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Localization;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

public sealed unsafe partial class NativeConfigWindow
{
    // ── Logs fields ──────────────────────────────────────────────────────────

    // Log sub-tab sources (matches ImGui version order). LiveOnly tabs disappear in None
    // mode — same rule as the Settings sub-tabs: anything that has no audio-file fallback
    // (Chat, Player Choices, Cutscene Choices, Backend communication) is meaningless when
    // there's no live generation backend running.
    private static readonly (string Label, TextSource Source, bool LiveOnly)[] LogTabs =
    {
        ("General",    TextSource.None,                       false),
        ("Dialogue",   TextSource.AddonTalk,                  false),
        ("Battle",     TextSource.AddonBattleTalk,            false),
        ("Chat",       TextSource.Chat,                       true),
        ("Bubbles",    TextSource.AddonBubble,                false),
        ("Cutscene",   TextSource.AddonCutsceneSelectString,  true),
        ("Choice",     TextSource.AddonSelectString,          true),
        ("Backend",    TextSource.Backend,                    true),
    };

    private TabBarNode? _logsTabBar;
    // Live-generation snapshot for the Logs sub-tab bar — same gating pattern as the Settings
    // sub-tab bar: LiveOnly log tabs disappear in None mode and reappear when the user
    // switches back to a live backend.
    private bool? _logsTabsLiveGenSnapshot;
    private readonly ScrollingListNode?[] _logsPanels = new ScrollingListNode?[LogTabs.Length];
    private readonly bool[] _logsDirty = new bool[LogTabs.Length];

    private int _activeLogTab;

    // Filter state — persists across full panel rebuilds
    private string _logsFilterMethod  = "";
    private string _logsFilterMessage = "";
    private string _logsFilterId      = "";
    private bool _logsFilterExpanded;  // tracks collapsible open/closed state

    // Pagination state
    private const int LogsPageSize = 100;
    private const int LogsRowsPerFrame = 10;
    private readonly List<LogMessage>?[] _logsFilteredData = new List<LogMessage>?[LogTabs.Length];
    private readonly int[] _logsPage = new int[LogTabs.Length];

    // Progressive loading within current page
    private int _logsProgressiveIndex;
    private int _logsProgressiveTab = -1; // -1 = idle

    // Pagination bars per log tab
    private readonly PaginationBar?[] _logsPaginationBars = new PaginationBar?[LogTabs.Length];

    // Column widths
    private const float LogColTimestamp = 85f;
    private const float LogColMethod   = 120f;
    private const float LogColId       = 40f;

    private float LogColMessage => _contentWidth - LogColTimestamp - LogColMethod - LogColId - 3 * 4 - 20;

    private void SetupLogs()
    {
        var w = _contentWidth;
        var paginationH = 28f;

        _logsTabBar = new TabBarNode { Size = new Vector2(w, 32), Position = _topContentPos };

        for (var i = 0; i < LogTabs.Length; i++)
        {
            var panelH = _innerContentSize.Y - paginationH - 4;
            _logsPanels[i] = Panel(_innerContentPos, new Vector2(_innerContentSize.X, panelH));
            _logsDirty[i] = true;

            var tabIdx = i;
            _logsPaginationBars[i] = new PaginationBar(
                new Vector2(_innerContentPos.X, _innerContentPos.Y + panelH + 4), w,
                page =>
                {
                    _logsPage[tabIdx] = page;
                    _logsDirty[tabIdx] = true;
                });
        }

        _logsTabsLiveGenSnapshot = _config.Alltalk.HasLiveGeneration;
        BuildLogsTabs(_logsTabsLiveGenSnapshot.Value);

        _log.LogUpdated += OnLogUpdated;
    }

    /// <summary>
    /// (Re)populates the Logs sub-tab bar. LiveOnly tabs (Chat / Cutscene / Choice / Backend)
    /// are omitted in None mode — same rule as the Settings sub-tab bar. Panel + pagination
    /// arrays keep their full size so source-indexed lookups (OnLogUpdated, per-source filters)
    /// stay valid; only the visible tab buttons shrink. Order is preserved by Clear()+AddTab.
    /// </summary>
    private void BuildLogsTabs(bool liveGen)
    {
        if (_logsTabBar == null) return;
        _logsTabBar.Clear();

        for (var i = 0; i < LogTabs.Length; i++)
        {
            if (LogTabs[i].LiveOnly && !liveGen) continue;
            var idx = i;
            _logsTabBar.AddTab(LogTabs[i].Label, () => ShowLogPanel(idx));
        }

        // Restore the previously-active sub-tab when its tab still exists; otherwise snap to
        // General (index 0). Panel indices stay the same regardless of which tabs are present.
        var activeStillVisible = !LogTabs[_activeLogTab].LiveOnly || liveGen;
        var targetIndex = activeStillVisible ? _activeLogTab : 0;
        _logsTabBar.SelectTab(LogTabs[targetIndex].Label);
        // Only force-show the panel + pagination if we're actually on the Logs top section.
        // Otherwise this rebuild (triggered by a mode flip while the user is on Settings)
        // would re-show the active log panel + its pagination arrows on top of whatever
        // section is currently active, intercepting clicks. _activeLogTab is still updated
        // so a later ShowTopPanel(3) lands on the right sub-tab.
        if (_activeTopTab == 3)
            ShowLogPanel(targetIndex);
        else
            _activeLogTab = targetIndex;
    }

    private void AddLogsNodes()
    {
        AddNode(_logsTabBar!);
        foreach (var p in _logsPanels)
            if (p != null) AddNode(p);
        for (var i = 0; i < LogTabs.Length; i++)
        {
            if (_logsPaginationBars[i] != null)
                foreach (var node in _logsPaginationBars[i]!.Nodes)
                    AddNode(node);
        }
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
            for (var i = 0; i < LogTabs.Length; i++)
            {
                if (_logsPaginationBars[i] != null)
                    foreach (var node in _logsPaginationBars[i]!.Nodes)
                        SetVisible(node, false);
            }
        }
    }

    private void ShowLogPanel(int index)
    {
        _activeLogTab = index;
        for (var i = 0; i < _logsPanels.Length; i++)
        {
            SetVisible(_logsPanels[i], i == index);
            if (_logsPaginationBars[i] != null)
                foreach (var node in _logsPaginationBars[i]!.Nodes)
                    SetVisible(node, i == index);
        }

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

        // Process deferred page changes (queued by button click handlers)
        _logsPaginationBars[_activeLogTab]?.Update();

        if (_logsDirty[_activeLogTab])
        {
            _logsDirty[_activeLogTab] = false;
            RebuildLogPanel(_activeLogTab);
        }

        ContinueLogsPageBuild();
    }

    /// <summary>
    /// Full rebuild of a log panel: options section, column headers, filter row, data rows (paginated).
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
        var logCfg = _config.GetLogSource(source);

        var debugCheck = Check(Loc.S("Show debug logs"), w, logCfg.ShowDebugLog, v =>
        {
            logCfg.ShowDebugLog = v;
            _config.Save();
            _logsDirty[index] = true;
        });

        var errorCheck = Check(Loc.S("Show error logs"), w, logCfg.ShowErrorLog, v =>
        {
            logCfg.ShowErrorLog = v;
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

        var id0Check = Check(Loc.S("Show ID 0 entries"), w, logCfg.ShowId0, v =>
        {
            logCfg.ShowId0 = v;
            _config.Save();
            _logsDirty[index] = true;
        });

        var jumpCheck = Check(Loc.S("Always jump to bottom"), w, logCfg.JumpToBottom, v =>
        {
            logCfg.JumpToBottom = v;
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

        // ── Filter and cache data ────────────────────────────────────────
        IEnumerable<LogMessage> filtered = _log.GetLogsForSource(source);

        // Visibility filters
        if (!logCfg.ShowDebugLog) filtered = filtered.Where(log => log.Type != LogType.Debug);
        if (!logCfg.ShowErrorLog) filtered = filtered.Where(log => log.Type != LogType.Error);
        if (!logCfg.ShowId0) filtered = filtered.Where(log => log.EventId?.Id != 0);

        // Text filters
        if (!string.IsNullOrEmpty(_logsFilterMethod))
            filtered = filtered.Where(log => log.Method.Contains(_logsFilterMethod, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_logsFilterMessage))
            filtered = filtered.Where(log => log.Message.Contains(_logsFilterMessage, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(_logsFilterId))
            filtered = filtered.Where(log => log.EventId != null && log.EventId.Id.ToString().Contains(_logsFilterId));

        var list = filtered.OrderBy(log => log.TimeStamp).ToList();
        _logsFilteredData[index] = list;

        // Jump to last page when "jump to bottom" is enabled
        if (_config.GetLogSource(source).JumpToBottom && list.Count > 0)
            _logsPage[index] = Math.Max(0, (list.Count - 1) / LogsPageSize);

        // ── Data rows (current page, progressively loaded) ────────────
        BuildLogRows(panel, index, w);

        panel.RecalculateLayout();
    }

    private void BuildLogRows(ScrollingListNode panel, int index, float w)
    {
        var list = _logsFilteredData[index];
        if (list == null || list.Count == 0)
        {
            panel.AddNode(Label(Loc.S("No log entries."), w));
            _logsPaginationBars[index]?.SetTotalItems(0, LogsPageSize);
            _logsProgressiveTab = -1;
            return;
        }

        // Start progressive build — rows added in ContinueLogsPageBuild()
        _logsProgressiveTab = index;
        _logsProgressiveIndex = 0;
        _logsPaginationBars[index]?.SetTotalItems(list.Count, LogsPageSize);
    }

    private void ContinueLogsPageBuild()
    {
        if (_logsProgressiveTab < 0) return;

        var index = _logsProgressiveTab;
        var panel = _logsPanels[index];
        var list = _logsFilteredData[index];
        if (panel == null || list == null) { _logsProgressiveTab = -1; return; }

        var page = _logsPaginationBars[index]?.CurrentPage ?? 0;
        var pageStart = page * LogsPageSize;
        var pageEnd = Math.Min(pageStart + LogsPageSize, list.Count);
        var pageCount = pageEnd - pageStart;

        var start = _logsProgressiveIndex;
        var end = Math.Min(start + LogsRowsPerFrame, pageCount);
        var w = _contentWidth;

        for (var i = start; i < end; i++)
        {
            var log = list[pageStart + i];
            var hasColor = log.Color != Vector4.Zero;

            var methodLabel = Label(log.Method, LogColMethod);
            methodLabel.FontSize = 11;
            methodLabel.AddTextFlags(TextFlags.WordWrap, TextFlags.MultiLine);
            methodLabel.Size = new Vector2(LogColMethod, 14);
            if (hasColor) methodLabel.TextColor = log.Color;

            var msgLabel = Label(log.Message, LogColMessage);
            msgLabel.FontSize = 11;
            msgLabel.AddTextFlags(TextFlags.WordWrap, TextFlags.MultiLine);
            msgLabel.Size = new Vector2(LogColMessage, 14);
            if (hasColor) msgLabel.TextColor = log.Color;

            // Measure wrapped text height to size the row correctly
            var methodH = methodLabel.GetTextDrawSize(false).Y;
            var msgH = msgLabel.GetTextDrawSize(false).Y;
            var rowH = Math.Max(16f, Math.Max(methodH, msgH) + 2);

            methodLabel.Size = new Vector2(LogColMethod, rowH);
            msgLabel.Size = new Vector2(LogColMessage, rowH);

            var row = new HorizontalListNode { Size = new Vector2(w, rowH), ItemSpacing = 4 };

            var tsLabel = Label(log.TimeStamp.ToString("HH:mm:ss"), LogColTimestamp);
            tsLabel.FontSize = 11;
            tsLabel.Size = new Vector2(LogColTimestamp, rowH);
            if (hasColor) tsLabel.TextColor = log.Color;
            row.AddNode(tsLabel);

            row.AddNode(methodLabel);
            row.AddNode(msgLabel);

            var idLabel = Label(log.EventId?.Id.ToString() ?? "", LogColId);
            idLabel.FontSize = 11;
            idLabel.Size = new Vector2(LogColId, rowH);
            if (hasColor) idLabel.TextColor = log.Color;
            row.AddNode(idLabel);

            panel.AddNode(row);
        }

        _logsProgressiveIndex = end;
        panel.RecalculateLayout();

        if (_logsProgressiveIndex >= pageCount)
        {
            _logsProgressiveTab = -1;

            var source = LogTabs[index].Source;
            if (_config.GetLogSource(source).JumpToBottom)
                panel.ScrollPosition = int.MaxValue;
        }
    }

}
