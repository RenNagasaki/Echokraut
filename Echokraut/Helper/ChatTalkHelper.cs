using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.TextToTalk.Utils;
using Echokraut.Utils;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using R3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Dalamud.Plugin.Services.IFramework;
using static Echokraut.Helper.ChatTalkHelper;
using static FFXIVClientStructs.FFXIV.Component.GUI.AtkComponentNumericInput.Delegates;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.AxHost;

namespace Echokraut.Helper
{
    internal class ChatTalkHelper
    {
        private record struct ChatMessage(XivChatType Type, SeString Sender, SeString Message);

        private readonly Echokraut echokraut;
        private readonly Configuration config;
        private readonly IObjectTable objects;
        private readonly IChatGui chat;
        private readonly IClientState clientState;
        private IChatGui.OnMessageDelegate handler;

        public ChatTalkHelper(Echokraut echokraut, Configuration config, IChatGui chat, IObjectTable objects, IClientState clientState)
        {
            this.echokraut = echokraut;
            this.config = config;
            this.objects = objects;
            this.chat = chat;
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
            
            var messageObj = new ChatMessage(type, sender, message);
            ProcessChatMessage(messageObj);
        }

        private void ProcessChatMessage(ChatMessage chatMessage)
        {
            var (type, sender, message) = chatMessage;
            var text = message.TextValue;
            text = TalkUtils.StripWorldFromNames(text);
            text = TalkUtils.NormalizePunctuation(text);

            var eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name);
            LogHelper.Start(MethodBase.GetCurrentMethod().Name, eventId);
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Chat ({type}): \"{text}\"", eventId);

            switch (type)
            {
                case XivChatType.NPCDialogue:
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    return;
                case XivChatType.NPCDialogueAnnouncements:
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    return;
                case XivChatType.Party:
                case XivChatType.CrossParty:
                case XivChatType.PvPTeam:
                    if (!config.VoiceChatParty)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.NoviceNetwork:
                    if (!config.VoiceChatNoviceNetwork)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.Say:
                    if (!config.VoiceChatSay)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.Yell:
                    if (!config.VoiceChatYell)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.Shout:
                    if (!config.VoiceChatShout)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.FreeCompany:
                    if (!config.VoiceChatFreeCompany)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.TellIncoming:
                case XivChatType.TellOutgoing:
                    if (!config.VoiceChatTell)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.Alliance:
                    if (!config.VoiceChatAlliance)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.CrossLinkShell1:
                case XivChatType.CrossLinkShell2:
                case XivChatType.CrossLinkShell3:
                case XivChatType.CrossLinkShell4:
                case XivChatType.CrossLinkShell5:
                case XivChatType.CrossLinkShell6:
                case XivChatType.CrossLinkShell7:
                case XivChatType.CrossLinkShell8:
                    if (!config.VoiceChatCrossLinkshell)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.Ls1:
                case XivChatType.Ls2:
                case XivChatType.Ls3:
                case XivChatType.Ls4:
                case XivChatType.Ls5:
                case XivChatType.Ls6:
                case XivChatType.Ls7:
                case XivChatType.Ls8:
                    if (!config.VoiceChatLinkshell)
                    {
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                        return;
                    }
                    break;
                case XivChatType.CustomEmote:
                case XivChatType.Echo:
                case XivChatType.Debug:
                case XivChatType.SystemMessage:
                case XivChatType.ErrorMessage:
                case XivChatType.SystemError:
                case XivChatType.None:
                case XivChatType.Notice:
                case XivChatType.GatheringSystemMessage:
                case XivChatType.Urgent:
                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    return;
            }

            // Find the game object this speaker represents
            var speaker = ObjectTableUtils.GetGameObjectByName(this.objects, sender);

            var localPlayer = clientState.LocalPlayer;
            if (!config.VoiceChatPlayer && localPlayer != null && localPlayer.Name.TextValue == sender.TextValue) return;

            if (speaker != null)
            {
                echokraut.Say(eventId, speaker, speaker.Name, text, TextSource.Chat);
            }
            else
            {
                echokraut.Say(eventId, null, sender ?? "", text, TextSource.Chat);
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
            return TalkUtils.TryGetEntityName(sender, out var senderName) ? senderName : sender;
        }

        public void Dispose()
        {
            chat.ChatMessage -= handler;
        }
    }
}
