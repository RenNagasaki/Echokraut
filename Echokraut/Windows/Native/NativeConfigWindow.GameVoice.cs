using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Enums;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.UI.Nodes;
using KamiToolKit.UI.Nodes.ComponentNode;

using static Echokraut.Windows.Native.NativeNodeFactory;

namespace Echokraut.Windows.Native;

/// <summary>
/// Voices tab › collapsible "Game voice reference" at the top (plan <c>docs/plans/game-voice-reference.md</c> §5, F5, §6):
/// opt-in switch with dialog, "rank on conversation start", the remote hint, the live
/// <see cref="IGameVoiceReferenceService.Availability"/>, the export/sync note and the
/// "Build index" batch. The switch only ever turns on through the confirmed dialog.
/// <para>Moved out of the Backend tab (2026-11-15): with one more control there, the last
/// checkboxes/buttons of this section were no longer drawn (live, reproduced by removing one).
/// Expanded, it takes the place of the voice list; collapsed, the list is back (user decision:
/// no own tab, a collapsible area at the top of "Voices").</para>
/// </summary>
public sealed unsafe partial class NativeConfigWindow
{
    /// <summary>Height the collapsible toggle takes above the voice list.</summary>
    private const float GvToggleHeight = 28f;
    private TextButtonNode? _gvToggle;
    private ScrollingListNode? _gvPanel;
    private bool _gvExpanded;
    private DateTime _gvLastProbeRequest = DateTime.MinValue;
    private CheckboxNode? _gvEnableCheck;
    private CheckboxNode? _gvRankCheck;
    private TextNode? _gvRemoteHint;
    private TextNode? _gvStatusLabel;
    private TextButtonNode? _gvBuildButton;
    private TextButtonNode? _gvCancelButton;
    private CheckboxNode? _gvAsrCheck;
    private CheckboxNode? _gvDetectCheck;
    private TextNode? _gvOutdatedLabel;
    private StatusProgressBar? _gvProgress;
    private NativeConfirmWindow? _gvConfirmWindow;

    private string _gvStatusText = "";
    private bool? _gvRemoteHintShown;
    private bool _gvAppliedEnabled;

    // Written by the background index build / clip delete, drained in UpdateGameVoice.
    private volatile int _gvDone;
    private volatile int _gvTotal;
    private volatile string? _gvToken;
    private long _gvElapsedTicks;
    private volatile string? _gvResultText;
    private System.Threading.CancellationTokenSource? _gvCts;
    // -1 = not counted yet (counted in the background once the server is available), -2 = failed.
    private volatile int _gvOutdated = -1;
    private volatile bool _gvCounting;
    private string _gvOutdatedText = "";
    private volatile bool _gvDeleteRunning;

    /// <summary>The opt-in / delete popup while open — part of the dialogue-advance hit test.</summary>
    internal KamiToolKit.Core.Primitives.NativeAddon.NativeAddon? GameVoiceConfirmWindow => _gvConfirmWindow;

    /// <summary>Toggle + panel at the top of the Voices tab; the panel covers the voice list area.</summary>
    private void SetupGameVoicePanel()
    {
        var w = _contentWidth;
        _gvToggle = Button(GameVoiceToggleText(), w, ToggleGameVoicePanel);
        _gvToggle.Position = _topContentPos;
        _gvPanel = Panel(_topContentPos + new Vector2(0, GvToggleHeight), _topContentSize - new Vector2(0, GvToggleHeight));
        BuildGameVoiceSection(_gvPanel, w - ScrollbarWidth);
    }

    private string GameVoiceToggleText() => $"{(_gvExpanded ? "[-]" : "[+]")} {Loc.S("Game voice reference")}";

    private void ToggleGameVoicePanel()
    {
        _gvExpanded = !_gvExpanded;
        if (_gvToggle != null) _gvToggle.String = GameVoiceToggleText();
        ShowVoiceSelectionSection(_activeTopTab == 1);
        _gvPanel?.RecalculateLayout();
    }

    private void AddGameVoiceNodes()
    {
        if (_gvToggle != null) AddNode(_gvToggle);
        if (_gvPanel != null) AddNode(_gvPanel);
    }

    private void BuildGameVoiceSection(ScrollingListNode list, float w)
    {
        var intro = WrappedLabel(Loc.S("Uses voiced lines of the same NPC from your own FFXIV installation as reference for EchokrauTTS (in memory only). Off by default; the voice pack stays the default and the fallback."), w);

        _gvAppliedEnabled = _config.GameVoiceReferenceEnabled;
        _gvEnableCheck = Check(Loc.S("Use game voices as reference (EchokrauTTS)"), w,
            _config.GameVoiceReferenceEnabled, OnGameVoiceToggled);

        _gvRemoteHint = WrappedLabel(Loc.S(GameVoiceConsent.RemoteHint), w);
        _gvRemoteHint.TextColor = new Vector4(1f, 0.75f, 0.35f, 1f);

        _gvRankCheck = Check(Loc.S("Rank the current NPC when a conversation starts"), w,
            _config.GameVoiceRankOnConversationStart,
            v => { _config.GameVoiceRankOnConversationStart = v; _config.Save(); });

        _gvStatusLabel = WrappedLabel("", w);
        var exportHint = WrappedLabel(Loc.S(GameVoiceConsent.ExportHint), w);

        _gvAsrCheck = Check(Loc.S("Check the best clips with Whisper (slower, better)"), w,
            _config.GameVoiceBatchWithAsr, v => { _config.GameVoiceBatchWithAsr = v; _config.Save(); });
        _gvDetectCheck = Check(Loc.S("Also detect voice-actor changes (suggestions for the maintainers, best with XTTS)"), w,
            _config.GameVoiceBatchDetectChanges, v => { _config.GameVoiceBatchDetectChanges = v; _config.Save(); });
        _gvOutdatedLabel = WrappedLabel("", w);
        _gvBuildButton = Button(Loc.S("Build index for all voiced NPCs"), 260, StartGameVoiceIndexBuild);
        _gvCancelButton = Button(Loc.S("Cancel"), 120, () => _gvCts?.Cancel());
        _gvProgress = new StatusProgressBar { Size = new Vector2(w, 24) };
        _gvProgress.ActionText = Loc.S("Idle");
        _gvProgress.SetProgress(0f, string.Empty);

        list.AddNode(intro);
        list.AddNode(_gvEnableCheck);
        list.AddNode(_gvRemoteHint);
        list.AddNode(_gvRankCheck);
        list.AddNode(_gvStatusLabel);
        list.AddNode(exportHint);
        list.AddNode(_gvAsrCheck);
        list.AddNode(_gvDetectCheck);
        list.AddNode(_gvOutdatedLabel);
        list.AddNode(_gvBuildButton);
        list.AddNode(_gvCancelButton);
        list.AddNode(_gvProgress);
    }

    private static TextNode WrappedLabel(string text, float w)
    {
        var node = Label(text, w);
        node.AddTextFlags(TextFlags.WordWrap | TextFlags.MultiLine);
        node.Size = new Vector2(w, 36);
        return node;
    }

    private void OnGameVoiceToggled(bool on)
    {
        if (on)
        {
            if (GameVoiceConsent.NeedsDialog(_config.GameVoiceConsentVersion))
            {
                // Not on until confirmed: UpdateGameVoice puts the tick back to the config value.
                OpenGameVoiceConfirm(
                    new[] { Loc.S(GameVoiceConsent.Title) }.Concat(GameVoiceConsent.Paragraphs.Select(Loc.S)).ToArray(),
                    Loc.S(GameVoiceConsent.Accept), Loc.S(GameVoiceConsent.Decline),
                    () =>
                    {
                        _config.GameVoiceConsentVersion = GameVoiceConsent.CurrentVersion;
                        _config.GameVoiceConsentUtc = DateTime.UtcNow;
                        EnableGameVoice();
                    },
                    () => { });
                return;
            }
            EnableGameVoice();
            return;
        }

        _config.GameVoiceReferenceEnabled = false;
        _config.Save();
        _gameVoice.ClearMemory();
        _log.Info(nameof(OnGameVoiceToggled), "Game-voice reference switched off.", new EKEventId(0, TextSource.None));

        // §6: offer to delete what was generated with the game voice — "Delete" is the first,
        // suggested answer. Nothing to ask about when no such clip exists.
        if (_db.GetGameVoiceGenerations().Count == 0) return;
        OpenGameVoiceConfirm(
            new[] { Loc.S(GameVoiceConsent.DisableTitle), Loc.S(GameVoiceConsent.DisableBody) },
            Loc.S(GameVoiceConsent.DisableDelete), Loc.S(GameVoiceConsent.DisableKeep),
            DeleteGameVoiceClips,
            () => { });
    }

    private void EnableGameVoice()
    {
        _config.GameVoiceReferenceEnabled = true;
        _config.Save();
        _log.Info(nameof(EnableGameVoice), $"Game-voice reference switched on (consent v{_config.GameVoiceConsentVersion}).",
            new EKEventId(0, TextSource.None));
        // /health is an HTTP call — never on the frame thread.
        Task.Run(() => _gameVoice.RefreshAvailabilityAsync(new EKEventId(0, TextSource.None)));
    }

    private void OpenGameVoiceConfirm(string[] paragraphs, string confirm, string cancel, Action onConfirm, Action onCancel)
    {
        _gvConfirmWindow?.Dispose();
        _gvConfirmWindow = new NativeConfirmWindow(paragraphs, confirm, cancel, onConfirm, onCancel)
        {
            InternalName = "EKGameVoiceConfirm",
            Title = paragraphs[0],
            Size = new Vector2(560, 420),
        };
        _gvConfirmWindow.Open();
    }

    private void DeleteGameVoiceClips()
    {
        if (_gvDeleteRunning) return;
        _gvDeleteRunning = true;
        Task.Run(() =>
        {
            try
            {
                var removed = _voiceClipManager.DeleteGameVoiceClips();
                _gvResultText = string.Format(Loc.S("Deleted {0} clip(s) made with game voices."), removed);
            }
            catch (Exception ex)
            {
                _log.Error(nameof(DeleteGameVoiceClips), ex.ToString(), new EKEventId(0, TextSource.None));
            }
            finally
            {
                _gvDeleteRunning = false;
            }
        });
    }

    private void StartGameVoiceIndexBuild()
    {
        // Dimming does not block clicks (Windows/CLAUDE.md › "Greyed out = unusable") — so the
        // handler checks the same condition the Dim call uses.
        if (!CanBuildGameVoiceIndex()) return;
        var language = _clientState.ClientLanguage;
        _gvDone = 0;
        _gvTotal = 0;
        _gvToken = null;
        _gvResultText = null;
        _gvCts?.Dispose();
        _gvCts = new System.Threading.CancellationTokenSource();
        var ct = _gvCts.Token;
        var options = new GameVoiceBatchOptions(_config.GameVoiceBatchWithAsr, _config.GameVoiceBatchDetectChanges);
        var progress = new SyncProgress<GameVoiceBatchProgress>(p =>
        {
            _gvDone = p.Done;
            _gvTotal = p.Total;
            _gvToken = p.Token;
            System.Threading.Interlocked.Exchange(ref _gvElapsedTicks, p.Elapsed.Ticks);
        });
        // (No await here: the window class is unsafe, and C# forbids await in an unsafe context.)
        Task.Run(() => _gameVoice.BuildIndexAsync(language, options, progress, ct)).ContinueWith(t =>
        {
            _gvOutdated = -1; // count again
            if (t.Status == TaskStatus.RanToCompletion)
            {
                var r = t.Result;
                _gvResultText = r.NotRunReason != null
                    ? string.Format(Loc.S("Index not built: {0}"), r.NotRunReason)
                    : string.Format(Loc.S(r.Cancelled
                        ? "Cancelled: {0} ranked ({1} with a game voice), {2} up to date, {3} unmatched (report in the harvest folder). A new run continues."
                        : "Index built: {0} ranked ({1} with a game voice), {2} up to date, {3} unmatched (report in the harvest folder)."),
                        r.Ranked, r.WithReference, r.UpToDate, r.Unmatched);
                return;
            }
            _log.Error(nameof(StartGameVoiceIndexBuild), t.Exception?.ToString() ?? "cancelled", new EKEventId(0, TextSource.None));
            _gvResultText = Loc.S("Index build failed, see log.");
        }, TaskScheduler.Default);
    }

    /// <summary>Counts outdated speaker tokens in the background (patch hint, plan §11.2).</summary>
    private void CountOutdatedGameVoiceTokens()
    {
        if (_gvCounting || _gvOutdated >= 0 || _gameVoice.IsIndexBuildRunning) return;
        if (!_config.GameVoiceReferenceEnabled || _gameVoice.Availability != GameVoiceAvailability.Available) return;
        _gvCounting = true;
        var language = _clientState.ClientLanguage;
        // (ContinueWith, not await: C# forbids await in this unsafe class.)
        Task.Run(() => _gameVoice.CountOutdatedTokensAsync(language)).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion) _gvOutdated = t.Result;
            else
            {
                _gvOutdated = -2; // shown as nothing; counted again after the next batch run
                _log.Warning(nameof(CountOutdatedGameVoiceTokens), t.Exception?.GetBaseException().Message ?? "cancelled", new EKEventId(0, TextSource.None));
            }
            _gvCounting = false;
        }, TaskScheduler.Default);
    }

    /// <summary>IProgress that reports on the calling thread (the window reads the volatile fields per frame).</summary>
    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private bool CanBuildGameVoiceIndex()
        => _config.GameVoiceReferenceEnabled
           && _gameVoice.Availability == GameVoiceAvailability.Available
           && !_batchMode.IsActive
           && !_gvDeleteRunning;

    /// <summary>Per-frame sync; every node write is gated on a change.</summary>
    private void UpdateGameVoice()
    {
        if (_gvEnableCheck == null) return;

        var enabled = _config.GameVoiceReferenceEnabled;
        var batchActive = _batchMode.IsActive;
        // A declined dialog leaves the tick set by the click — put it back to the truth.
        if (_gvEnableCheck.IsChecked != enabled || _gvAppliedEnabled != enabled)
        {
            _gvEnableCheck.IsChecked = enabled;
            _gvAppliedEnabled = enabled;
        }
        // Switching mid-batch would change what the running index build talks to.
        Dim(_gvEnableCheck, !batchActive || _batchMode.CurrentOperation != BatchOperation.GameVoiceIndex);
        Dim(_gvRankCheck, enabled);

        var showRemote = GameVoiceConsent.ShowRemoteHint(_config.BackendSelection, _config.ActiveInstanceType);
        if (_gvRemoteHintShown != showRemote)
        {
            _gvRemoteHintShown = showRemote;
            SetVisible(_gvRemoteHint, showRemote);
            _gvPanel?.RecalculateLayout();
        }

        // Nobody asked the server yet (plugin reload, wrapper restart): ask now instead of at the
        // first spoken line — until then the build button stays disabled. Throttled, off-thread.
        if (enabled && _gameVoice.Availability == GameVoiceAvailability.Unknown
            && DateTime.UtcNow - _gvLastProbeRequest > TimeSpan.FromSeconds(10))
        {
            _gvLastProbeRequest = DateTime.UtcNow;
            Task.Run(() => _gameVoice.RefreshAvailabilityAsync(new EKEventId(0, TextSource.None)));
        }

        var status = Loc.S(GameVoiceConsent.AvailabilityText(_gameVoice.Availability));
        if (!string.Equals(status, _gvStatusText, StringComparison.Ordinal) && _gvStatusLabel != null)
        {
            _gvStatusText = status;
            _gvStatusLabel.String = status;
        }

        Dim(_gvBuildButton, CanBuildGameVoiceIndex());
        Dim(_gvCancelButton, _gameVoice.IsIndexBuildRunning);
        Dim(_gvAsrCheck, enabled && !_gameVoice.IsIndexBuildRunning);
        Dim(_gvDetectCheck, enabled && !_gameVoice.IsIndexBuildRunning);

        CountOutdatedGameVoiceTokens();
        var outdated = _gvOutdated;
        var outdatedText = outdated < 0 || !enabled ? ""
            : outdated == 0 ? Loc.S("Every voiced NPC has a current game-voice index.")
            : string.Format(Loc.S("{0} voiced speaker(s) without a current index (new game patch or first run)."), outdated);
        if (!string.Equals(outdatedText, _gvOutdatedText, StringComparison.Ordinal) && _gvOutdatedLabel != null)
        {
            _gvOutdatedText = outdatedText;
            _gvOutdatedLabel.String = outdatedText;
        }

        if (_gvProgress == null) return;
        if (_gameVoice.IsIndexBuildRunning)
        {
            var total = _gvTotal;
            var done = _gvDone;
            var elapsed = TimeSpan.FromTicks(System.Threading.Interlocked.Read(ref _gvElapsedTicks));
            var remaining = done > 0 && total > done ? TimeSpan.FromTicks(elapsed.Ticks / done * (total - done)) : (TimeSpan?)null;
            _gvProgress.ActionText = Loc.S("Building game-voice index...") + (_gvToken is { } tok ? $" ({tok})" : "");
            _gvProgress.SetProgress(total > 0 ? (float)done / total : 0f,
                total > 0 ? $"{done}/{total}" + (remaining is { } rem ? " · " + string.Format(Loc.S("{0} left"), rem.ToString(@"h\:mm\:ss")) : "") : string.Empty);
        }
        else if (_gvDeleteRunning)
        {
            _gvProgress.ActionText = Loc.S("Deleting clips made with game voices...");
        }
        else if (_gvResultText is { } result)
        {
            _gvProgress.ActionText = result;
            _gvProgress.SetProgress(_gvTotal > 0 ? 1f : 0f, string.Empty);
            _gvResultText = null;
        }
    }

    private void DisposeGameVoice()
    {
        _gvConfirmWindow?.Close();
        _gvConfirmWindow?.Dispose();
        _gvConfirmWindow = null;
    }
}
