using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    /// <summary>
    /// Legacy per-source log-filter blob. No longer used at runtime — kept only so older saved
    /// configs still deserialize. <see cref="Configuration.MigrateLegacyLogConfig"/> folds it into
    /// <c>Configuration.LogSources</c> (Echotools <c>LogSourceConfig</c> per <c>TextSource</c>) on
    /// startup and nulls it out. Do not add new fields here.
    /// </summary>
    public class LogConfig
    {
        #region General
        public bool ShowGeneralDebugLog { get; set; } = true;
        public bool ShowGeneralErrorLog { get; set; } = true;
        public bool ShowGeneralId0 { get; set; } = true;
        public bool GeneralJumpToBottom { get; set; } = true;
        #endregion
        #region Chat
        public bool ShowChatDebugLog { get; set; } = true;
        public bool ShowChatErrorLog { get; set; } = true;
        public bool ShowChatId0 { get; set; } = true;
        public bool ChatJumpToBottom { get; set; } = true;
        #endregion
        #region Talk
        public bool ShowTalkDebugLog { get; set; } = true;
        public bool ShowTalkErrorLog { get; set; } = true;
        public bool ShowTalkId0 { get; set; } = true;
        public bool TalkJumpToBottom { get; set; } = true;
        #endregion
        #region BattleTalk
        public bool ShowBattleTalkDebugLog { get; set; } = true;
        public bool ShowBattleTalkErrorLog { get; set; } = true;
        public bool ShowBattleTalkId0 { get; set; } = true;
        public bool BattleTalkJumpToBottom { get; set; } = true;
        #endregion
        #region Bubble
        public bool ShowBubbleDebugLog { get; set; } = true;
        public bool ShowBubbleErrorLog { get; set; } = true;
        public bool ShowBubbleId0 { get; set; } = true;
        public bool BubbleJumpToBottom { get; set; } = true;
        #endregion
        #region CutSceneSelectString
        public bool ShowCutsceneSelectStringDebugLog { get; set; } = true;
        public bool ShowCutsceneSelectStringErrorLog { get; set; } = true;
        public bool ShowCutsceneSelectStringId0 { get; set; } = true;
        public bool CutsceneSelectStringJumpToBottom { get; set; } = true;
        #endregion
        #region SelectString
        public bool ShowSelectStringDebugLog { get; set; } = true;
        public bool ShowSelectStringErrorLog { get; set; } = true;
        public bool ShowSelectStringId0 { get; set; } = true;
        public bool SelectStringJumpToBottom { get; set; } = true;
        #endregion
        #region Backend
        public bool ShowBackendDebugLog { get; set; } = true;
        public bool ShowBackendErrorLog { get; set; } = true;
        public bool ShowBackendId0 { get; set; } = true;
        public bool BackendJumpToBottom { get; set; } = true;
        #endregion
    }
}
