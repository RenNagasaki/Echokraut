using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.DataClasses
{
    public class VoiceMessage
    {
        public string Text { get; set; } = null!;
        public string OriginalText { get; set; } = null!;
        //public string TextTemplate { get; set; }

        public IGameObject? SpeakerObj {  get; set; }
        public IGameObject? SpeakerFollowObj {  get; set; }
        public NpcMapData Speaker { get; set; } = null!;
        public TextSource Source { get; set; }
        public int? ChatType { get; set; }
        public ClientLanguage Language { get; set; }
        public float Volume { get; set; } = 1f;

        public bool LoadedLocally {  get; set; }

        public bool IsLastInDialogue { get; set; } = false;
        public bool OnlyRequest { get; set; } = false;
        public bool Is3D { get; set; } = false;

        public EKEventId EventId { get; set; } = null!;

        public Stream Stream { get; set; } = null!;
        public Guid StreamId { get; set; }

        /// <summary>
        /// Id of the matching <c>voice_clips</c> row, set by <c>VoiceMessageProcessor</c> after
        /// the clip is upserted. Used by <c>AudioPlaybackService.OnSourceEnded</c> to log a
        /// <c>voice_clip_generations</c> row once the audio has been written to disk.
        /// 0 means "no DB row" (e.g. VoiceTest playback).
        /// </summary>
        public int VoiceClipId { get; set; }

        /// <summary>
        /// Mirrors <c>VoiceClipEntity.HasPlayerPlaceholder</c>. Read by
        /// <c>LiveGenerationLogger</c> so live-path generations are stored with the same
        /// <c>player_content_id</c> the UI uses when querying via
        /// <c>VoiceClipManagerService.GetEffectivePlayerId</c> (0 for shareable clips,
        /// the local player's content id for placeholder clips). Without this the rows are
        /// written but invisible to the manager UI.
        /// </summary>
        public bool HasPlayerPlaceholder { get; set; }

        public string GetDebugInfo()
        {
            return $"SpeakerFollowObj: {SpeakerFollowObj}, SpeakerObj: {SpeakerObj}, Speaker: {Speaker}, IsLastInDialogue: {IsLastInDialogue}, LoadedLocally: {LoadedLocally}, Source: {Source}, ChatType: {ChatType}, Language: {Language}";
        }
    }
}
