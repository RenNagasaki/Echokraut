using System;
using System.Numerics;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Localization;
using Echokraut.Services;
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
    private readonly Action _onComplete;

    private int _wizardStep;

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
    private TextNode? _finishSummary;
    private TextNode? _finishHelp;
    private TextButtonNode? _finishButton;

    // ── Links (always visible) ───────────────────────────────────────────
    private HorizontalListNode? _linksRow;

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
        _choiceLocalDesc = Lbl(Loc.S("Runs on your GPU — best quality, requires ~20GB disk space"), w);

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
        _finishSummary = Lbl(Loc.S("You're all set!"), w, 14);
        _finishHelp = Lbl(Loc.S("Use /ek in chat to open the full configuration window."), w);
        _finishButton = Btn(Loc.S("I Understand"), 200, () =>
        {
            _config.FirstTime = false;
            _config.Save();
            _onComplete();
            Close();
        });

        list.AddNode(_backButton2);
        list.AddNode(_finishSummary);
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
        var step = _wizardStep;
        var instanceType = _config.Alltalk.InstanceType;
        var isLocal = instanceType == AlltalkInstanceType.Local;
        var isRemote = instanceType == AlltalkInstanceType.Remote;
        var isNone = instanceType == AlltalkInstanceType.None;

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
        if (s1 && isLocal) _localNodes?.Update(_config, _alltalkInstance);

        // Remote nodes
        if (_remoteAllNodes != null)
            foreach (var n in _remoteAllNodes) SetVisible(n, s1 && isRemote);

        // No instance nodes
        SetVisible(_noInstanceWarning, s1 && isNone);
        SetVisible(_noInstancePathInput, s1 && isNone);
        SetVisible(_noInstanceGdCheck, s1 && isNone);
        SetVisible(_noInstanceGdLinkInput, s1 && isNone);
        Dim(_noInstanceGdLinkInput, _config.GoogleDriveDownload);

        // Next button — dim if local but not installed
        SetVisible(_nextButton, s1);
        var canNext = isRemote || (isLocal && _config.Alltalk.LocalInstall) || isNone;
        Dim(_nextButton, canNext);

        // ── Step 2 visibility ────────────────────────────────────────────
        var s2 = step == 2;
        SetVisible(_backButton2, s2);
        SetVisible(_finishSummary, s2);
        SetVisible(_finishHelp, s2);
        SetVisible(_finishButton, s2);

        if (s2 && _finishSummary != null)
            _finishSummary.String = $"Setup mode: {instanceType}. You're all set!";
    }

    private void TestConnection()
    {
        if (_remoteNodes == null) return;
        _remoteNodes.ConnectionResultLabel.String = "Testing...";

        var task = _config.BackendSelection == TTSBackends.Alltalk
            ? _backend.CheckReady(new EKEventId(0, TextSource.None))
            : System.Threading.Tasks.Task.FromResult("No backend selected");

        task.ContinueWith(t =>
        {
            if (_remoteNodes == null) return;
            _remoteNodes.ConnectionResultLabel.String = t.IsFaulted
                ? $"Error: {t.Exception?.InnerException?.Message}"
                : $"Result: {t.Result}";
        });
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
