using System;
using System.Numerics;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Native first-time setup window with a step-by-step wizard.
/// Step 0: Welcome — choose Local/Remote/None.
/// Step 1: Configure — instance-specific settings + Back/Next.
/// Step 2: Finish — summary + I Understand button.
/// All nodes are created in OnSetup; visibility managed in OnUpdate.
/// </summary>
public sealed unsafe class NativeFirstTimeWindow : NativeAddon
{
    private readonly Configuration _config;
    private readonly IAlltalkInstanceService _alltalkInstance;
    private readonly IBackendService _backend;
    private readonly IFramework _framework;
    private readonly IBatchModeService _batchMode;
    private readonly Action _onComplete;

    private int _wizardStep;

    // Remote connection test state. The Step-1 Next button is gated on a successful
    // CheckReady against the *current* base URL; if the user edits the URL after a
    // successful test, _remoteTestUrlSnapshot stops matching _config.Alltalk.BaseUrl and
    // the user is forced to retest.
    private bool _remoteTestSucceeded;
    private string _remoteTestUrlSnapshot = string.Empty;
    private bool _remoteTestRunning;

    // ── Step 0: Welcome ──────────────────────────────────────────────────
    private TextNode? _welcomeTitle;
    private TextNode? _welcomeDesc;
    private HorizontalLineNode? _welcomeSep;
    private TextButtonNode? _choiceLocalBtn;
    private TextNode? _choiceLocalDesc;
    private TextButtonNode? _choiceRemoteBtn;
    private TextNode? _choiceRemoteDesc;
    private TextButtonNode? _choiceNoneBtn;
    private TextNode? _choiceNoneDesc;

    // ── Step 1: Configure ────────────────────────────────────────────────
    private TextButtonNode? _backButton1;
    private TextNode? _step1Title;
    private TextNode? _step1Subtitle;
    private HorizontalLineNode? _step1Sep;

    // Local instance nodes
    private NativeAlltalkBuilder.LocalInstanceNodes? _localNodes;
    private NodeBase[]? _localEssentialNodes;
    private NodeBase[]? _localAdvancedNodes;
    private NodeBase[]? _localPostAdvancedNodes;
    private TextButtonNode? _localAdvancedToggle;

    // Remote instance nodes
    private NativeAlltalkBuilder.RemoteInstanceNodes? _remoteNodes;
    private NodeBase[]? _remoteAllNodes;

    // No instance nodes
    private TextNode? _noInstanceWarning;
    private TextInputNode? _noInstancePathInput;
    private CheckboxNode? _noInstanceGdCheck;
    private TextInputNode? _noInstanceGdLinkInput;

    private TextButtonNode? _nextButton;

    // ── Step 2: Finish ───────────────────────────────────────────────────
    private TextButtonNode? _backButton2;
    private TextNode? _finishTitle;
    private TextNode? _finishDetails;
    private TextNode? _finishHelp;
    private TextButtonNode? _finishButton;

    // ── Links (always visible) ───────────────────────────────────────────
    private HorizontalListNode? _linksRow;

    // Outer list reference + last-applied visibility signature. ScrollingListNode
    // doesn't auto-shrink hidden children — without an explicit RecalculateLayout()
    // every time we toggle a node's IsVisible, hidden Step-0 nodes keep their slots
    // and Step-1 content appears pushed way down. Track the (step, instanceType)
    // tuple as the visibility signature and recompute layout only on transitions.
    private ScrollingListNode? _list;
    private (int step, AlltalkInstanceType type)? _lastVisibilitySignature;

    // Install progress bar pinned at the very top of the window. Driven by
    // IAlltalkInstanceService.CurrentInstallStatus / CurrentInstallProgress. Visible only
    // while Local install is running on Step 1; the user gets phase-level feedback
    // (Preparing → Installer → Voice samples N/M → Finalizing → Done).
    private StatusProgressBar? _installProgressBar;

    public NativeFirstTimeWindow(
        Configuration config,
        IAlltalkInstanceService alltalkInstance,
        IBackendService backend,
        IFramework framework,
        IBatchModeService batchMode,
        Action onComplete)
    {
        _config = config;
        _alltalkInstance = alltalkInstance;
        _backend = backend;
        _framework = framework;
        _batchMode = batchMode;
        _onComplete = onComplete;
    }

    protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
    {
        var pos = ContentStartPosition;
        var size = ContentSize;
        var w = size.X;

        // Reserved progress-bar strip at the top of the window. Always present (so the
        // ScrollingListNode size below stays stable across install state changes), but the
        // bar itself is hidden until an install run kicks in.
        const float progressBarHeight = 28f;
        const float progressBarGap = 4f;
        _installProgressBar = new StatusProgressBar
        {
            Position = pos,
            Size = new Vector2(size.X, progressBarHeight),
            IsVisible = false,
        };
        _installProgressBar.ActionText = string.Empty;
        _installProgressBar.SetProgress(0f, string.Empty);
        AddNode(_installProgressBar);

        var listPos = new Vector2(pos.X, pos.Y + progressBarHeight + progressBarGap);
        var listSize = new Vector2(size.X, size.Y - progressBarHeight - progressBarGap);
        var list = new ScrollingListNode
        {
            Position = listPos,
            Size = listSize,
            FitWidth = true,
            ItemSpacing = 4,
        };
        _list = list;
        _lastVisibilitySignature = null;

        // ── Step 0: Welcome ──────────────────────────────────────────────
        _welcomeTitle = Lbl(Loc.S("Welcome to Echokraut!"), w, 14);
        _welcomeDesc = Lbl(Loc.S("Choose how you want to set up text-to-speech:"), w);
        _welcomeSep = Sep(w);

        _choiceLocalBtn = Btn(Loc.S("Local TTS"), w, () =>
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.Local;
            _config.Save();
            _wizardStep = 1;
        });
        _choiceLocalDesc = Lbl(Loc.S("Runs on your GPU or CPU — best quality, requires ~20GB disk space"), w);

        _choiceRemoteBtn = Btn(Loc.S("Remote Server"), w, () =>
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.Remote;
            _config.Save();
            _wizardStep = 1;
        });
        _choiceRemoteDesc = Lbl(Loc.S("Connect to a server running Alltalk (yours or someone else's)"), w);

        _choiceNoneBtn = Btn(Loc.S("Audio Files Only"), w, () =>
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.None;
            _config.Save();
            _wizardStep = 1;
        });
        _choiceNoneDesc = Lbl(Loc.S("No generation — use pre-made audio from friends or Google Drive"), w);

        list.AddNode(_welcomeTitle);
        list.AddNode(_welcomeDesc);
        list.AddNode(_welcomeSep);
        list.AddNode(_choiceLocalBtn);
        list.AddNode(_choiceLocalDesc);
        list.AddNode(_choiceRemoteBtn);
        list.AddNode(_choiceRemoteDesc);
        list.AddNode(_choiceNoneBtn);
        list.AddNode(_choiceNoneDesc);

        // ── Step 1: Configure ────────────────────────────────────────────
        _backButton1 = Btn(Loc.S("Back"), 80, () => { _wizardStep = 0; });
        list.AddNode(_backButton1);

        // Dynamic header — title shows the chosen mode, subtitle is constant context.
        _step1Title = Lbl(string.Empty, w, 14);
        _step1Subtitle = Lbl(Loc.S("Configure your setup"), w);
        _step1Sep = Sep(w);
        list.AddNode(_step1Title);
        list.AddNode(_step1Subtitle);
        list.AddNode(_step1Sep);

        // Local instance
        _localNodes = NativeAlltalkBuilder.BuildLocalInstance(w, _config, _alltalkInstance);
        _localEssentialNodes = _localNodes.EssentialNodes;
        _localAdvancedNodes = _localNodes.AdvancedNodes;
        _localPostAdvancedNodes = _localNodes.PostAdvancedNodes;
        foreach (var n in _localEssentialNodes) list.AddNode(n);

        _localAdvancedToggle = Btn(Loc.S("[+] Advanced Options"), w, () =>
        {
            var isHidden = _localAdvancedNodes!.Length > 0 && !_localAdvancedNodes[0].IsVisible;
            foreach (var n in _localAdvancedNodes!) n.IsVisible = isHidden;
            _localAdvancedToggle!.String = isHidden ? Loc.S("[-] Advanced Options") : Loc.S("[+] Advanced Options");
            // Re-flow so the post-advanced block doesn't leave a gap when collapsed,
            // and doesn't push past the visible area when expanded.
            _list?.RecalculateLayout();
        });
        list.AddNode(_localAdvancedToggle);
        foreach (var n in _localAdvancedNodes) list.AddNode(n);
        foreach (var n in _localPostAdvancedNodes) list.AddNode(n);

        // Remote instance
        _remoteNodes = NativeAlltalkBuilder.BuildRemoteInstance(w, _config, _backend);
        _remoteAllNodes = _remoteNodes.AllNodes;
        _remoteNodes.TestConnectionButton.OnClick = () => TestConnection();
        foreach (var n in _remoteAllNodes) list.AddNode(n);

        // No instance
        _noInstanceWarning = Lbl(Loc.S("No audio will be generated. Use pre-made audio from friends or Google Drive."), w);
        _noInstancePathInput = Inp(Loc.S("Local audio directory"), w, 260, _config.LocalSaveLocation,
            v => { _config.LocalSaveLocation = v; _config.Save(); });
        _noInstanceGdCheck = Chk(Loc.S("Download from Google Drive"), w, _config.GoogleDriveDownload,
            v => { _config.GoogleDriveDownload = v; _config.Save(); });
        _noInstanceGdLinkInput = Inp(Loc.S("Google Drive share link"), w, 100, _config.GoogleDriveShareLink,
            v => { _config.GoogleDriveShareLink = v; _config.Save(); });

        list.AddNode(_noInstanceWarning);
        list.AddNode(_noInstancePathInput);
        list.AddNode(_noInstanceGdCheck);
        list.AddNode(_noInstanceGdLinkInput);

        _nextButton = Btn(Loc.S("Next"), 80, () => { _wizardStep = 2; });
        list.AddNode(_nextButton);

        // ── Step 2: Finish ───────────────────────────────────────────────
        _backButton2 = Btn(Loc.S("Back"), 80, () => { _wizardStep = 1; });
        _finishTitle = Lbl(Loc.S("You're all set!"), w, 14);
        // Multi-line summary node: rebuilt each frame in OnUpdate so its content
        // tracks Configuration / install state / remote test result live. Generous
        // height covers up to ~7 rows at 18px each.
        _finishDetails = new TextNode
        {
            Size = new Vector2(w, 140),
            String = string.Empty,
            FontType = FontType.Axis,
            FontSize = 12,
        };
        _finishDetails.AddTextFlags(TextFlags.MultiLine);
        _finishHelp = Lbl(Loc.S("Use /ek in chat to open the full configuration window."), w);
        _finishButton = Btn(Loc.S("I Understand"), 200, () =>
        {
            _config.FirstTime = false;
            _config.Save();
            _onComplete();
            Close();
        });

        list.AddNode(_backButton2);
        list.AddNode(_finishTitle);
        list.AddNode(_finishDetails);
        list.AddNode(_finishHelp);
        list.AddNode(_finishButton);
        list.AddNode(Sep(w));

        // ── Links ────────────────────────────────────────────────────────
        _linksRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        var discordBtn = Btn(Loc.S("Join discord server"), 160, () => CMDHelper.OpenUrl(Constants.DISCORDURL));
        var githubBtn = Btn(Loc.S("Alltalk Github"), 120, () => CMDHelper.OpenUrl(Constants.ALLTALKGITHUBURL));
        _linksRow.AddNode(discordBtn);
        _linksRow.AddNode(githubBtn);
        list.AddNode(_linksRow);

        AddNode(list);
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        ScreenClampHelper.ClampToScreen(addon, Size);

        var step = _wizardStep;
        var instanceType = _config.Alltalk.InstanceType;
        var isLocal = instanceType == AlltalkInstanceType.Local;
        var isRemote = instanceType == AlltalkInstanceType.Remote;
        var isNone = instanceType == AlltalkInstanceType.None;
        // FTU is also reachable via /ekfirst after a batch op started (e.g. user opens it
        // mid-harvest to reconfigure). Lock the choice / install / test buttons in that
        // case — same reasoning as the main config window's mode switcher.
        var batchActive = _batchMode.IsActive;

        // ── Step 0 visibility ────────────────────────────────────────────
        var s0 = step == 0;
        SetVisible(_welcomeTitle, s0);
        SetVisible(_welcomeDesc, s0);
        SetVisible(_welcomeSep, s0);
        SetVisible(_choiceLocalBtn, s0);
        SetVisible(_choiceLocalDesc, s0);
        SetVisible(_choiceRemoteBtn, s0);
        SetVisible(_choiceRemoteDesc, s0);
        SetVisible(_choiceNoneBtn, s0);
        SetVisible(_choiceNoneDesc, s0);

        // ── Step 1 visibility ────────────────────────────────────────────
        var s1 = step == 1;
        SetVisible(_backButton1, s1);
        SetVisible(_step1Title, s1);
        SetVisible(_step1Subtitle, s1);
        SetVisible(_step1Sep, s1);
        if (s1 && _step1Title != null)
        {
            _step1Title.String = isLocal ? Loc.S("Local TTS")
                : isRemote ? Loc.S("Remote Server")
                : Loc.S("Audio Files Only");
        }

        // Local nodes
        if (_localEssentialNodes != null)
            foreach (var n in _localEssentialNodes) SetVisible(n, s1 && isLocal);
        SetVisible(_localAdvancedToggle, s1 && isLocal);
        if (!s1 || !isLocal)
        {
            if (_localAdvancedNodes != null)
                foreach (var n in _localAdvancedNodes) SetVisible(n, false);
        }
        if (_localPostAdvancedNodes != null)
            foreach (var n in _localPostAdvancedNodes) SetVisible(n, s1 && isLocal);
        if (s1 && isLocal) _localNodes?.Update(_config, _alltalkInstance, batchActive);

        // Remote nodes
        if (_remoteAllNodes != null)
            foreach (var n in _remoteAllNodes) SetVisible(n, s1 && isRemote);
        if (s1 && isRemote && _remoteNodes != null)
        {
            Dim(_remoteNodes.BaseUrlInput,         !batchActive);
            Dim(_remoteNodes.TestConnectionButton, !batchActive);
        }

        // No instance nodes
        SetVisible(_noInstanceWarning, s1 && isNone);
        SetVisible(_noInstancePathInput, s1 && isNone);
        SetVisible(_noInstanceGdCheck, s1 && isNone);
        SetVisible(_noInstanceGdLinkInput, s1 && isNone);
        Dim(_noInstancePathInput, !batchActive);
        Dim(_noInstanceGdCheck,   !batchActive);
        Dim(_noInstanceGdLinkInput, _config.GoogleDriveDownload && !batchActive);

        // Step-0 mode-choice buttons also lock during batch — picking a new mode mid-batch
        // would change which backend any in-flight backend call talks to.
        Dim(_choiceLocalBtn,  !batchActive);
        Dim(_choiceRemoteBtn, !batchActive);
        Dim(_choiceNoneBtn,   !batchActive);

        // Next button — gated per mode:
        //   Local : LocalInstall must be true (install completed)
        //   Remote: a successful CheckReady against the *current* BaseUrl
        //   None  : always allowed
        // Batch lock also blocks Next so the user can't advance the wizard while the
        // backend they're configuring is being touched by a running op.
        SetVisible(_nextButton, s1);
        var remoteUrlMatches = string.Equals(_remoteTestUrlSnapshot, _config.Alltalk.BaseUrl, StringComparison.Ordinal);
        var canNext = !batchActive && ((isLocal && _config.Alltalk.LocalInstall)
            || (isRemote && _remoteTestSucceeded && remoteUrlMatches)
            || isNone);
        Dim(_nextButton, canNext);

        // ── Step 2 visibility ────────────────────────────────────────────
        var s2 = step == 2;
        SetVisible(_backButton2, s2);
        SetVisible(_finishTitle, s2);
        SetVisible(_finishDetails, s2);
        SetVisible(_finishHelp, s2);
        SetVisible(_finishButton, s2);

        if (s2 && _finishDetails != null)
            _finishDetails.String = BuildFinishSummary(instanceType);

        // Re-flow the ScrollingListNode whenever the visible block changes. Hidden nodes
        // otherwise hold their slots (game-side layout cache), so e.g. clicking "Local"
        // on Step 0 → Step 1 leaves the Local install controls floating below the now-
        // invisible Step-0 buttons. Signature on (step, instanceType) is enough — the
        // advanced-options accordion has its own toggle that can fire RecalculateLayout
        // independently if we ever need it.
        var sig = (step, instanceType);
        if (_lastVisibilitySignature != sig)
        {
            _lastVisibilitySignature = sig;
            _list?.RecalculateLayout();
        }

        // Install progress bar — visible while a Local install is running OR briefly
        // showing the terminal status afterwards. The bar lives outside the scrolling list
        // (pinned to the top), so toggling its visibility doesn't perturb the list layout.
        if (_installProgressBar != null)
        {
            var installing = _alltalkInstance.Installing;
            var hasTerminalStatus = !string.IsNullOrEmpty(_alltalkInstance.CurrentInstallStatus);
            var showBar = s1 && isLocal && (installing || hasTerminalStatus);
            _installProgressBar.IsVisible = showBar;
            if (showBar)
            {
                _installProgressBar.ActionText = _alltalkInstance.CurrentInstallStatus;
                _installProgressBar.SetProgress(
                    Math.Clamp(_alltalkInstance.CurrentInstallProgress, 0f, 1f),
                    string.Empty);
            }
        }
    }

    private void TestConnection()
    {
        if (_remoteNodes == null || _remoteTestRunning) return;

        _remoteTestRunning = true;
        // Tentatively invalidate prior result — even before the request starts, the
        // user has signalled they want a fresh verdict.
        _remoteTestSucceeded = false;
        var urlAtStart = _config.Alltalk.BaseUrl;
        _remoteNodes.ConnectionResultLabel.String = Loc.S("Testing...");

        var task = _config.BackendSelection == TTSBackends.Alltalk
            ? _backend.CheckReady(new EKEventId(0, TextSource.None))
            : System.Threading.Tasks.Task.FromResult(Loc.S("No backend selected"));

        // ContinueWith fires on a thread-pool thread; bounce to the framework thread
        // before touching ATK nodes (label string + KamiToolKit node state aren't
        // thread-safe — they must be mutated on the main game thread).
        task.ContinueWith(t => _framework.RunOnFrameworkThread(() =>
        {
            _remoteTestRunning = false;
            if (_remoteNodes == null) return;

            if (t.IsFaulted)
            {
                _remoteTestSucceeded = false;
                _remoteTestUrlSnapshot = string.Empty;
                _remoteNodes.ConnectionResultLabel.String =
                    string.Format(Loc.S("Error: {0}"), t.Exception?.InnerException?.Message ?? string.Empty);
                return;
            }

            // CheckReady returns AllTalk's /api/ready body on success ("Ready") and a
            // human-readable error message on failure (see AlltalkBackend.CheckReady).
            // "Ready" is the only success token; everything else is treated as failure.
            var result = t.Result ?? string.Empty;
            var success = string.Equals(result.Trim(), "Ready", StringComparison.OrdinalIgnoreCase);
            _remoteTestSucceeded = success;
            _remoteTestUrlSnapshot = success ? urlAtStart : string.Empty;
            _remoteNodes.ConnectionResultLabel.String = success
                ? Loc.S("Connection successful")
                : string.Format(Loc.S("Error: {0}"), result);
        }));
    }

    /// <summary>
    /// Builds the localized multi-line summary shown on Step 2. Lines are separated
    /// by '\n'; the TextNode has TextFlags.MultiLine set in OnSetup.
    /// </summary>
    private string BuildFinishSummary(AlltalkInstanceType instanceType)
    {
        var modeLabel = instanceType switch
        {
            AlltalkInstanceType.Local => Loc.S("Local TTS"),
            AlltalkInstanceType.Remote => Loc.S("Remote Server"),
            _ => Loc.S("Audio Files Only"),
        };

        var lines = new System.Collections.Generic.List<string>
        {
            $"{Loc.S("Mode")}: {modeLabel}",
        };

        switch (instanceType)
        {
            case AlltalkInstanceType.Local:
                lines.Add($"{Loc.S("Install path")}: {_config.Alltalk.LocalInstallPath}");
                lines.Add($"{Loc.S("Install status")}: " +
                    (_config.Alltalk.LocalInstall ? Loc.S("Installed") : Loc.S("Not installed yet")));
                break;

            case AlltalkInstanceType.Remote:
                lines.Add($"{Loc.S("Server URL")}: {_config.Alltalk.BaseUrl}");
                var remoteOk = _remoteTestSucceeded
                    && string.Equals(_remoteTestUrlSnapshot, _config.Alltalk.BaseUrl, StringComparison.Ordinal);
                lines.Add($"{Loc.S("Connection")}: " +
                    (remoteOk ? Loc.S("Connection successful") : Loc.S("Not yet tested")));
                break;

            default: // None
                lines.Add($"{Loc.S("Audio path")}: {_config.LocalSaveLocation}");
                lines.Add($"{Loc.S("Google Drive")}: " +
                    (_config.GoogleDriveDownload
                        ? (string.IsNullOrWhiteSpace(_config.GoogleDriveShareLink) ? Loc.S("Enabled") : _config.GoogleDriveShareLink)
                        : Loc.S("Disabled")));
                break;
        }

        return string.Join("\n", lines);
    }

    private static void Dim(NodeBase? node, bool enabled)
    {
        if (node != null) node.Alpha = enabled ? 1.0f : 0.4f;
    }

    private static void SetVisible(NodeBase? node, bool visible)
    {
        if (node != null) node.IsVisible = visible;
    }

    private static TextNode Lbl(string text, float width, int fontSize = 12) => new()
    {
        Size = new Vector2(width, fontSize + 6),
        String = text,
        FontType = FontType.Axis,
        FontSize = (byte)fontSize,
    };

    private static HorizontalLineNode Sep(float width) => new()
    {
        Size = new Vector2(width, 4),
    };

    private static TextButtonNode Btn(string label, float minWidth, Action onClick)
    {
        var node = new TextButtonNode { Size = new Vector2(minWidth, 24), String = label };
        var textW = node.LabelNode.GetTextDrawSize(label).X + 36;
        if (textW > minWidth) node.Size = new Vector2(textW, 24);
        node.OnClick = onClick;
        return node;
    }

    private static CheckboxNode Chk(string label, float width, bool initial, Action<bool> onChange) => new()
    {
        Size = new Vector2(width, 24),
        String = label,
        IsChecked = initial,
        OnClick = onChange,
    };

    private static TextInputNode Inp(string placeholder, float width, int maxChars, string initial, Action<string> onComplete)
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
}
