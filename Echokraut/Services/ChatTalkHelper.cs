using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using Echokraut.Services;
using Echotools.Logging.Services;

namespace Echokraut.Services
{
    internal unsafe class ChatTalkHelper : IChatTalkHelper
    {
        private record struct ChatMessage(XivChatType Type, SeString Sender, SeString Message);

        // Injected dependencies
        private readonly IVoiceMessageProcessor _voiceProcessor;
        private readonly IChatGui _chatGui;
        private readonly ILogService _log;
        private readonly Configuration _configuration;
        private readonly IGameObjectService _gameObjects;
        private readonly ITextProcessingService _textProcessing;

        private readonly Conditions* conditions;
        private IChatGui.OnMessageDelegate handler = null!;

        public ChatTalkHelper(
            IVoiceMessageProcessor voiceProcessor,
            IChatGui chatGui,
            ILogService log,
            Configuration configuration,
            IGameObjectService gameObjects,
            ITextProcessingService textProcessing)
        {
            _voiceProcessor = voiceProcessor ?? throw new ArgumentNullException(nameof(voiceProcessor));
            _chatGui = chatGui ?? throw new ArgumentNullException(nameof(chatGui));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
            _textProcessing = textProcessing ?? throw new ArgumentNullException(nameof(textProcessing));

            this.conditions = Conditions.Instance();

            HookIntoChat();
        }

        private void HookIntoChat()
        {
            handler = new IChatGui.OnMessageDelegate(Handle);
            _chatGui.ChatMessage += handler;
        }
        void Handle(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool handled)
        {
            if (!_configuration.Enabled) return;
            if (!_configuration.VoiceChat) return;
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
                var realSender = _textProcessing.StripWorldFromNames(sender);
                text = _textProcessing.NormalizePunctuation(text);

                switch ((ushort)type)
                {
                    case (ushort)XivChatType.Say:
                        if (!_configuration.VoiceChatSay)
                            return;
                        break;
                    case (ushort)XivChatType.Shout:
                        if (!_configuration.VoiceChatShout)
                            return;
                        break;
                    case (ushort)XivChatType.TellIncoming:
                        if (!_configuration.VoiceChatTell)
                            return;
                        break;
                    case (ushort)XivChatType.TellOutgoing:
                        if (!_configuration.VoiceChatTell)
                            return;
                        realSender = _gameObjects.LocalPlayer?.Name.TextValue ?? "PLAYER";
                        break;
                    case (ushort)XivChatType.Party:
                    case (ushort)XivChatType.CrossParty:
                    case (ushort)XivChatType.PvPTeam:
                        if (!_configuration.VoiceChatParty)
                            return;
                        realSender = realSender.Substring(1);
                        break;
                    case (ushort)XivChatType.Alliance:
                        if (!_configuration.VoiceChatAlliance)
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
                        if (!_configuration.VoiceChatLinkshell)
                            return;
                        break;
                    case (ushort)XivChatType.FreeCompany:
                        if (!_configuration.VoiceChatFreeCompany)
                            return;
                        break;
                    case (ushort)XivChatType.NoviceNetwork:
                        if (!_configuration.VoiceChatNoviceNetwork)
                            return;
                        break;
                    case (ushort)XivChatType.Yell:
                        if (!_configuration.VoiceChatYell)
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
                        if (!_configuration.VoiceChatCrossLinkshell)
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
                        //LogHelper.Debug(nameof(ProcessChatMessage), $"Unwanted Chat ({type}): \"{text}\"", new EKEventId(0, TextSource.Chat));
                        return;
                }

                var eventId = _log.Start(nameof(ProcessChatMessage), TextSource.Chat);
                _log.Debug(nameof(ProcessChatMessage), $"{type.ToString()}: \"{text}\"", eventId);

                // Find the game object this speaker represents
                var speaker = _gameObjects.GetGameObjectByName(realSender, eventId);
                _log.Debug(nameof(ProcessChatMessage), $"{type.ToString()}: \"{speaker}\" {realSender}", eventId);

                if (!_configuration.VoiceChatPlayer && _gameObjects.LocalPlayer != null && _gameObjects.LocalPlayer.Name.TextValue == realSender) return;

                _ = _voiceProcessor.ProcessSpeechAsync(eventId, speaker ?? null, speaker?.Name ?? "", text);
            }
            catch (Exception ex)
            {
                _log.Error(nameof(ProcessChatMessage), $"Error: {ex}", new EKEventId(0, TextSource.Chat));
            }
        }

        public void Dispose()
        {
            _chatGui.ChatMessage -= handler;
        }
    }
}
