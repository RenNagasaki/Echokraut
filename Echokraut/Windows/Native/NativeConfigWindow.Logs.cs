using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
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

    private void SetupLogs()
    {
        var w = _contentWidth;

        _logsTabBar = new TabBarNode { Size = new Vector2(w, 32), Position = _topContentPos };

        for (var i = 0; i < LogTabs.Length; i++)
        {
            _logsPanels[i] = Panel(_innerContentPos, _innerContentSize);
            _logsDirty[i] = true;

            var idx = i;
            // Build initial options for each log panel
            BuildLogPanelOptions(idx);
        }

        for (var i = 0; i < LogTabs.Length; i++)
        {
            var idx = i;
            _logsTabBar.AddTab(LogTabs[i].Label, () => ShowLogPanel(idx));
        }

        // Subscribe to log updates
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
        // Mark all matching tabs as dirty
        for (var i = 0; i < LogTabs.Length; i++)
        {
            if (LogTabs[i].Source == source)
                _logsDirty[i] = true;
        }
    }

    private void UpdateLogs()
    {
        if (_activeTopTab != 3) return;

        // Update main-thread logs
        _log.UpdateMainThreadLogs();

        if (_logsDirty[_activeLogTab])
        {
            _logsDirty[_activeLogTab] = false;
            RebuildLogPanel(_activeLogTab);
        }
    }

    private void BuildLogPanelOptions(int index)
    {
        var panel = _logsPanels[index];
        if (panel == null) return;

        var w = _contentWidth;
        var source = LogTabs[index].Source;

        var showDebug = GetShowDebug(source);
        var showError = GetShowError(source);
        var showId0 = GetShowId0(source);

        var debugCheck = Check("Show debug logs", w, showDebug, v =>
        {
            SetShowDebug(source, v);
            _config.Save();
            _logsDirty[index] = true;
        });

        var errorCheck = Check("Show error logs", w, showError, v =>
        {
            SetShowError(source, v);
            _config.Save();
            _logsDirty[index] = true;
        });

        var clearButton = Button("Clear logs", 120, () =>
        {
            _log.ClearLogs(source);
            _logsDirty[index] = true;
        });

        // Build content nodes list for collapsible section
        var optionNodes = new List<NodeBase> { debugCheck, errorCheck };

        if (source != TextSource.None)
        {
            var id0Check = Check("Show Id 0 entries", w, showId0, v =>
            {
                SetShowId0(source, v);
                _config.Save();
                _logsDirty[index] = true;
            });
            optionNodes.Add(id0Check);
        }
        optionNodes.Add(clearButton);

        CreateCollapsibleSection(panel, "Filter Options", w, true, optionNodes.ToArray());
        panel.AddNode(Separator(w));
    }

    private void RebuildLogPanel(int index)
    {
        var panel = _logsPanels[index];
        if (panel == null) return;

        var w = _contentWidth;
        var source = LogTabs[index].Source;

        panel.Clear();

        // Re-add options
        BuildLogPanelOptions(index);

        var showDebug = GetShowDebug(source);
        var showError = GetShowError(source);
        var showId0 = GetShowId0(source);

        var logs = _log.GetLogsForSource(source);

        // Apply filters
        var filtered = logs.Where(log =>
        {
            if (log.type == LogType.Debug && !showDebug) return false;
            if (log.type == LogType.Error && !showError) return false;
            if (!showId0 && source != TextSource.None && log.eventId?.Id == 0) return false;
            return true;
        }).ToList();

        // Show last 200 entries max for performance
        var toShow = filtered.Count > 200 ? filtered.Skip(filtered.Count - 200).ToList() : filtered;

        foreach (var log in toShow)
        {
            var timestamp = log.timeStamp.ToString("HH:mm:ss");
            var text = $"[{timestamp}] {log.method}: {log.message}";
            var label = Label(text, w);
            label.Size = new Vector2(w, 16);
            label.FontSize = 11;
            panel.AddNode(label);
        }

        if (toShow.Count == 0)
            panel.AddNode(Label("No log entries.", w));

        panel.RecalculateLayout();
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
            case TextSource.Chat:                    _config.logConfig.ShowChatId0 = value; break;
            case TextSource.AddonTalk:               _config.logConfig.ShowTalkId0 = value; break;
            case TextSource.AddonBattleTalk:         _config.logConfig.ShowBattleTalkId0 = value; break;
            case TextSource.AddonBubble:             _config.logConfig.ShowBubbleId0 = value; break;
            case TextSource.AddonCutsceneSelectString: _config.logConfig.ShowCutsceneSelectStringId0 = value; break;
            case TextSource.AddonSelectString:       _config.logConfig.ShowSelectStringId0 = value; break;
            case TextSource.Backend:                 _config.logConfig.ShowBackendId0 = value; break;
        }
    }
}
