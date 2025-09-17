using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Addons;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using OtterGui;
using OtterGui.Raii;

namespace Echokraut.Windows;

public class DialogExtraOptionsWindow : Window, IDisposable
{
    public static VoiceMessage? CurrentVoiceMessage = null;
    public static bool IsVoiced = false;
    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public DialogExtraOptionsWindow() : base("EK-DialogExtraOptionsWindow")
    {
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
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawReadyStates();
    }

    private void DrawReadyStates()
    {
        if (Plugin.Configuration.ShowExtraOptionsInDialogue && !IsVoiced)
        {
            var iconSize = new Vector2(24, 24) * AddonTalkHelper.AddonScale;
            var offsetX = 56 * AddonTalkHelper.AddonScale;
            var offsetXButton = 16 * AddonTalkHelper.AddonScale;
            var offsetY = 104 * AddonTalkHelper.AddonScale;

            var xPos = (AddonTalkHelper.AddonPos.X + offsetX);
            var yPos = (AddonTalkHelper.AddonPos.Y + offsetY);
            var sizeExtra = new Vector2(iconSize.X * 3 + offsetXButton * 2, iconSize.Y);
            var sizeExtraExtra = new Vector2(iconSize.X * 15, 0);
            Size = sizeExtra + (Plugin.Configuration.ShowExtraExtraOptionsInDialogue
                                    ? sizeExtraExtra
                                    : new Vector2());
            Position = new Vector2(xPos, yPos);

            var disabled = CurrentVoiceMessage != null && CurrentVoiceMessage.SpeakerObj != null && Plugin.Configuration.MutedNpcDialogues.Contains(CurrentVoiceMessage.SpeakerObj.DataId);
            using (ImRaii.Disabled(disabled))
            {
                if (PlayingHelper.Playing)
                {
                    if (PlayingHelper.AudioEngine.GetState(CurrentVoiceMessage.StreamId) != PlaybackState.Playing)
                    {
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##ResumeDialog",
                                                         iconSize,
                                                         "Resume dialogue", false, true))
                            Plugin.Resume(CurrentVoiceMessage);
                    }
                    else if (PlayingHelper.AudioEngine.GetState(CurrentVoiceMessage.StreamId) == PlaybackState.Playing)
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Pause.ToIconString()}##PauseDialog",
                                                         iconSize,
                                                         "Pause dialogue", false, true))
                            Plugin.Pause(CurrentVoiceMessage);
                }
                else
                    using (ImRaii.Disabled(PlayingHelper.RecreationStarted))
                        if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Play.ToIconString()}##RecreateDialog",
                                                         iconSize,
                                                         "Recreate dialogue", false, true))
                            AddonTalkHelper.RecreateInference();

                ImGui.SameLine();
                using (ImRaii.Disabled(!PlayingHelper.Playing))
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Stop.ToIconString()}##StopDialog", iconSize,
                                                     "Stop dialogue", !PlayingHelper.Playing, true))
                        Plugin.Cancel(CurrentVoiceMessage);
            }

            ImGui.SameLine();
            if (!disabled)
            {
                if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Microphone.ToIconString()}##MuteDialogue",
                                                 iconSize,
                                                 "Mute dialogue", false, true))
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name,
                                   $"Muting NPC Dialogue: {CurrentVoiceMessage.SpeakerObj!.Name.TextValue}",
                                   new EKEventId(0, TextSource.AddonTalk));
                    Plugin.Configuration.MutedNpcDialogues.Add(CurrentVoiceMessage.SpeakerObj!.DataId);
                    if (PlayingHelper.Playing)
                        Plugin.Cancel(CurrentVoiceMessage);
                }
            }
            else if (ImGuiUtil.DrawDisabledButton(
                         $"{FontAwesomeIcon.MicrophoneSlash.ToIconString()}##UnmuteDialogue",
                         iconSize,
                         "Unmute dialogue", false, true))
            {
                LogHelper.Info(MethodBase.GetCurrentMethod().Name,
                               $"Unmuting NPC Dialogue: {CurrentVoiceMessage.SpeakerObj!.Name.TextValue}",
                               new EKEventId(0, TextSource.AddonTalk));
                Plugin.Configuration.MutedNpcDialogues.Remove(CurrentVoiceMessage.SpeakerObj!.DataId);
                AddonTalkHelper.RecreateInference();
            }

            if (Plugin.Configuration.ShowExtraExtraOptionsInDialogue)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Constants.BLACKCOLOR))
                {
                    using (ImRaii.PushColor(ImGuiCol.CheckMark, Constants.BLACKCOLOR))
                    {
                        ImGui.SameLine();
                        var autoAdvance = Plugin.Configuration!.AutoAdvanceTextAfterSpeechCompleted;
                        if (ImGui.Checkbox("Auto advance", ref autoAdvance))
                        {
                            Plugin.Configuration.AutoAdvanceTextAfterSpeechCompleted = autoAdvance;
                            Plugin.Configuration.Save();
                        }
                    }
                }
                ImGui.SameLine();
                if (CurrentVoiceMessage.Speaker.VoicesSelectableDialogue.Draw(
                        CurrentVoiceMessage.Speaker.Voice?.VoiceName ?? "", out var selectedIndexVoice))
                {
                    var newVoiceItem =
                        Plugin.Configuration!.EchokrautVoices.FindAll(f => f.IsSelectable(
                                                                          CurrentVoiceMessage.Speaker.Name,
                                                                          CurrentVoiceMessage.Speaker.Gender,
                                                                          CurrentVoiceMessage.Speaker.Race,
                                                                          CurrentVoiceMessage.Speaker.IsChild))[
                            selectedIndexVoice];

                    if (CurrentVoiceMessage.Speaker.Voice != newVoiceItem)
                    {
                        CurrentVoiceMessage.Speaker.Voice = newVoiceItem;
                        CurrentVoiceMessage.Speaker.DoNotDelete = true;
                        CurrentVoiceMessage.Speaker.RefreshSelectable();
                        Plugin.Configuration.Save();
                        LogHelper.Info(MethodBase.GetCurrentMethod()!.Name,
                                       $"Updated Voice for {CurrentVoiceMessage.Speaker.Name}: {CurrentVoiceMessage.Speaker.ToString()} from: {CurrentVoiceMessage.Speaker.Voice} to: {newVoiceItem}",
                                       new EKEventId(0, TextSource.None));
                        Plugin.Cancel(CurrentVoiceMessage);
                        AddonTalkHelper.RecreateInference();
                    }
                }
            }
            ImGui.SameLine();
            if (ImGuiUtil.DrawDisabledButton(
                    $"{FontAwesomeIcon.Cog.ToIconString()}##OpenSettings",
                    iconSize,
                    "Toggle Echokraut config window", false, true))
            {
                CommandHelper.ToggleConfigUi();
            }
        }
    }
}
