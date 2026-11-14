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
using KamiToolKit.Nodes;

using static Echokraut.Windows.Native.NativeNodeFactory;

namespace Echokraut.Windows.Native;

/// <summary>
/// Backend tab › "Game voice reference" (plan <c>docs/plans/game-voice-reference.md</c> §5, F5, §6):
/// opt-in switch with dialog, "rank on conversation start", the remote hint, the live
/// <see cref="IGameVoiceReferenceService.Availability"/>, the export/sync note and the
/// "Build index" batch. The switch only ever turns on through the confirmed dialog.
/// </summary>
public sealed unsafe partial class NativeConfigWindow
{
    private CheckboxNode? _gvEnableCheck;
    private CheckboxNode? _gvRankCheck;
    private TextNode? _gvRemoteHint;
    private TextNode? _gvStatusLabel;
    private TextButtonNode? _gvBuildButton;
    private StatusProgressBar? _gvProgress;
    private NativeConfirmWindow? _gvConfirmWindow;

    private string _gvStatusText = "";
    private bool? _gvRemoteHintShown;
    private bool _gvAppliedEnabled;

    // Written by the background index build / clip delete, drained in UpdateGameVoice.
    private volatile int _gvDone;
    private volatile int _gvTotal;
    private volatile string? _gvResultText;
    private volatile bool _gvDeleteRunning;

    /// <summary>The opt-in / delete popup while open — part of the dialogue-advance hit test.</summary>
    internal KamiToolKit.BaseTypes.NativeAddon? GameVoiceConfirmWindow => _gvConfirmWindow;

    private void BuildGameVoiceSection(ScrollingListNode list, float w)
    {
        var header = Label(Loc.S("Game voice reference"), w);
        header.FontSize = 14;

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

        _gvBuildButton = Button(Loc.S("Build index"), 160, StartGameVoiceIndexBuild);
        _gvProgress = new StatusProgressBar { Size = new Vector2(w, 24) };
        _gvProgress.ActionText = Loc.S("Idle");
        _gvProgress.SetProgress(0f, string.Empty);

        list.AddNode(Separator(w));
        list.AddNode(header);
        list.AddNode(intro);
        list.AddNode(_gvEnableCheck);
        list.AddNode(_gvRemoteHint);
        list.AddNode(_gvRankCheck);
        list.AddNode(_gvStatusLabel);
        list.AddNode(exportHint);
        list.AddNode(_gvBuildButton);
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
        _gvResultText = null;
        var progress = new Progress<(int Done, int Total)>(p => { _gvDone = p.Done; _gvTotal = p.Total; });
        // (No await here: the window class is unsafe, and C# forbids await in an unsafe context.)
        Task.Run(() => _gameVoice.BuildIndexAsync(language, progress)).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion)
            {
                _gvResultText = string.Format(Loc.S("Index built: {0} NPC(s) got a game-voice reference."), t.Result);
                return;
            }
            _log.Error(nameof(StartGameVoiceIndexBuild), t.Exception?.ToString() ?? "cancelled", new EKEventId(0, TextSource.None));
            _gvResultText = Loc.S("Index build failed, see log.");
        }, TaskScheduler.Default);
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
            _settingsPanels[4]?.RecalculateLayout();
        }

        var status = Loc.S(GameVoiceConsent.AvailabilityText(_gameVoice.Availability));
        if (!string.Equals(status, _gvStatusText, StringComparison.Ordinal) && _gvStatusLabel != null)
        {
            _gvStatusText = status;
            _gvStatusLabel.String = status;
        }

        Dim(_gvBuildButton, CanBuildGameVoiceIndex());

        if (_gvProgress == null) return;
        if (_gameVoice.IsIndexBuildRunning)
        {
            var total = _gvTotal;
            var done = _gvDone;
            _gvProgress.ActionText = Loc.S("Building game-voice index...");
            _gvProgress.SetProgress(total > 0 ? (float)done / total : 0f, total > 0 ? $"{done}/{total}" : string.Empty);
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
