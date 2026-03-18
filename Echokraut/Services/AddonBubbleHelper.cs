using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using static Dalamud.Plugin.Services.IFramework;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Utility;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;

namespace Echokraut.Services
{
    public class AddonBubbleHelper : IAddonBubbleHelper
    {
        // Injected dependencies
        private readonly IVoiceMessageProcessor _voiceProcessor;
        private readonly ICondition _condition;
        private readonly IClientState _clientState;
        private readonly IObjectTable _objectTable;
        private readonly ISigScanner _sigScanner;
        private readonly IGameInteropProvider _gameInteropProvider;
        private readonly ILogService _log;
        private readonly Configuration _configuration;
        private readonly ILuminaService _lumina;
        private readonly ISoundHelper _soundHelper;

        private unsafe delegate nint OpenChatBubbleDelegate(nint self, GameObject* actor, nint textPtr, bool notSure, int attachmentPointID);
        private readonly Hook<OpenChatBubbleDelegate> mOpenChatBubbleHook = null!;
        private readonly object mSpeechBubbleInfoLockObj = new();
        private readonly List<SpeechBubbleInfo> mSpeechBubbleInfo = new();
        private bool nextIsVoice = false;
        private DateTime timeNextVoice = DateTime.Now;

        public void NotifyNextIsVoice()
        {
            nextIsVoice = true;
            timeNextVoice = DateTime.Now;
        }

        public unsafe AddonBubbleHelper(
            IVoiceMessageProcessor voiceProcessor,
            ICondition condition,
            IClientState clientState,
            IObjectTable objectTable,
            ISigScanner sigScanner,
            IGameInteropProvider gameInteropProvider,
            ILogService log,
            Configuration configuration,
            ILuminaService lumina,
            ISoundHelper soundHelper)
        {
            _voiceProcessor = voiceProcessor ?? throw new ArgumentNullException(nameof(voiceProcessor));
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
            _objectTable = objectTable ?? throw new ArgumentNullException(nameof(objectTable));
            _sigScanner = sigScanner ?? throw new ArgumentNullException(nameof(sigScanner));
            _gameInteropProvider = gameInteropProvider ?? throw new ArgumentNullException(nameof(gameInteropProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _lumina = lumina ?? throw new ArgumentNullException(nameof(lumina));
            _soundHelper = soundHelper ?? throw new ArgumentNullException(nameof(soundHelper));
            _soundHelper.BattleBubbleVoiceLine += NotifyNextIsVoice;

            unsafe
            {
                var fpOpenChatBubble = _sigScanner.ScanText("E8 ?? ?? ?? ?? F6 86 ?? ?? ?? ?? ?? C7 46 ?? ?? ?? ?? ??");
                if (fpOpenChatBubble != nint.Zero)
                {
                    _log.Info(nameof(AddonBubbleHelper), $"OpenChatBubble function signature found at 0x{fpOpenChatBubble:X}", new EKEventId(0, TextSource.AddonBubble));
                    mOpenChatBubbleHook = _gameInteropProvider.HookFromAddress<OpenChatBubbleDelegate>(fpOpenChatBubble, OpenChatBubbleDetour);
                    mOpenChatBubbleHook?.Enable();
                }
                else
                {
                    _log.Error(nameof(AddonBubbleHelper), $"Unable to find the specified function signature for OpenChatBubble", new EKEventId(0, TextSource.AddonBubble));
                }
            }
        }

        unsafe private nint OpenChatBubbleDetour(nint pThis, GameObject* pActor, nint pString, bool param3, int attachmentPointID)
        {
            try
            {
                if (!_configuration.Enabled || !_configuration.VoiceBubble || _condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] || _condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent])
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);

                var voiceNext = nextIsVoice;
                nextIsVoice = false;

                if (voiceNext && DateTime.Now > timeNextVoice.AddMilliseconds(1000))
                    voiceNext = false;

                var territory = _lumina.GetTerritory();
                if (!_configuration.VoiceBubblesInCity && territory?.Mount != true)
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);

                if (pString != nint.Zero && !_clientState.IsPvPExcludingDen)
                {
                    //	Idk if the actor can ever be null, but if it can, assume that we should print the bubble just in case.  Otherwise, only don't print if the actor is a player.
                    if (pActor == null && !voiceNext || (byte)pActor->ObjectKind != (byte)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player && !voiceNext)
                    {
                        var eventId = _log.Start(nameof(OpenChatBubbleDetour), TextSource.AddonBubble);
                        _log.Debug(nameof(OpenChatBubbleDetour), $"Found EntityId: {pActor->GetGameObjectId().ObjectId}", eventId);
                        var currentTime_mSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        var speakerName = SeString.Empty;
                        if (pActor != null && !pActor->Name.IsEmpty)
                        {
                            speakerName = pActor->GetName().ExtractText();
                        }

                        var text = MemoryHelper.ReadSeStringNullTerminated(pString);
                        var bubbleInfo = new SpeechBubbleInfo(text, currentTime_mSec, speakerName);

                        lock (mSpeechBubbleInfoLockObj)
                        {
                            var extantMatch = mSpeechBubbleInfo.Find((x) => { return x.IsSameMessageAs(bubbleInfo); });
                            if (extantMatch != null)
                            {
                                if (currentTime_mSec - extantMatch.TimeLastSeen_mSec > 5000)
                                {
                                    _log.Info(nameof(OpenChatBubbleDetour), $"Found bubble: {speakerName} - {text}", eventId);
                                    var actorObject = _objectTable.CreateObjectReference((nint)pActor);
                                    _ = _voiceProcessor.ProcessSpeechAsync(eventId, actorObject, speakerName, text.ToString());
                                }
                                else
                                {

                                    _log.Info(nameof(OpenChatBubbleDetour), $"Bubble already played in the last <5 seconds. Skipping: {speakerName} - {text}", eventId);
                                    _log.End(nameof(OpenChatBubbleDetour), eventId);
                                }

                                extantMatch.TimeLastSeen_mSec = currentTime_mSec;
                            }
                            else
                            {
                                mSpeechBubbleInfo.Add(bubbleInfo);
                                _log.Info(nameof(OpenChatBubbleDetour), $"Found bubble: {speakerName} - {text}", eventId);
                                var actorObject = _objectTable.CreateObjectReference((nint)pActor);
                                _ = _voiceProcessor.ProcessSpeechAsync(eventId, actorObject, speakerName, text.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(nameof(OpenChatBubbleDetour), $"Error: {ex}", new EKEventId(0, TextSource.AddonBubble));
            }

            return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);
        }

        public void Dispose()
        {
            _soundHelper.BattleBubbleVoiceLine -= NotifyNextIsVoice;
            ManagedBass.Bass.Free();
            mOpenChatBubbleHook?.Dispose();
        }
    }
}
