using System;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Controllers;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using ManagedBass;
using EKConfig = Echokraut.DataClasses.Configuration;

namespace Echokraut.Windows.Native;

/// <summary>
/// Attaches Play/Pause/Stop/Mute/AutoAdvance/VoiceDropDown buttons to the Talk addon.
/// Buttons are always visible; unavailable actions are shown at reduced alpha.
/// Uses PreReceiveEvent to cancel dialogue advance when a button is clicked.
/// </summary>
public sealed unsafe class DialogTalkController : IDisposable
{
    private const float EnabledAlpha  = 1.0f;
    private const float DisabledAlpha = 0.4f;

    private readonly EKConfig _config;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly ILipSyncHelper _lipSync;
    private readonly Action _recreateInference;
    private readonly IAddonLifecycle _addonLifecycle;
    private Func<Vector2, bool>? _isInsideOwnedWindow;

    private readonly AddonController _talkController;

    private HorizontalListNode? _layout;
    private TextButtonNode?   _playPauseButton;
    private TextButtonNode?   _stopButton;
    private CheckboxNode?     _muteCheckbox;
    private CheckboxNode?     _autoAdvanceCheckbox;
    private TextDropDownNode? _voiceDropDown;

    // Advance suppression: set by OnCollapsed, consumed by the next PreReceiveEvent check.
    private bool _suppressNextAdvance;
    // Owned open/closed state — set in OnUncollapsed, cleared in OnCollapsed.
    private bool _dropDownIsOpen;
    // Pause/resume tracking: audio is paused while the dropdown is open.
    private bool _pausedForDropDown;
    private bool _dropDownWasOpen; // PostUpdate snapshot for resume-on-close detection

    private string _lastSpeakerKey = string.Empty;

    // Voice selection deferred from ATK event context to OnUpdate.
    // OptionSelectedHandler crashes at UpdateLabel (LabelNode.Node null after ReattachNode),
    // but our OnOptionSelected lambda fires first — so we capture here and process next frame.
    private string? _pendingVoiceSelection;

    public DialogTalkController(
        EKConfig config,
        IAudioPlaybackService audioPlayback,
        ILipSyncHelper lipSync,
        Action recreateInference,
        IAddonLifecycle addonLifecycle)
    {
        _config = config;
        _audioPlayback = audioPlayback;
        _lipSync = lipSync;
        _recreateInference = recreateInference;
        _addonLifecycle = addonLifecycle;

        _talkController = new AddonController("Talk");
        _talkController.OnAttach += OnAttach;
        _talkController.OnUpdate += OnUpdate;
        _talkController.OnDetach += OnDetach;
        _talkController.Enable();

        _addonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
    }

    /// <summary>
    /// Registers a hit-test callback so clicks inside owned native windows
    /// (e.g., config window) suppress Talk addon advance.
    /// </summary>
    public void SetWindowHitTest(Func<Vector2, bool> hitTest) => _isInsideOwnedWindow = hitTest;

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
        _talkController.Dispose();
        DisposeNodes();
    }

    private void DisposeNodes()
    {
        _layout?.Dispose();
        _layout = null;
        _playPauseButton = null;
        _stopButton = null;
        _muteCheckbox = null;
        _autoAdvanceCheckbox = null;
        _voiceDropDown = null;
        _lastSpeakerKey = string.Empty;
        _suppressNextAdvance = false;
        _dropDownIsOpen = false;
        _pausedForDropDown = false;
        _dropDownWasOpen = false;
        _pendingVoiceSelection = null;
    }

    private void OnAttach(AtkUnitBase* addon)
    {
        _playPauseButton = new TextButtonNode { Size = new Vector2(60, 24), String = Loc.S("Play") };
        _playPauseButton.OnClick = OnPlayPauseClick;
        // Size to fit the longest label (Play vs Pause)
        var playW = _playPauseButton.LabelNode.GetTextDrawSize(Loc.S("Play")).X;
        var pauseW = _playPauseButton.LabelNode.GetTextDrawSize(Loc.S("Pause")).X;
        _playPauseButton.Size = new Vector2(Math.Max(playW, pauseW) + 36, 24);

        _stopButton = new TextButtonNode { Size = new Vector2(60, 24), String = Loc.S("Stop") };
        _stopButton.OnClick = OnStopClick;
        _stopButton.Size = new Vector2(_stopButton.LabelNode.GetTextDrawSize(Loc.S("Stop")).X + 36, 24);

        _muteCheckbox = new CheckboxNode { Size = new Vector2(60, 24), String = Loc.S("Mute") };
        _muteCheckbox.OnClick = OnMuteClick;

        _autoAdvanceCheckbox = new CheckboxNode { Size = new Vector2(130, 24), String = Loc.S("Auto-advance") };
        _autoAdvanceCheckbox.OnClick = OnAutoAdvanceClick;

        _voiceDropDown = new TextDropDownNode { Size = new Vector2(185, 24), Options = [] };

        // OnOptionSelected fires as the first line of OptionSelectedHandler, before UpdateLabel.
        // UpdateLabel then crashes (LabelNode.Node null after Uncollapse's ReattachNode triggers
        // native destructors). HandleEvents catches the crash. We capture the selection here and
        // process it on the next PostUpdate frame where Collapse/SetText are safe to call.
        _voiceDropDown.OnOptionSelected = option => _pendingVoiceSelection = option;

        _voiceDropDown.OnUncollapsed = () =>
        {
            _dropDownIsOpen = true;
            OnDropDownOpened();
        };
        _voiceDropDown.OnCollapsed = () =>
        {
            _dropDownIsOpen = false;
            _suppressNextAdvance = true;
        };

        _layout = new HorizontalListNode
        {
            Size = new Vector2(530, 28),
            Alignment = HorizontalListAnchor.Left,
            ItemSpacing = 4,
        };
        _layout.AddNode(_playPauseButton);
        _layout.AddNode(_stopButton);
        _layout.AddNode(_muteCheckbox);
        _layout.AddNode(_autoAdvanceCheckbox);
        _layout.AddNode(_voiceDropDown);

        _layout.Position = new Vector2(56, 104);
        _layout.AttachNode(addon);
    }

    private void OnUpdate(AtkUnitBase* addon)
    {
        if (_layout is null) return;

        try
        {
            OnUpdateInner(addon);
        }
        catch
        {
            // AddonController.OnAddonEvent has no try/catch. Reset suppression flags so
            // the player can still advance dialogue even if UI logic has failed.
            _dropDownIsOpen = false;
            _suppressNextAdvance = false;
        }
    }

    private void OnUpdateInner(AtkUnitBase* addon)
    {
        // Process pending voice selection deferred from ATK event handler.
        if (_pendingVoiceSelection != null)
        {
            var selected = _pendingVoiceSelection;
            _pendingVoiceSelection = null;
            ProcessVoiceSelection(selected);
        }

        var msg = DialogState.CurrentVoiceMessage;

        var visible = _config.ShowExtraOptionsInDialogue && !DialogState.IsVoiced && _audioPlayback.InDialog;
        _layout!.IsVisible = visible;
        if (!visible) return;

        // Safety net: sync _dropDownIsOpen if dropdown physically collapsed without OnCollapsed firing.
        if (_dropDownIsOpen && _voiceDropDown is { IsCollapsed: true })
            _dropDownIsOpen = false;

        // Resume audio if the dropdown closed this frame without a voice change being made.
        if (_dropDownWasOpen && !_dropDownIsOpen && _pausedForDropDown)
        {
            if (msg != null) _audioPlayback.ResumePlaying(msg);
            _pausedForDropDown = false;
        }
        _dropDownWasOpen = _dropDownIsOpen;

        var isMuted    = IsMuted();
        var hasSpeaker = msg?.SpeakerObj != null;

        var streamState = msg != null
            ? _audioPlayback.GetStreamState(msg.StreamId)
            : PlaybackState.Stopped;
        var isStreamPlaying = streamState == PlaybackState.Playing;
        var isStreamPaused  = streamState == PlaybackState.Paused;
        var isActive        = _audioPlayback.IsPlaying || isStreamPaused;

        _playPauseButton!.String = isStreamPlaying ? Loc.S("Pause") : Loc.S("Play");
        SetEnabled(_playPauseButton, !isMuted && (isActive || !_audioPlayback.RecreationStarted));
        SetEnabled(_stopButton!, isActive && !isMuted);

        _muteCheckbox!.IsVisible = hasSpeaker;
        if (hasSpeaker)
        {
            _muteCheckbox.IsChecked = isMuted;
            SetEnabled(_muteCheckbox, true);
        }

        var showExtra = _config.ShowExtraExtraOptionsInDialogue;
        _autoAdvanceCheckbox!.IsVisible = showExtra;
        if (showExtra)
            _autoAdvanceCheckbox.IsChecked = _config.AutoAdvanceTextAfterSpeechCompleted;

        var speaker = msg?.Speaker;
        var showVoice = showExtra && speaker != null;

        // If the dropdown is hidden while open (speaker gone, extra options toggled off),
        // collapse it so the option list isn't left floating over the addon.
        if (!showVoice && _dropDownIsOpen)
            _voiceDropDown?.Collapse(false);

        _voiceDropDown!.IsVisible = showVoice;
        if (showVoice)
        {
            var speakerKey = speaker!.ToString();
            if (speakerKey != _lastSpeakerKey)
            {
                _lastSpeakerKey = speakerKey;
                var voices = _config.EchokrautVoices
                    .FindAll(f => f.IsSelectable(speaker.Name, speaker.Gender, speaker.Race, speaker.IsChild));
                var voiceNames    = voices.ConvertAll(v => v.VoiceName);
                var selectedVoice = speaker.Voice?.VoiceName ?? string.Empty;
                // Bypass TextDropDownNode.Options and DropDownNode.SelectedOption setters —
                // both call UpdateLabel → LabelNode.Node->SetText which crashes when LabelNode.Node is null.
                _voiceDropDown.OptionListNode.Options = voiceNames;
                _voiceDropDown.OptionListNode.SelectedOption = selectedVoice;
                if (_voiceDropDown.LabelNode.Node != null)
                    _voiceDropDown.LabelNode.String = selectedVoice;
            }
        }

        _layout.RecalculateLayout();
    }

    /// <summary>
    /// Processes a voice selection queued from the ATK option-click event.
    /// Must be called from OnUpdate (PostUpdate) — NOT from inside an ATK event handler.
    /// </summary>
    private void ProcessVoiceSelection(string voiceName)
    {
        // Apply voice change FIRST — pure config/service calls, no native node access.
        // This must happen before Collapse because Collapse (via ReattachNode) can crash,
        // and if the outer OnUpdate catch fires the voice change code would never run.
        var msg = DialogState.CurrentVoiceMessage;
        if (msg?.Speaker != null)
        {
            var voices = _config.EchokrautVoices
                .FindAll(f => f.IsSelectable(msg.Speaker.Name, msg.Speaker.Gender, msg.Speaker.Race, msg.Speaker.IsChild));
            var newVoice = voices.Find(v => v.VoiceName == voiceName);

            if (newVoice != null && newVoice != msg.Speaker.Voice)
            {
                // Voice changed: suppress audio resume so the new voice regenerates instead.
                _pausedForDropDown = false;

                msg.Speaker.Voice = newVoice;
                msg.Speaker.DoNotDelete = true;
                msg.Speaker.RefreshSelectable();
                _config.Save();

                // Force dropdown label refresh on next frame.
                _lastSpeakerKey = string.Empty;

                _lipSync.TryStopLipSync(msg);
                _audioPlayback.StopPlaying(msg);
                _recreateInference();
            }
            // No change: leave _pausedForDropDown as-is so the resume logic in OnUpdateInner fires.
        }

        // Collapse the dropdown — bypass DropDownNode.SelectedOption setter to avoid UpdateLabel crash.
        // Wrapped in try/catch: if ReattachNode crashes, flags are reset so dialogue can still advance.
        try
        {
            if (_voiceDropDown != null && !_voiceDropDown.IsCollapsed)
            {
                _voiceDropDown.OptionListNode.SelectedOption = voiceName;
                if (_voiceDropDown.LabelNode.Node != null)
                    _voiceDropDown.LabelNode.String = voiceName;
                _voiceDropDown.Collapse(false);      // fires OnCollapsed
                _suppressNextAdvance = false;        // deferred context has no pending click to suppress
            }
        }
        catch
        {
            _dropDownIsOpen = false;
            _suppressNextAdvance = false;
        }
    }

    private void OnDetach(AtkUnitBase* addon) => DisposeNodes();

    private void OnPreReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (_layout is null || !_layout.IsVisible) return;
        if (args is not AddonReceiveEventArgs eventArgs) return;

        var eventData = (AtkEventData*)eventArgs.AtkEventData;
        if (eventData == null) return;

        var eventType = (AtkEventType)eventArgs.AtkEventType;
        var isDialogueAdvancing =
            (eventType == AtkEventType.MouseClick && ((byte)eventData->MouseData.Modifier & 0b0001_0000) == 0) ||
            eventArgs.AtkEventType == (byte)AtkEventType.InputReceived;

        if (!isDialogueAdvancing) return;

        // If the dropdown is open and this click lands outside our toolbar, close it.
        // DropDownFocusCollisionNode handles clicks inside Talk's bounds; this handles the rest.
        if (_dropDownIsOpen && !_suppressNextAdvance && !_layout.CheckCollision(eventData))
            _voiceDropDown?.Collapse(false); // fires OnCollapsed: _dropDownIsOpen=false, _suppressNextAdvance=true

        // Check if the click landed inside any owned Echokraut native window
        var clickedOwnedWindow = _isInsideOwnedWindow != null
            && _isInsideOwnedWindow(new Vector2(eventData->MouseData.PosX, eventData->MouseData.PosY));

        if (_layout.CheckCollision(eventData) || _suppressNextAdvance || _dropDownIsOpen || clickedOwnedWindow)
        {
            _suppressNextAdvance = false;
            eventArgs.AtkEventType = 0;
        }
    }

    private static void SetEnabled(NodeBase node, bool enabled)
        => node.Alpha = enabled ? EnabledAlpha : DisabledAlpha;

    private bool IsMuted()
        => DialogState.CurrentVoiceMessage?.SpeakerObj != null
           && _config.MutedNpcDialogues.Contains(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);

    private void OnDropDownOpened()
    {
        var msg = DialogState.CurrentVoiceMessage;
        if (msg != null && _audioPlayback.GetStreamState(msg.StreamId) == PlaybackState.Playing)
        {
            _audioPlayback.PausePlaying(msg);
            _pausedForDropDown = true;
        }
    }

    private void OnPlayPauseClick()
    {
        var msg = DialogState.CurrentVoiceMessage;

        if (_audioPlayback.IsPlaying && msg != null)
        {
            var streamState = _audioPlayback.GetStreamState(msg.StreamId);
            if (streamState == PlaybackState.Playing)
                _audioPlayback.PausePlaying(msg);
            else
                _audioPlayback.ResumePlaying(msg);
        }
        else if (!_audioPlayback.RecreationStarted)
        {
            _recreateInference();
        }
    }

    private void OnStopClick()
    {
        if (DialogState.CurrentVoiceMessage != null) _lipSync.TryStopLipSync(DialogState.CurrentVoiceMessage);
        _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
    }

    private void OnMuteClick(bool isChecked)
    {
        if (DialogState.CurrentVoiceMessage?.SpeakerObj == null) return;

        if (isChecked)
        {
            _config.MutedNpcDialogues.Add(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);
            if (_audioPlayback.IsPlaying)
            {
                _lipSync.TryStopLipSync(DialogState.CurrentVoiceMessage);
                _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
            }
        }
        else
        {
            _config.MutedNpcDialogues.Remove(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);
            _recreateInference();
        }
    }

    private void OnAutoAdvanceClick(bool isChecked)
    {
        _config.AutoAdvanceTextAfterSpeechCompleted = isChecked;
        _config.Save();
    }
}
