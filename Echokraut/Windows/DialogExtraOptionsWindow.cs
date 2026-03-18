using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using ManagedBass;
using OtterGui;
using OtterGui.Raii;
using Echokraut.Localization;

namespace Echokraut.Windows;

public class DialogExtraOptionsWindow : Window, IDisposable
{
    private readonly ILogService _log;
    private readonly Echokraut.DataClasses.Configuration _config;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly ILipSyncHelper _lipSync;
    private readonly Action _recreateInference;
    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public DialogExtraOptionsWindow(ILogService log, Echokraut.DataClasses.Configuration config, IAudioPlaybackService audioPlayback, ILipSyncHelper lipSync, Action recreateInference)
        : base("EK-DialogExtraOptionsWindow")
    {
        _log = log;
        _config = config;
        _audioPlayback = audioPlayback;
        _lipSync = lipSync;
        _recreateInference = recreateInference;
        Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBackground |
                ImGuiWindowFlags.NoNav;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(1, 1),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        ForceMainWindow = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        IsOpen = true;
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawReadyStates();
    }

    private void DrawReadyStates()
    {
        if (_config.ShowExtraOptionsInDialogue && !DialogState.IsVoiced && _audioPlayback.InDialog)
        {
            var iconSize = new Vector2(24, 24) * AddonTalkHelper.AddonScale;
            var offsetX = 56 * AddonTalkHelper.AddonScale;
            var offsetXButton = 16 * AddonTalkHelper.AddonScale;
            var offsetY = 104 * AddonTalkHelper.AddonScale;

            var xPos = (AddonTalkHelper.AddonPos.X + offsetX);
            var yPos = (AddonTalkHelper.AddonPos.Y + offsetY);
            var sizeExtra = new Vector2(iconSize.X * 3 + offsetXButton * 2, iconSize.Y);
            var sizeExtraExtra = new Vector2(iconSize.X * 15, 0);
            Size = sizeExtra + (_config.ShowExtraExtraOptionsInDialogue
                                    ? sizeExtraExtra
                                    : new Vector2());
            Position = new Vector2(xPos, yPos);

            var disabled = DialogState.CurrentVoiceMessage != null && DialogState.CurrentVoiceMessage.SpeakerObj != null && _config.MutedNpcDialogues.Contains(DialogState.CurrentVoiceMessage.SpeakerObj.BaseId);
            using (ImRaii.Disabled(disabled))
            {
                if (_audioPlayback.IsPlaying && DialogState.CurrentVoiceMessage != null)
                {
                    if (_audioPlayback.GetStreamState(DialogState.CurrentVoiceMessage.StreamId) != PlaybackState.Playing)
                    {
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##ResumeDialog",
                                                         iconSize,
                                                         Loc.S("Resume dialogue"), false, true))
                            _audioPlayback.ResumePlaying(DialogState.CurrentVoiceMessage);
                    }
                    else if (_audioPlayback.GetStreamState(DialogState.CurrentVoiceMessage.StreamId) == PlaybackState.Playing)
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Pause.ToIconString()}##PauseDialog",
                                                         iconSize,
                                                         Loc.S("Pause dialogue"), false, true))
                            _audioPlayback.PausePlaying(DialogState.CurrentVoiceMessage);
                }
                else
                    using (ImRaii.Disabled(_audioPlayback.RecreationStarted))
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##RecreateDialog",
                                                         iconSize,
                                                         Loc.S("Replay dialogue"), false, true))
                            _recreateInference();

                ImGui.SameLine();
                using (ImRaii.Disabled(!_audioPlayback.IsPlaying))
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Stop.ToIconString()}##StopDialog", iconSize,
                                                     Loc.S("Stop dialogue"), !_audioPlayback.IsPlaying, true))
                    {
                        if (DialogState.CurrentVoiceMessage != null) _lipSync.TryStopLipSync(DialogState.CurrentVoiceMessage);
                        _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
                    }
            }

            ImGui.SameLine();
            if (!disabled)
            {
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Microphone.ToIconString()}##MuteDialogue",
                                                 iconSize,
                                                 Loc.S("Mute dialogue"), false, true))
                {
                    _log.Info(nameof(DrawReadyStates),
                              $"Muting NPC Dialogue: {DialogState.CurrentVoiceMessage!.SpeakerObj!.Name.TextValue}",
                              new EKEventId(0, TextSource.AddonTalk));
                    _config.MutedNpcDialogues.Add(DialogState.CurrentVoiceMessage.SpeakerObj!.BaseId);
                    if (_audioPlayback.IsPlaying)
                    {
                        if (DialogState.CurrentVoiceMessage != null) _lipSync.TryStopLipSync(DialogState.CurrentVoiceMessage);
                        _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
                    }
                }
            }
            else if (ImGuiUtil.DrawDisabledButton(
                         $"{FontAwesomeIcon.MicrophoneSlash.ToIconString()}##UnmuteDialogue",
                         iconSize,
                         Loc.S("Unmute dialogue"), false, true))
            {
                _log.Info(nameof(DrawReadyStates),
                          $"Unmuting NPC Dialogue: {DialogState.CurrentVoiceMessage!.SpeakerObj!.Name.TextValue}",
                          new EKEventId(0, TextSource.AddonTalk));
                _config.MutedNpcDialogues.Remove(DialogState.CurrentVoiceMessage.SpeakerObj!.BaseId);
                _recreateInference();
            }

            if (_config.ShowExtraExtraOptionsInDialogue)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Constants.BLACKCOLOR))
                {
                    using (ImRaii.PushColor(ImGuiCol.CheckMark, Constants.BLACKCOLOR))
                    {
                        ImGui.SameLine();
                        var autoAdvance = _config.AutoAdvanceTextAfterSpeechCompleted;
                        if (ImGui.Checkbox(Loc.S("Auto-advance"), ref autoAdvance))
                        {
                            _config.AutoAdvanceTextAfterSpeechCompleted = autoAdvance;
                            _config.Save();
                        }
                    }
                }
                ImGui.SameLine();
                if (DialogState.CurrentVoiceMessage != null && DialogState.CurrentVoiceMessage.Speaker.VoicesSelectableDialogue.Draw(
                        DialogState.CurrentVoiceMessage.Speaker.Voice?.VoiceName ?? "", out var selectedIndexVoice))
                {
                    var newVoiceItem =
                        _config.EchokrautVoices.FindAll(f => f.IsSelectable(
                                                                          DialogState.CurrentVoiceMessage.Speaker.Name,
                                                                          DialogState.CurrentVoiceMessage.Speaker.Gender,
                                                                          DialogState.CurrentVoiceMessage.Speaker.Race,
                                                                          DialogState.CurrentVoiceMessage.Speaker.IsChild))[
                            selectedIndexVoice];

                    if (DialogState.CurrentVoiceMessage.Speaker.Voice != newVoiceItem)
                    {
                        DialogState.CurrentVoiceMessage.Speaker.Voice = newVoiceItem;
                        DialogState.CurrentVoiceMessage.Speaker.DoNotDelete = true;
                        DialogState.CurrentVoiceMessage.Speaker.RefreshSelectable();
                        _config.Save();
                        _log.Info(nameof(DrawReadyStates),
                                  $"Updated Voice for {DialogState.CurrentVoiceMessage.Speaker.Name}: {DialogState.CurrentVoiceMessage.Speaker.ToString()} from: {DialogState.CurrentVoiceMessage.Speaker.Voice} to: {newVoiceItem}",
                                  new EKEventId(0, TextSource.None));
                        if (DialogState.CurrentVoiceMessage != null) _lipSync.TryStopLipSync(DialogState.CurrentVoiceMessage);
                        _audioPlayback.StopPlaying(DialogState.CurrentVoiceMessage);
                        _recreateInference();
                    }
                }
            }
        }
    }
}
