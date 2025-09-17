using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.DataHelper;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Reflection;
using Echokraut.Helper.Functional;

namespace Echokraut.Helper.Addons
{
    internal unsafe class ChatTalkHelper
    {
        private record struct ChatMessage(XivChatType Type, SeString Sender, SeString Message);

        private readonly Conditions* conditions;
        private IChatGui.OnMessageDelegate handler;

        public ChatTalkHelper()
        {
            this.conditions = Conditions.Instance();

            HookIntoChat();
        }

        private void HookIntoChat()
        {
            handler = new IChatGui.OnMessageDelegate(Handle);
            Plugin.ChatGui.ChatMessage += handler;
        }
        void Handle(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool handled)
        {
            if (!Plugin.Configuration.Enabled) return;
            if (!Plugin.Configuration.VoiceChat) return;
            if (conditions->WatchingCutscene78 || conditions->WatchingCutscene || conditions->OccupiedInCutSceneEvent || conditions->DutyRecorderPlayback) return;

            var messageObj = new ChatMessage(type, sender, message);
            ProcessChatMessage(messageObj);
        }

        private void ProcessChatMessage(ChatMessage chatMessage)
        {
            try
            {
                var (type, sender, message) = chatMessage;
                var text = message.TextValue;
                var realSender = TalkTextHelper.StripWorldFromNames(sender);
                text = TalkTextHelper.NormalizePunctuation(text);

                switch ((ushort)type)
                {
                    case (ushort)XivChatType.Say:
                        if (!Plugin.Configuration.VoiceChatSay)
                            return;
                        break;
                    case (ushort)XivChatType.Shout:
                        if (!Plugin.Configuration.VoiceChatShout)
                            return;
                        break;
                    case (ushort)XivChatType.TellIncoming:
                        if (!Plugin.Configuration.VoiceChatTell)
                            return;
                        break;
                    case (ushort)XivChatType.TellOutgoing:
                        if (!Plugin.Configuration.VoiceChatTell)
                            return;
                        realSender = DalamudHelper.LocalPlayer?.Name.TextValue ?? "PLAYER";
                        break;
                    case (ushort)XivChatType.Party:
                    case (ushort)XivChatType.CrossParty:
                    case (ushort)XivChatType.PvPTeam:
                        if (!Plugin.Configuration.VoiceChatParty)
                            return;
                        realSender = realSender.Substring(1);
                        break;
                    case (ushort)XivChatType.Alliance:
                        if (!Plugin.Configuration.VoiceChatAlliance)
                            return;
                        break;
                    case (ushort)XivChatType.Ls1:
                    case (ushort)XivChatType.Ls2:
                    case (ushort)XivChatType.Ls3:
                    case (ushort)XivChatType.Ls4:
                    case (ushort)XivChatType.Ls5:
                    case (ushort)XivChatType.Ls6:
                    case (ushort)XivChatType.Ls7:
                    case (ushort)XivChatType.Ls8:
                        if (!Plugin.Configuration.VoiceChatLinkshell)
                            return;
                        break;
                    case (ushort)XivChatType.FreeCompany:
                        if (!Plugin.Configuration.VoiceChatFreeCompany)
                            return;
                        break;
                    case (ushort)XivChatType.NoviceNetwork:
                        if (!Plugin.Configuration.VoiceChatNoviceNetwork)
                            return;
                        break;
                    case (ushort)XivChatType.Yell:
                        if (!Plugin.Configuration.VoiceChatYell)
                            return;
                        break;
                    case (ushort)XivChatType.CrossLinkShell1:
                    case (ushort)XivChatType.CrossLinkShell2:
                    case (ushort)XivChatType.CrossLinkShell3:
                    case (ushort)XivChatType.CrossLinkShell4:
                    case (ushort)XivChatType.CrossLinkShell5:
                    case (ushort)XivChatType.CrossLinkShell6:
                    case (ushort)XivChatType.CrossLinkShell7:
                    case (ushort)XivChatType.CrossLinkShell8:
                        if (!Plugin.Configuration.VoiceChatCrossLinkshell)
                            return;
                        break;
                    case (ushort)XivChatType.None:
                    case (ushort)XivChatType.Debug:
                    case (ushort)XivChatType.Urgent:
                    case (ushort)XivChatType.Notice:
                    case (ushort)XivChatType.CustomEmote:
                    case (ushort)XivChatType.StandardEmote:
                    case (ushort)XivChatType.Echo:
                    case (ushort)XivChatType.SystemError:
                    case (ushort)XivChatType.SystemMessage:
                    case (ushort)XivChatType.GatheringSystemMessage:
                    case (ushort)XivChatType.ErrorMessage:
                    case (ushort)XivChatType.NPCDialogue:
                    case (ushort)XivChatType.NPCDialogueAnnouncements:
                    case (ushort)XivChatType.RetainerSale:
                    case ushort:
                        //LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Unwanted Chat ({type}): \"{text}\"", new EKEventId(0, TextSource.Chat));
                        return;
                }

                var eventId = LogHelper.Start(MethodBase.GetCurrentMethod().Name, TextSource.Chat);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"{type.ToString()}: \"{text}\"", eventId);

                // Find the game object this speaker represents
                var speaker = DalamudHelper.GetGameObjectByName(Plugin.ClientState, Plugin.ObjectTable, realSender, eventId);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"{type.ToString()}: \"{speaker}\" {realSender}", eventId);

                if (!Plugin.Configuration.VoiceChatPlayer && DalamudHelper.LocalPlayer != null && DalamudHelper.LocalPlayer.Name.TextValue == realSender) return;

                Plugin.Say(eventId, speaker ?? null, speaker?.Name ?? "", text);
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", new EKEventId(0, TextSource.Chat));
            }
        }

        private static SeString GetCleanSpeakerName(IGameObject? speaker, SeString sender)
        {
            // Get the speaker name from their entity data, if possible
            if (speaker != null)
                return speaker.Name;

            // Parse the speaker name from chat and hope it's right
            return TalkTextHelper.TryGetEntityName(sender, out var senderName) ? senderName : sender;
        }

        public void Dispose()
        {
            Plugin.ChatGui.ChatMessage -= handler;
        }
    }
}
