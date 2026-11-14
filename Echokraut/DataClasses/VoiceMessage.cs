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

        /// <summary>
        /// Ask the backend for a fresh generation instead of a cached copy. Set for explicit
        /// (re)generation from the Voice Clip Manager: the EchokrauTTS wrapper otherwise answers an
        /// identical request within its cache window with the SAME audio (wrapper
        /// <c>response_cache_minutes</c>), which would make "regenerate" a no-op. Sent as
        /// <c>no_cache</c>; older wrappers ignore the field.
        /// </summary>
        public bool NoCache { get; set; } = false;

        public EKEventId EventId { get; set; } = null!;

        /// <summary>
        /// Cancellation epoch of <see cref="Source"/> at the moment this message started being
        /// built (see <c>IVoiceMessageQueue.GetEpoch</c>). If the source is cancelled afterwards
        /// — the player skipped past this line — the epoch moves on and the message is dropped by
        /// the generation and playback loops even if it only reached the queue after the flush.
        /// </summary>
        public long CancelEpoch { get; set; }

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
        /// Id of the speaker's <c>characters</c> row, set together with <see cref="VoiceClipId"/>.
        /// Keys the game-voice reference index (<c>IGameVoiceReferenceService</c>); 0 = unknown,
        /// which means "no game reference, voice pack".
        /// </summary>
        public int CharacterId { get; set; }

        /// <summary>ENpcBase id of the speaker captured when the clip was logged (0 = none). A
        /// plain value on purpose: the generation loop must not read <see cref="SpeakerObj"/>,
        /// whose game object may be gone by then.</summary>
        public uint NpcBaseId { get; set; }

        /// <summary>
        /// What the stream in <see cref="Stream"/> was generated from, set by
        /// <c>BackendService.GenerateVoice</c> (<c>GameVoice</c> only when the server accepted the
        /// game-voice reference). Carried onto the generation row; <c>GameVoice</c> also suppresses the
        /// Drive upload (plan game-voice-reference §6).
        /// </summary>
        public Enums.ReferenceSource ReferenceSource { get; set; }

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
