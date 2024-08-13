using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class LogConfig
    {
        #region General
        public bool ShowGeneralInfoLog { get; set; } = true;
        public bool ShowGeneralDebugLog { get; set; } = true;
        public bool ShowGeneralErrorLog { get; set; } = true;
        public bool GeneralJumpToBottom { get; set; } = true;
        #endregion
        #region Chat
        public bool ShowChatInfoLog { get; set; } = true;
        public bool ShowChatDebugLog { get; set; } = true;
        public bool ShowChatErrorLog { get; set; } = true;
        public bool ShowChatId0 { get; set; } = true;
        public bool ChatJumpToBottom { get; set; } = true;
        #endregion
        #region Talk
        public bool ShowTalkInfoLog { get; set; } = true;
        public bool ShowTalkDebugLog { get; set; } = true;
        public bool ShowTalkErrorLog { get; set; } = true;
        public bool ShowTalkId0 { get; set; } = true;
        public bool TalkJumpToBottom { get; set; } = true;
        #endregion
        #region BattleTalk
        public bool ShowBattleTalkInfoLog { get; set; } = true;
        public bool ShowBattleTalkDebugLog { get; set; } = true;
        public bool ShowBattleTalkErrorLog { get; set; } = true;
        public bool ShowBattleTalkId0 { get; set; } = true;
        public bool BattleTalkJumpToBottom { get; set; } = true;
        #endregion
        #region Bubble
        public bool ShowBubbleInfoLog { get; set; } = true;
        public bool ShowBubbleDebugLog { get; set; } = true;
        public bool ShowBubbleErrorLog { get; set; } = true;
        public bool ShowBubbleId0 { get; set; } = true;
        public bool BubbleJumpToBottom { get; set; } = true;
        #endregion
        #region CutSceneSelectString
        public bool ShowCutSceneSelectStringInfoLog { get; set; } = true;
        public bool ShowCutSceneSelectStringDebugLog { get; set; } = true;
        public bool ShowCutSceneSelectStringErrorLog { get; set; } = true;
        public bool ShowCutSceneSelectStringId0 { get; set; } = true;
        public bool CutSceneSelectStringJumpToBottom { get; set; } = true;
        #endregion
        #region SelectString
        public bool ShowSelectStringInfoLog { get; set; } = true;
        public bool ShowSelectStringDebugLog { get; set; } = true;
        public bool ShowSelectStringErrorLog { get; set; } = true;
        public bool ShowSelectStringId0 { get; set; } = true;
        public bool SelectStringJumpToBottom { get; set; } = true;
        #endregion
    }
}
