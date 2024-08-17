using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.DataHelper;
using Echokraut.TextToTalk.Utils;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Dalamud.Plugin.Services.IFramework;
using static Echokraut.Helper.Addons.ChatTalkHelper;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentNumericInput.Delegates;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.AxHost;

namespace Echokraut.Helper.Addons
{
    internal class ChatTalkHelper
    {
        private record struct ChatMessage(XivChatType Type, SeString Sender, SeString Message);

        private readonly Echokraut echokraut;
        private readonly Configuration config;
        private readonly IChatGui chat;
        private readonly IObjectTable objects;
        private readonly IClientState clientState;
        private IChatGui.OnMessageDelegate handler;

        public ChatTalkHelper(Echokraut echokraut, Configuration config, IChatGui chat, IObjectTable objects, IClientState clientState)
        {
            this.echokraut = echokraut;
            this.config = config;
            this.chat = chat;
            this.objects = objects;
            this.clientState = clientState;

            HookIntoChat();
        }

        private void HookIntoChat()
        {
            handler = new IChatGui.OnMessageDelegate(Handle);
            chat.ChatMessage += handler;
        }
        void Handle(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool handled)
        {
            if (!config.Enabled) return;
            if (!config.VoiceChat) return;
            if (Conditions.IsWatchingCutscene78 || Conditions.IsWatchingCutscene || Conditions.IsOccupiedInCutSceneEvent || Conditions.IsDutyRecorderPlayback) return;

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
                        if (!config.VoiceChatSay)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.Shout:
                        if (!config.VoiceChatShout)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.TellIncoming:
                    case (ushort)XivChatType.TellOutgoing:
                        if (!config.VoiceChatTell)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.Party:
                    case (ushort)XivChatType.CrossParty:
                    case (ushort)XivChatType.PvPTeam:
                        if (!config.VoiceChatParty)
                        {
                            return;
                        }
                        realSender = realSender.Substring(1);
                        break;
                    case (ushort)XivChatType.Alliance:
                        if (!config.VoiceChatAlliance)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.Ls1:
                    case (ushort)XivChatType.Ls2:
                    case (ushort)XivChatType.Ls3:
                    case (ushort)XivChatType.Ls4:
                    case (ushort)XivChatType.Ls5:
                    case (ushort)XivChatType.Ls6:
                    case (ushort)XivChatType.Ls7:
                    case (ushort)XivChatType.Ls8:
                        if (!config.VoiceChatLinkshell)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.FreeCompany:
                        if (!config.VoiceChatFreeCompany)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.NoviceNetwork:
                        if (!config.VoiceChatNoviceNetwork)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.Yell:
                        if (!config.VoiceChatYell)
                        {
                            return;
                        }
                        break;
                    case (ushort)XivChatType.CrossLinkShell1:
                    case (ushort)XivChatType.CrossLinkShell2:
                    case (ushort)XivChatType.CrossLinkShell3:
                    case (ushort)XivChatType.CrossLinkShell4:
                    case (ushort)XivChatType.CrossLinkShell5:
                    case (ushort)XivChatType.CrossLinkShell6:
                    case (ushort)XivChatType.CrossLinkShell7:
                    case (ushort)XivChatType.CrossLinkShell8:
                        if (!config.VoiceChatCrossLinkshell)
                        {
                            return;
                        }
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

                var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.Chat);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chat ({type}): \"{text}\"", eventId);

                // Find the game object this speaker represents
                var speaker = DalamudHelper.GetGameObjectByName(clientState, objects, realSender, eventId);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chat ({type}): \"{speaker}\" {realSender}", eventId);

                var localPlayer = clientState.LocalPlayer;
                if (!config.VoiceChatPlayer && localPlayer != null && localPlayer.Name.TextValue == realSender) return;

                if (speaker != null)
                {
                    echokraut.Say(eventId, speaker, speaker.Name, text);
                }
                else
                {
                    echokraut.Say(eventId, null, realSender ?? "", text);
                }
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
            {
                return speaker.Name;
            }

            // Parse the speaker name from chat and hope it's right
            return TalkTextHelper.TryGetEntityName(sender, out var senderName) ? senderName : sender;
        }

        public void Dispose()
        {
            chat.ChatMessage -= handler;
        }
    }
}
