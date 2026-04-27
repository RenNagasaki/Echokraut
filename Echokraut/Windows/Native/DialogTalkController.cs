using System;
using System.Numerics;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Echotools.UI.Nodes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Classes;
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
    private readonly Action _openVoiceClipManager;
    private readonly ILogService _log;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly INpcDataService _npcData;
    private readonly IEchokrautIpc _ipc;
    private Func<Vector2, bool>? _isInsideOwnedWindow;

    private readonly AddonController _talkController;

    private SimpleImageNode? _background;
    private HorizontalListNode? _layout;
    private DynamicIconButtonNode? _playStopButton;
    private DynamicIconButtonNode? _settingsButton;
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
        Action openVoiceClipManager,
        IAddonLifecycle addonLifecycle,
        ILogService log,
        INpcDataService npcData,
        IEchokrautIpc ipc)
    {
        _log = log;
        _config = config;
        _audioPlayback = audioPlayback;
        _lipSync = lipSync;
        _recreateInference = recreateInference;
        _openVoiceClipManager = openVoiceClipManager;
        _npcData = npcData;
        _addonLifecycle = addonLifecycle;
        _ipc = ipc;

        _talkController = new AddonController("Talk");
        _talkController.OnAttach += OnAttach;
        _talkController.OnUpdate += OnUpdate;
        _talkController.OnDetach += OnDetach;
        _talkController.Enable();

        _addonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);

        // Publish toolbar hit-test via IPC so cooperators (e.g. Echosync) can filter clicks
        // meant for our buttons. PreventOriginal alone isn't enough — listener order across
        // plugins isn't deterministic.
        _ipc.SetClickInToolbarCheck(IsClickInToolbarCoords);
    }

    /// <summary>
    /// Registers a hit-test callback so clicks inside owned native windows
    /// (e.g., config window) suppress Talk addon advance.
    /// </summary>
    public void SetWindowHitTest(Func<Vector2, bool> hitTest)
    {
        _isInsideOwnedWindow = hitTest;
        DialogState.IsInsideOwnedWindow = hitTest;
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "Talk", OnPreReceiveEvent);
        _ipc.SetClickInToolbarCheck(null);
        _talkController.Dispose();
        DisposeNodes();
    }

    /// <summary>
    /// Coordinate-based hit-test for the IPC. Returns true if the screen-space click would
    /// land inside the visible toolbar (layout buttons or surrounding background panel).
    /// </summary>
    private bool IsClickInToolbarCoords(int x, int y)
    {
        if (_layout is null || !_layout.IsVisible) return false;
        var sx = (short)x;
        var sy = (short)y;
        if (_layout.CheckCollision(sx, sy)) return true;
        if (_background != null && _background.IsVisible && _background.CheckCollision(sx, sy)) return true;
        return false;
    }

    private void DisposeNodes()
    {
        _background?.Dispose();
        _background = null;
        _layout?.Dispose();
        _layout = null;
        _playStopButton = null;
        _settingsButton = null;
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
        _playStopButton = new DynamicIconButtonNode { Size = new Vector2(28, 28), Position = new Vector2(0, 2) };
        _playStopButton.Icon = ButtonIcon.Volume;
        _playStopButton.Tooltip = Loc.S("Play");
        _playStopButton.OnClick = OnPlayStopClick;

        _muteCheckbox = new CheckboxNode { Size = new Vector2(60, 24), String = Loc.S("Mute"), Position = new Vector2(0, 5) };
        _muteCheckbox.OnClick = OnMuteClick;

        _autoAdvanceCheckbox = new CheckboxNode { Size = new Vector2(130, 24), String = Loc.S("Auto-advance"), Position = new Vector2(0, 5) };
        _autoAdvanceCheckbox.OnClick = OnAutoAdvanceClick;

        _voiceDropDown = new TextDropDownNode { Size = new Vector2(185, 24), Options = [], Position = new Vector2(0, 5) };

        _settingsButton = new DynamicIconButtonNode { Size = new Vector2(28, 28), Position = new Vector2(0, 2) };
        _settingsButton.Icon = ButtonIcon.GearCog;
        _settingsButton.OnClick = () => _openVoiceClipManager();

        // Tooltip + hover highlight on the ImageNode — DynamicIconButtonNode gives ImageNode
        // RespondToMouse/HasCollision/EmitsEvents in its ctor, so MouseOver fires reliably here.
        // Setting TextTooltip on the ImageNode itself auto-registers ShowTooltip/HideTooltip
        // bound to that node (which actually has the collision flag, unlike the wrapping
        // ComponentNode whose tooltip flow ToggleCollisionFlag deliberately skips).
        _settingsButton.ImageNode.TextTooltip = Loc.S("Open Voice Clip Manager");
        var normalTint = new Vector3(1f, 1f, 1f);
        var hoverTint = new Vector3(1.4f, 1.4f, 1.4f);
        _settingsButton.ImageNode.MultiplyColor = normalTint;
        _settingsButton.ImageNode.AddEvent(AtkEventType.MouseOver, () =>
        {
            if (_settingsButton != null) _settingsButton.ImageNode.MultiplyColor = hoverTint;
        });
        _settingsButton.ImageNode.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (_settingsButton != null) _settingsButton.ImageNode.MultiplyColor = normalTint;
        });

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

        const int padding = 10;
        const int overlap = 52;
        const int buttonH = 28;

        _layout = new HorizontalListNode
        {
            Size = new Vector2(530, buttonH),
            Alignment = HorizontalListAnchor.Left,
            ItemSpacing = 4,
        };
        // Give the layout HasCollision so OnPreReceiveEvent's _layout.CheckCollision reliably
        // returns true for clicks inside the toolbar — including clicks on dimmed buttons that
        // would otherwise pass through and advance the Talk dialog. Don't add RespondToMouse
        // or EmitsEvents — those would change the cursor and risk swallowing events meant for
        // child buttons.
        _layout.AddNodeFlags(NodeFlags.HasCollision);
        _layout.AddNode(_playStopButton);
        _layout.AddNode(_muteCheckbox);
        _layout.AddNode(_autoAdvanceCheckbox);
        _layout.AddNode(_voiceDropDown);
        _layout.AddNode(_settingsButton);

        var addonW = addon->RootNode->Width;
        var addonH = addon->RootNode->Height;
        var bgW = addonW - 40;
        var bgH = (int)(bgW * 128f / 512f / 2f) - 5; // preserve Talk_Basic.tex aspect ratio (512:128)
        var bgX = (addonW - bgW) / 2f;

        _background = new SimpleImageNode
        {
            TexturePath = "ui/uld/Talk_Basic.tex",
            TextureCoordinates = Vector2.Zero,
            TextureSize = new Vector2(544, 144),
            Size = new Vector2(544, 144),
            Position = new Vector2(bgX, addonH - overlap),
            Scale = new Vector2(bgW / 544f, bgH / 144f),
        };
        _background.AttachNode(addon, NodePosition.AsFirstChild);

        _layout.Position = new Vector2(bgX + (bgW - _layout.Size.X) / 2f - 25, addonH - overlap + (bgH - buttonH) / 2f - 5);
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

        // Master toggle — ONLY ShowExtraOptionsInDialogue hides the whole row.
        // Every other condition (speaker missing, dialogue voiced, "extra extra" sub-setting)
        // only dims individual buttons via SetEnabled, so the row layout stays stable and
        // no button ever disappears unexpectedly.
        var visible = _config.ShowExtraOptionsInDialogue;
        _layout!.IsVisible = visible;
        if (_background != null) _background.IsVisible = visible;
        if (!visible) return;

        // Keep controls and background anchored below the Talk addon (height can change with text length).
        const int overlap = 52;
        const int buttonH = 28;
        var addonW = addon->RootNode->Width;
        var addonH = addon->RootNode->Height;
        var bgW = addonW - 40;
        var bgH = (int)(bgW * 128f / 512f / 2f) - 5;
        var bgX = (addonW - bgW) / 2f;

        _background!.Scale = new Vector2(bgW / 544f, bgH / 144f);
        _background.Position = new Vector2(bgX, addonH - overlap);
        _layout.Position = new Vector2(bgX + (bgW - _layout.Size.X) / 2f - 25, addonH - overlap + (bgH - buttonH) / 2f - 5);

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

        var inDialog      = _audioPlayback.InDialog;
        var notVoiced     = !DialogState.IsVoiced;
        var isMuted       = IsMuted();
        var hasSpeakerObj = msg?.SpeakerObj != null;
        var speaker       = msg?.Speaker;
        var hasSpeaker    = speaker != null;

        var streamState = msg != null
            ? _audioPlayback.GetStreamState(msg.StreamId)
            : PlaybackState.Stopped;
        var isStreamPaused = streamState == PlaybackState.Paused;
        var isActive       = _audioPlayback.IsPlaying || isStreamPaused;

        // Play/Stop — always visible, dimmed when not in actionable dialogue.
        _playStopButton!.Icon = isActive ? ButtonIcon.Mute : ButtonIcon.Volume;
        _playStopButton.Tooltip = isActive ? Loc.S("Stop") : Loc.S("Play");
        SetEnabled(_playStopButton, inDialog && notVoiced && !isMuted && (isActive || !_audioPlayback.RecreationStarted));

        // Mute — always visible, dimmed when no speaker / dialogue inactive.
        _muteCheckbox!.IsVisible = true;
        _muteCheckbox.IsChecked = hasSpeakerObj && isMuted;
        SetEnabled(_muteCheckbox, hasSpeakerObj && inDialog && notVoiced);

        // ShowExtraExtraOptionsInDialogue is a sub-setting; per spec it only dims, never hides.
        var showExtra = _config.ShowExtraExtraOptionsInDialogue;

        _autoAdvanceCheckbox!.IsVisible = true;
        _autoAdvanceCheckbox.IsChecked = _config.AutoAdvanceTextAfterSpeechCompleted;
        SetEnabled(_autoAdvanceCheckbox, showExtra);

        var voiceUsable = showExtra && hasSpeaker;

        // Force-collapse if currently open but disabled — otherwise the floating option list
        // dangles with no way for the user to dismiss it (clicks are inert at low alpha).
        if (!voiceUsable && _dropDownIsOpen)
            _voiceDropDown?.Collapse(false);

        _voiceDropDown!.IsVisible = true;
        SetEnabled(_voiceDropDown, voiceUsable);
        if (voiceUsable)
        {
            var speakerKey = speaker!.ToString();
            if (speakerKey != _lastSpeakerKey)
            {
                _lastSpeakerKey = speakerKey;
                var voices = _npcData.GetEchokrautVoices()
                    .FindAll(f => f.IsSelectable(speaker.Name, speaker.Gender, speaker.Race, speaker.BodyType));
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

        // Settings button opens an external window — always visible AND enabled,
        // independent of dialogue state.
        _settingsButton!.IsVisible = true;
        SetEnabled(_settingsButton, true);

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
            var voices = _npcData.GetEchokrautVoices()
                .FindAll(f => f.IsSelectable(msg.Speaker.Name, msg.Speaker.Gender, msg.Speaker.Race, msg.Speaker.BodyType));
            var newVoice = voices.Find(v => v.VoiceName == voiceName);

            if (newVoice != null && newVoice != msg.Speaker.Voice)
            {
                // Voice changed: suppress audio resume so the new voice regenerates instead.
                _pausedForDropDown = false;

                msg.Speaker.Voice = newVoice;
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
        if (args is not AddonReceiveEventArgs eventArgs) return;

        var eventData = (AtkEventData*)eventArgs.AtkEventData;
        if (eventData == null) return;

        var eventType = (AtkEventType)eventArgs.AtkEventType;
        var isDialogueAdvancing =
            (eventType == AtkEventType.MouseClick && ((byte)eventData->MouseData.Modifier & 0b0001_0000) == 0) ||
            eventArgs.AtkEventType == (byte)AtkEventType.InputReceived;

        if (!isDialogueAdvancing) return;

        var clickPos = new Vector2(eventData->MouseData.PosX, eventData->MouseData.PosY);

        // Check if the click landed inside any owned Echokraut native window (always, even without layout)
        var clickedOwnedWindow = _isInsideOwnedWindow != null && _isInsideOwnedWindow(clickPos);

        if (clickedOwnedWindow)
        {
            // PreventOriginal skips the addon's native ReceiveEvent entirely. Setting AtkEventType=0
            // alone does NOT suppress — Dalamud forwards the modified event to the original handler,
            // and the Talk addon's click logic still runs. PreventOriginal calls
            // atkEvent->SetEventIsHandled() and bypasses the original vtable entry.
            eventArgs.PreventOriginal();
            return;
        }

        if (_layout is null || !_layout.IsVisible) return;

        var clickedLayout = _layout.CheckCollision(eventData);
        var clickedBackground = _background != null && _background.IsVisible && _background.CheckCollision(eventData);

        // If the dropdown is open and this click lands outside our toolbar, close it.
        if (_dropDownIsOpen && !_suppressNextAdvance && !clickedLayout)
            _voiceDropDown?.Collapse(false); // fires OnCollapsed: _dropDownIsOpen=false, _suppressNextAdvance=true

        if (clickedLayout || clickedBackground || _suppressNextAdvance || _dropDownIsOpen)
        {
            _suppressNextAdvance = false;
            eventArgs.PreventOriginal();
        }
    }

    private static void SetEnabled(NodeBase node, bool enabled)
        => node.Alpha = enabled ? EnabledAlpha : DisabledAlpha;

    private bool IsMuted()
        // Per-instance mute (CharacterInstance.IsMuted, keyed by ENpcBase ID).
        // Distinct from Edit NPC's "Enabled" toggle, which disables the whole character context.
        => DialogState.CurrentVoiceMessage?.SpeakerObj != null
           && _npcData.IsDialogueMuted(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);

    private void OnDropDownOpened()
    {
        var msg = DialogState.CurrentVoiceMessage;
        if (msg != null && _audioPlayback.GetStreamState(msg.StreamId) == PlaybackState.Playing)
        {
            _audioPlayback.PausePlaying(msg);
            _pausedForDropDown = true;
        }
    }

    private void OnPlayStopClick()
    {
        var msg = DialogState.CurrentVoiceMessage;
        var isActive = _audioPlayback.IsPlaying || (msg != null && _audioPlayback.GetStreamState(msg.StreamId) == PlaybackState.Paused);

        if (isActive)
        {
            if (msg != null) _lipSync.TryStopLipSync(msg);
            _audioPlayback.StopPlaying(msg);
        }
        else if (!_audioPlayback.RecreationStarted)
        {
            _recreateInference();
        }
    }

    private void OnMuteClick(bool isChecked)
    {
        if (DialogState.CurrentVoiceMessage?.SpeakerObj == null) return;

        if (isChecked)
        {
            _npcData.MuteDialogue(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);
            if (_audioPlayback.IsPlaying)
            {
                _lipSync.TryStopLipSync(DialogState.CurrentVoiceMessage);
                _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
            }
        }
        else
        {
            _npcData.UnmuteDialogue(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);
            _recreateInference();
        }
    }

    private void OnAutoAdvanceClick(bool isChecked)
    {
        _config.AutoAdvanceTextAfterSpeechCompleted = isChecked;
        _config.Save();
    }
}
