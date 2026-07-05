using System;
using System.Linq;
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

using static Echokraut.Windows.Native.NativeNodeFactory;

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
    private readonly IFramework _framework;
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
    private StringDropDownNode? _voiceDropDown;

    // Custom tooltip rendered as children of the Talk addon. AtkTooltipManager places its
    // tooltip in a separate addon layer that ends up below Talk during NPC dialog, so the
    // built-in tooltip path was unusable here. Rendering our own nodes inside Talk's tree
    // inherits Talk's depth and lands above the dialog background — without changing Talk
    // itself. Background uses ui/uld/ToolTipS.tex (same NineGrid layout as KamiToolKit's
    // TextNineGridNode) so the appearance matches the standard FFXIV tooltip.
    private SimpleNineGridNode? _tooltipBg;
    private TextNode? _tooltipText;
    private string _playStopTooltipText = string.Empty;

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

    // OnUpdate runs every frame while the Talk addon is open. Each native property write
    // (Alpha, IsVisible, IsChecked, Icon, Position/Scale) maps to an ATK call, and our own
    // CLAUDE.md (Windows/) warns: "Don't set alpha/multiply every frame — set only when
    // state changes". We cache the last-applied value per property and only re-write when
    // it differs. Without this every dialog open dropped FPS noticeably on lower-end rigs.
    private bool? _lastRowVisible;
    private float _lastAddonW = -1f;
    private float _lastAddonH = -1f;
    private bool? _lastIsActive;
    private bool? _lastPlayStopEnabled;
    private bool? _lastMuteEnabled;
    private bool? _lastMuteChecked;
    private bool? _lastAutoChecked;
    private bool? _lastVoiceEnabled;

    public DialogTalkController(
        EKConfig config,
        IAudioPlaybackService audioPlayback,
        ILipSyncHelper lipSync,
        Action recreateInference,
        Action openVoiceClipManager,
        IAddonLifecycle addonLifecycle,
        IFramework framework,
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
        _framework = framework;
        _ipc = ipc;

        _talkController = new AddonController
        {
            AddonName = "Talk",
            OnSetup = OnAttach,
            OnUpdate = OnUpdate,
            OnFinalize = OnDetach,
        };
        // KamiToolKit (latest) asserts the main/framework thread inside
        // AddonController.Enable() (and OnSetup runs synchronously there if Talk is
        // already open). Plugin construction runs off the framework thread, so enable
        // on the framework thread to satisfy the assert. Runs synchronously if we're
        // already on it.
        _framework.RunOnFrameworkThread(() => _talkController.Enable());

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
        _tooltipBg?.Dispose();
        _tooltipBg = null;
        _tooltipText?.Dispose();
        _tooltipText = null;
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

        // Reset cached state — fresh nodes on next OnAttach won't have any of the previous
        // values applied, so a "no change" comparison would incorrectly skip the first write.
        _lastRowVisible = null;
        _lastAddonW = -1f;
        _lastAddonH = -1f;
        _lastIsActive = null;
        _lastPlayStopEnabled = null;
        _lastMuteEnabled = null;
        _lastMuteChecked = null;
        _lastAutoChecked = null;
        _lastVoiceEnabled = null;
    }

    /// <summary>
    /// Show the custom tooltip above the given anchor button. Anchor's <c>Position</c> is
    /// expected to be set by the parent <see cref="HorizontalListNode"/> layout.
    /// </summary>
    private void ShowToolbarTooltip(string text, NodeBase anchor)
    {
        if (_tooltipBg is null || _tooltipText is null || _layout is null || string.IsNullOrEmpty(text)) return;

        _tooltipText.String = text;
        // Add room for the NineGrid border (≈14px each side) on top of the measured text width.
        var textW = _tooltipText.GetTextDrawSize(text).X + 28f;
        var bgH = 24f;

        var x = _layout.Position.X + anchor.Position.X + (anchor.Size.X - textW) / 2f;
        var y = _layout.Position.Y - bgH - 4f;

        _tooltipBg.Size = new Vector2(textW, bgH);
        _tooltipBg.Position = new Vector2(x, y);
        _tooltipText.Size = new Vector2(textW, bgH);
        _tooltipText.Position = new Vector2(x, y);
        _tooltipBg.IsVisible = true;
        _tooltipText.IsVisible = true;
    }

    private void HideToolbarTooltip()
    {
        if (_tooltipBg != null) _tooltipBg.IsVisible = false;
        if (_tooltipText != null) _tooltipText.IsVisible = false;
    }

    private void OnAttach(AtkUnitBase* addon)
    {
        // Hover highlight + tooltip are wired on each button's ImageNode (not the wrapping
        // ComponentNode). Reasons:
        // - DynamicIconButtonNode gives ImageNode RespondToMouse/HasCollision/EmitsEvents in
        //   its ctor, so MouseOver fires reliably here.
        // - AtkTooltipManager places its tooltip in a separate addon layer that ends up below
        //   Talk during NPC dialog, so we render a custom tooltip via ShowToolbarTooltip
        //   instead — see _tooltipBg / _tooltipText below.
        _playStopTooltipText = Loc.S("Play");
        _playStopButton = new DynamicIconButtonNode { Size = new Vector2(28, 28), Position = new Vector2(0, 2) };
        _playStopButton.Icon = CircleButtonIcon.Volume;
        _playStopButton.OnClick = OnPlayStopClick;
        _playStopButton.ImageNode.AddNodeFlags(NodeFlags.HasCollision);
        WireIconButtonHover(_playStopButton, () => _playStopButton != null,
            () => ShowToolbarTooltip(_playStopTooltipText, _playStopButton), HideToolbarTooltip);

        _muteCheckbox = WithLabelColor(new CheckboxNode { Size = new Vector2(60, 24), String = Loc.S("Mute"), Position = new Vector2(0, 5) });
        _muteCheckbox.OnClick = OnMuteClick;

        _autoAdvanceCheckbox = WithLabelColor(new CheckboxNode { Size = new Vector2(130, 24), String = Loc.S("Auto-advance"), Position = new Vector2(0, 5) });
        _autoAdvanceCheckbox.OnClick = OnAutoAdvanceClick;

        _voiceDropDown = new StringDropDownNode { Size = new Vector2(185, 24), MaxListOptions = 8, Options = [], Position = new Vector2(0, 5) };

        var settingsTooltipText = Loc.S("Open Voice Clip Manager");
        _settingsButton = new DynamicIconButtonNode { Size = new Vector2(28, 28), Position = new Vector2(0, 2) };
        // UV (112, 28) on Character.tex = CircleButtonIcon.MusicNote — visually distinct from the
        // gear icon (which now lives on the config-window opener buttons).
        _settingsButton.Icon = CircleButtonIcon.MusicNote;
        _settingsButton.OnClick = () => _openVoiceClipManager();
        _settingsButton.ImageNode.AddNodeFlags(NodeFlags.HasCollision);
        WireIconButtonHover(_settingsButton, () => _settingsButton != null,
            () => ShowToolbarTooltip(settingsTooltipText, _settingsButton), HideToolbarTooltip);

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

        // Static one-shot setup for nodes whose visible/enabled state never changes while the
        // toolbar row is shown. Pulled out of OnUpdate to avoid per-frame property writes —
        // see fields _lastRowVisible, _lastIsActive, etc. for the rest of the cached state.
        _muteCheckbox.IsVisible = true;
        _autoAdvanceCheckbox.IsVisible = true;
        _voiceDropDown.IsVisible = true;
        _settingsButton.IsVisible = true;
        SetEnabled(_settingsButton, true);
        SetEnabled(_autoAdvanceCheckbox, true);

        // Tooltip nodes — attached AFTER the layout so they render on top within Talk's
        // hierarchy. Sized/positioned dynamically by ShowToolbarTooltip; hidden by default.
        // Background settings mirror KamiToolKit.TextNineGridNode so the look matches the
        // standard FFXIV tooltip used in our other native windows.
        _tooltipBg = new SimpleNineGridNode
        {
            TexturePath = "ui/uld/ToolTipS.tex",
            TextureCoordinates = new Vector2(0f, 0f),
            TextureSize = new Vector2(32f, 24f),
            TopOffset = 10,
            BottomOffset = 10,
            LeftOffset = 15,
            RightOffset = 15,
            Size = new Vector2(140, 24),
            IsVisible = false,
        };
        _tooltipBg.AttachNode(addon);

        _tooltipText = new TextNode
        {
            Size = new Vector2(140, 24),
            FontType = FontType.Axis,
            FontSize = 12,
            TextColor = new Vector4(1f, 1f, 1f, 1f),
            TextOutlineColor = new Vector4(0f, 0f, 0f, 1f),
            TextFlags = TextFlags.Edge,
            AlignmentType = AlignmentType.Center,
            String = string.Empty,
            IsVisible = false,
        };
        _tooltipText.AttachNode(addon);
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

        // Master toggle — ShowExtraOptionsInDialogue hides the whole row. Every other
        // condition (speaker missing, dialogue voiced, etc.) only dims individual buttons
        // via SetEnabled, so the row layout stays stable and no button ever disappears
        // unexpectedly.
        var visible = _config.ShowExtraOptionsInDialogue;
        if (_lastRowVisible != visible)
        {
            _layout!.IsVisible = visible;
            if (_background != null) _background.IsVisible = visible;
            _lastRowVisible = visible;
        }
        if (!visible) return;

        // Keep controls and background anchored below the Talk addon (height can change with text length).
        // Position writes are gated on actual addon-size change — Talk's RootNode size only shifts
        // when the dialog text wraps to a new line, not every frame.
        const int overlap = 52;
        const int buttonH = 28;
        var addonW = addon->RootNode->Width;
        var addonH = addon->RootNode->Height;
        if (addonW != _lastAddonW || addonH != _lastAddonH)
        {
            var bgW = addonW - 40;
            var bgH = (int)(bgW * 128f / 512f / 2f) - 5;
            var bgX = (addonW - bgW) / 2f;
            _background!.Scale = new Vector2(bgW / 544f, bgH / 144f);
            _background.Position = new Vector2(bgX, addonH - overlap);
            _layout!.Position = new Vector2(bgX + (bgW - _layout.Size.X) / 2f - 25, addonH - overlap + (bgH - buttonH) / 2f - 5);
            _lastAddonW = addonW;
            _lastAddonH = addonH;
        }

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

        // Play/Stop — Icon + tooltip text only flip when the active state flips.
        if (_lastIsActive != isActive)
        {
            _playStopButton!.Icon = isActive ? CircleButtonIcon.Mute : CircleButtonIcon.Volume;
            _playStopTooltipText  = isActive ? Loc.S("Stop") : Loc.S("Play");
            _lastIsActive = isActive;
        }
        var playStopEnabled = inDialog && notVoiced && !isMuted && (isActive || !_audioPlayback.RecreationStarted);
        if (_lastPlayStopEnabled != playStopEnabled)
        {
            SetEnabled(_playStopButton!, playStopEnabled);
            _lastPlayStopEnabled = playStopEnabled;
        }

        // Mute checkbox — alpha + checked state cached separately.
        var muteEnabled = hasSpeakerObj && inDialog && notVoiced;
        var muteChecked = hasSpeakerObj && isMuted;
        if (_lastMuteChecked != muteChecked)
        {
            _muteCheckbox!.IsChecked = muteChecked;
            _lastMuteChecked = muteChecked;
        }
        if (_lastMuteEnabled != muteEnabled)
        {
            SetEnabled(_muteCheckbox!, muteEnabled);
            _lastMuteEnabled = muteEnabled;
        }

        // Auto-advance — IsChecked is the only dynamic input here. SetEnabled(true) is set
        // once during OnAttach (see below) since this control is always interactive while
        // the row is visible.
        var autoChecked = _config.AutoAdvanceTextAfterSpeechCompleted;
        if (_lastAutoChecked != autoChecked)
        {
            _autoAdvanceCheckbox!.IsChecked = autoChecked;
            _lastAutoChecked = autoChecked;
        }

        var voiceUsable = hasSpeaker;

        // Force-collapse if currently open but disabled — otherwise the floating option list
        // dangles with no way for the user to dismiss it (clicks are inert at low alpha).
        if (!voiceUsable && _dropDownIsOpen)
            _voiceDropDown?.Collapse(false);

        if (_lastVoiceEnabled != voiceUsable)
        {
            SetEnabled(_voiceDropDown!, voiceUsable);
            _lastVoiceEnabled = voiceUsable;
        }
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
                // Only assign Options when the list actually changed in content. The reworked
                // DropDownNode pools its popup buttons (RebuildPopupList only reallocates when the
                // visible-button count changes) and guards disposals, so re-assigning is far safer
                // than the old node — but skipping identical rebuilds still avoids needless work
                // when consecutive speakers share the same selectable-voice list.
                var currentOptions = _voiceDropDown!.Options;
                var optionsChanged = currentOptions is null || !currentOptions.SequenceEqual(voiceNames);
                if (optionsChanged)
                    _voiceDropDown.Options = voiceNames;
                _voiceDropDown.SelectedOption = selectedVoice; // setter refreshes the label safely
                // Layout content changed (option list rebuilt) — recompute layout once here.
                if (optionsChanged)
                    _layout?.RecalculateLayout();
            }
        }
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
                // Persist to SQLite (voice assignments live in the DB since v5+); without
                // this the user's pick survives only the current session and the next
                // generation pipeline re-resolves the voice from the stale DB row.
                _npcData.SaveCharacter(msg.Speaker);
                _config.Save();

                // Force dropdown label refresh on next frame.
                _lastSpeakerKey = string.Empty;

                _lipSync.TryStopLipSync(msg);
                _audioPlayback.StopPlaying(msg);
                _recreateInference();
            }
            // No change: leave _pausedForDropDown as-is so the resume logic in OnUpdateInner fires.
        }

        // Sync the selection + collapse. The reworked DropDownNode already collapses itself and
        // refreshes its label when a popup option is clicked, so this is usually a no-op by the
        // time the deferred handler runs — but a picked voice can also be applied from other paths,
        // so keep it. Wrapped in try/catch so dialogue can still advance if a native call throws.
        try
        {
            if (_voiceDropDown != null && !_voiceDropDown.IsCollapsed)
            {
                _voiceDropDown.SelectedOption = voiceName; // setter refreshes the label safely
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
            (AtkEventType)eventArgs.AtkEventType == AtkEventType.InputReceived;

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
