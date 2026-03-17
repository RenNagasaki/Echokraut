using System;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;

namespace Echokraut.Windows.Native;

/// <summary>
/// Native first-time setup window shown when the plugin is used for the first time.
/// Guides the user through Alltalk instance setup.
/// </summary>
public sealed unsafe class NativeFirstTimeWindow : NativeAddon
{
    private readonly Configuration _config;
    private readonly IAlltalkInstanceService _alltalkInstance;
    private readonly IBackendService _backend;
    private readonly Action _onComplete;

    // Instance type checkboxes
    private CheckboxNode? _localCheck;
    private CheckboxNode? _remoteCheck;
    private CheckboxNode? _noInstanceCheck;

    // Local section nodes
    private TextInputNode? _localInstallPathInput;
    private CheckboxNode? _isWindows11Check;
    private CheckboxNode? _autoStartCheck;
    private TextButtonNode? _installButton;
    private TextButtonNode? _startButton;
    private TextButtonNode? _stopButton;
    private HorizontalListNode? _startStopRow;
    private TextNode? _localInstallLabel;
    private HorizontalLineNode? _localSep;

    // Remote section nodes
    private TextNode? _remoteLabel;
    private TextInputNode? _baseUrlInput;
    private HorizontalLineNode? _remoteSep;

    // No instance section nodes
    private TextNode? _noInstanceWarning1;
    private TextNode? _noInstanceWarning2;
    private TextNode? _noInstanceWarning3;
    private HorizontalLineNode? _noInstanceSep;

    // Finish button
    private TextButtonNode? _finishButton;

    public NativeFirstTimeWindow(
        Configuration config,
        IAlltalkInstanceService alltalkInstance,
        IBackendService backend,
        Action onComplete)
    {
        _config = config;
        _alltalkInstance = alltalkInstance;
        _backend = backend;
        _onComplete = onComplete;

        // Migrate instance type if needed
        if (_config.Alltalk.InstanceType == AlltalkInstanceType.None
            && !_config.Alltalk.LocalInstance && !_config.Alltalk.RemoteInstance)
        {
            _config.Alltalk.InstanceType = !string.IsNullOrWhiteSpace(_config.Alltalk.BaseUrl) && !_config.Alltalk.BaseUrl.Contains("127.0.0.1")
                ? AlltalkInstanceType.Remote
                : AlltalkInstanceType.Local;
        }
    }

    protected override void OnSetup(AtkUnitBase* addon)
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

        // ── Intro ────────────────────────────────────────────────────────────
        list.AddNode(Lbl("Welcome to Echokraut!", w, 14));
        list.AddNode(Lbl("This plugin gives nearly every text in the game a voice.", w));
        list.AddNode(Lbl("It uses Alltalk by erew123 for text-to-speech generation.", w));
        list.AddNode(Lbl("You can install a local instance (runs on your GPU),", w));
        list.AddNode(Lbl("connect to a remote one, or use audio files only.", w));
        list.AddNode(Sep(w));

        // ── Instance type selection ──────────────────────────────────────────
        list.AddNode(Lbl("Select your instance type:", w, 14));

        _localCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = "Local instance (runs on your GPU)",
            IsChecked = _config.Alltalk.InstanceType == AlltalkInstanceType.Local,
            OnClick = v => { if (v) SetInstanceType(AlltalkInstanceType.Local); },
        };
        _remoteCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = "Remote instance (connect to a server)",
            IsChecked = _config.Alltalk.InstanceType == AlltalkInstanceType.Remote,
            OnClick = v => { if (v) SetInstanceType(AlltalkInstanceType.Remote); },
        };
        _noInstanceCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = "No instance (audio files only, no generation)",
            IsChecked = _config.Alltalk.InstanceType == AlltalkInstanceType.None,
            OnClick = v => { if (v) SetInstanceType(AlltalkInstanceType.None); },
        };

        list.AddNode(_localCheck);
        list.AddNode(_remoteCheck);
        list.AddNode(_noInstanceCheck);
        list.AddNode(Sep(w));

        // ── Local instance section ───────────────────────────────────────────
        _localInstallLabel = Lbl("Local instance install path:", w);
        list.AddNode(_localInstallLabel);

        _localInstallPathInput = new TextInputNode
        {
            Size = new Vector2(w, 28),
            MaxCharacters = 128,
            PlaceholderString = "Install path (no spaces or dashes)",
            String = _config.Alltalk.LocalInstallPath,
        };
        _localInstallPathInput.OnInputReceived = s => { _config.Alltalk.LocalInstallPath = s.ToString(); _config.Save(); };
        list.AddNode(_localInstallPathInput);

        _isWindows11Check = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = "Is Windows 11",
            IsChecked = _config.Alltalk.IsWindows11,
            OnClick = v => { _config.Alltalk.IsWindows11 = v; _config.Save(); },
        };
        list.AddNode(_isWindows11Check);

        _installButton = new TextButtonNode { Size = new Vector2(200, 24), String = _config.Alltalk.LocalInstall ? "Reinstall" : "Install" };
        _installButton.OnClick = () =>
        {
            if (_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting)
                _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));
            _alltalkInstance.Install();
        };
        list.AddNode(_installButton);

        _autoStartCheck = new CheckboxNode
        {
            Size = new Vector2(w, 24),
            String = "Auto start local instance on plugin load",
            IsChecked = _config.Alltalk.AutoStartLocalInstance,
            OnClick = v => { _config.Alltalk.AutoStartLocalInstance = v; _config.Save(); },
        };
        list.AddNode(_autoStartCheck);

        _startButton = new TextButtonNode { Size = new Vector2(80, 24), String = "Start" };
        _startButton.OnClick = () => _alltalkInstance.StartInstance();
        _stopButton = new TextButtonNode { Size = new Vector2(80, 24), String = "Stop" };
        _stopButton.OnClick = () => _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));

        _startStopRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        _startStopRow.AddNode(_startButton);
        _startStopRow.AddNode(_stopButton);
        list.AddNode(_startStopRow);

        _localSep = Sep(w);
        list.AddNode(_localSep);

        // ── Remote instance section ──────────────────────────────────────────
        _remoteLabel = Lbl("Remote instance URL:", w);
        list.AddNode(_remoteLabel);

        _baseUrlInput = new TextInputNode
        {
            Size = new Vector2(w, 28),
            MaxCharacters = 80,
            PlaceholderString = "Alltalk base URL (e.g. http://192.168.1.100:7851)",
            String = _config.Alltalk.BaseUrl,
        };
        _baseUrlInput.OnInputReceived = s => { _config.Alltalk.BaseUrl = s.ToString(); _config.Save(); };
        list.AddNode(_baseUrlInput);

        _remoteSep = Sep(w);
        list.AddNode(_remoteSep);

        // ── No instance warning ──────────────────────────────────────────────
        _noInstanceWarning1 = Lbl("WARNING: Selecting 'No Instance' means no audio will be generated.", w);
        _noInstanceWarning2 = Lbl("You will need to get audio files from a friend or via Google Drive Share.", w);
        _noInstanceWarning3 = Lbl("Only use this if you are unable to use Alltalk at all.", w);
        list.AddNode(_noInstanceWarning1);
        list.AddNode(_noInstanceWarning2);
        list.AddNode(_noInstanceWarning3);

        _noInstanceSep = Sep(w);
        list.AddNode(_noInstanceSep);

        // ── Finish ───────────────────────────────────────────────────────────
        list.AddNode(Lbl("Once ready, press the button below to start using Echokraut.", w));
        list.AddNode(Lbl("Use /ek in chat to open the full configuration window.", w));

        _finishButton = new TextButtonNode { Size = new Vector2(200, 28), String = "I Understand" };
        _finishButton.OnClick = () =>
        {
            _config.FirstTime = false;
            _config.Save();
            _onComplete();
            Close();
        };
        list.AddNode(_finishButton);

        list.AddNode(Sep(w));

        // Links
        var linksRow = new HorizontalListNode { Size = new Vector2(w, 26), ItemSpacing = 4 };
        var discordBtn = new TextButtonNode { Size = new Vector2(160, 24), String = "Join discord server" };
        discordBtn.OnClick = () => CMDHelper.OpenUrl(Constants.DISCORDURL);
        var githubBtn = new TextButtonNode { Size = new Vector2(120, 24), String = "Alltalk Github" };
        githubBtn.OnClick = () => CMDHelper.OpenUrl(Constants.ALLTALKGITHUBURL);
        linksRow.AddNode(discordBtn);
        linksRow.AddNode(githubBtn);
        list.AddNode(linksRow);

        AddNode(list);
    }

    protected override void OnUpdate(AtkUnitBase* addon)
    {
        var instanceType = _config.Alltalk.InstanceType;
        var isLocal  = instanceType == AlltalkInstanceType.Local;
        var isRemote = instanceType == AlltalkInstanceType.Remote;
        var isNone   = instanceType == AlltalkInstanceType.None;

        // Dim already-selected instance type
        Dim(_localCheck, !isLocal);
        Dim(_remoteCheck, !isRemote);
        Dim(_noInstanceCheck, !isNone);

        // Local section visibility
        SetVisible(_localInstallLabel, isLocal);
        SetVisible(_localInstallPathInput, isLocal);
        SetVisible(_isWindows11Check, isLocal);
        SetVisible(_installButton, isLocal);
        SetVisible(_autoStartCheck, isLocal);
        SetVisible(_startStopRow, isLocal);
        SetVisible(_localSep, isLocal);

        // Remote section visibility
        SetVisible(_remoteLabel, isRemote);
        SetVisible(_baseUrlInput, isRemote);
        SetVisible(_remoteSep, isRemote);

        // No instance warning visibility
        SetVisible(_noInstanceWarning1, isNone);
        SetVisible(_noInstanceWarning2, isNone);
        SetVisible(_noInstanceWarning3, isNone);
        SetVisible(_noInstanceSep, isNone);

        // Install button state
        if (_installButton != null)
        {
            _installButton.String = _alltalkInstance.Installing
                ? "Installing..."
                : _config.Alltalk.LocalInstall ? "Reinstall" : "Install";
            Dim(_installButton, !_alltalkInstance.Installing);
        }

        // Start/stop button state
        if (_startButton != null)
        {
            _startButton.String = _alltalkInstance.InstanceStarting ? "Starting..." : _alltalkInstance.InstanceRunning ? "Running" : "Start";
            Dim(_startButton, !_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting);
        }
        if (_stopButton != null)
            Dim(_stopButton, (_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting) && !_alltalkInstance.InstanceStopping);

        // Finish button — only enabled when setup is valid
        var canFinish = isRemote || (isLocal && _config.Alltalk.LocalInstall) || isNone;
        Dim(_finishButton, !canFinish);
    }

    private void SetInstanceType(AlltalkInstanceType type)
    {
        _config.Alltalk.InstanceType = type;
        _config.Save();
        _localCheck!.IsChecked  = type == AlltalkInstanceType.Local;
        _remoteCheck!.IsChecked = type == AlltalkInstanceType.Remote;
        _noInstanceCheck!.IsChecked = type == AlltalkInstanceType.None;
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
}
