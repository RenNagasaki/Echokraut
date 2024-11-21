using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using static Dalamud.Plugin.Services.IFramework;
using Dalamud.Game.ClientState.Objects.SubKinds;
using System.Reflection;
using Echokraut.Enums;
using Echokraut.Helper.DataHelper;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Addons
{
    public class AddonBubbleHelper
    {

        private unsafe delegate nint OpenChatBubbleDelegate(nint self, GameObject* actor, nint textPtr, bool notSure, int attachmentPointID);
        private readonly Hook<OpenChatBubbleDelegate> mOpenChatBubbleHook;
        private readonly object mSpeechBubbleInfoLockObj = new();
        private readonly List<SpeechBubbleInfo> mSpeechBubbleInfo = new();
        private OnUpdateDelegate updateHandler;
        private ICondition condition;
        private IObjectTable objects;
        private ISigScanner sigScanner;
        private IGameInteropProvider gameInteropProvider;
        private IClientState clientState;
        private IFramework framework;
        private IDataManager dataManager;
        private Configuration configuration;
        private Echokraut echokraut;
        private unsafe Camera* camera;
        private unsafe IPlayerCharacter localPlayer;
        public bool nextIsVoice = false;
        public DateTime timeNextVoice = DateTime.Now;

        public unsafe AddonBubbleHelper(Echokraut echokraut, ICondition condition, IDataManager dataManager, IFramework framework, IObjectTable objectTable, ISigScanner sigScanner, IGameInteropProvider gameInteropProvider, IClientState clientState, Configuration config)
        {
            this.echokraut = echokraut;
            this.condition = condition;
            this.dataManager = dataManager;
            this.framework = framework;
            this.objects = objectTable;
            this.sigScanner = sigScanner;
            this.gameInteropProvider = gameInteropProvider;
            this.clientState = clientState;
            configuration = config;
            ManagedBass.Bass.Init(Flags: ManagedBass.DeviceInitFlags.Device3D);
            //ManagedBass.Bass.CurrentDevice = 1;
            Update3DFactors(config.VoiceBubbleAudibleRange);

            unsafe
            {
                var fpOpenChatBubble = sigScanner.ScanText("E8 ?? ?? ?? ?? F6 86 ?? ?? ?? ?? ?? C7 46 ?? ?? ?? ?? ??");
                if (fpOpenChatBubble != nint.Zero)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"OpenChatBubble function signature found at 0x{fpOpenChatBubble:X}", new EKEventId(0, TextSource.AddonBubble));
                    mOpenChatBubbleHook = gameInteropProvider.HookFromAddress<OpenChatBubbleDelegate>(fpOpenChatBubble, OpenChatBubbleDetour);
                    mOpenChatBubbleHook?.Enable();
                }
                else
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Unable to find the specified function signature for OpenChatBubble", new EKEventId(0, TextSource.AddonBubble));
                }
            }

            HookIntoFrameworkUpdate();
        }

        public void Update3DFactors(float audibleRange)
        {
            ManagedBass.Bass.Set3DFactors(1, audibleRange, 1);
            ManagedBass.Bass.Apply3D();
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Updated 3D factors to: {audibleRange}", new EKEventId(0, TextSource.AddonBubble));
        }

        private void HookIntoFrameworkUpdate()
        {
            updateHandler = new OnUpdateDelegate(Handle);
            framework.Update += updateHandler;

        }
        unsafe void Handle(IFramework f)
        {
            try
            {
                if (!configuration.Enabled) return;
                if (!configuration.VoiceBubble) return;

                var territory = LuminaHelper.GetTerritory();
                if (territory == null || !configuration.VoiceBubblesInCity && !territory.Value.Mount) return;

                if (camera == null && CameraManager.Instance() != null)
                    camera = CameraManager.Instance()->GetActiveCamera();

                localPlayer = clientState.LocalPlayer!;

                if (camera != null && localPlayer != null)
                {
                    var position = new Vector3();
                    if (configuration.VoiceSourceCam)
                        position = camera->CameraBase.SceneCamera.Position;
                    else
                        position = localPlayer.Position;

                    var matrix = camera->CameraBase.SceneCamera.ViewMatrix;
                    ManagedBass.Bass.Set3DPosition(
                        new ManagedBass.Vector3D(position.X, position.Y, position.Z),
                        new ManagedBass.Vector3D(),
                        new ManagedBass.Vector3D(matrix[2], matrix[1], matrix[0]),
                        new ManagedBass.Vector3D(0, 1, 0));
                    ManagedBass.Bass.Apply3D();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", new EKEventId(0, TextSource.AddonBubble));
            }
        }

        unsafe private nint OpenChatBubbleDetour(nint pThis, GameObject* pActor, nint pString, bool param3, int attachmentPointID)
        {
            try
            {
                if (!configuration.Enabled || !configuration.VoiceBubble || condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] || condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent])
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);

                var voiceNext = nextIsVoice;
                nextIsVoice = false;

                if (voiceNext && DateTime.Now > timeNextVoice.AddMilliseconds(1000))
                    voiceNext = false;

                var territory = LuminaHelper.GetTerritory();
                if (!configuration.VoiceBubblesInCity && !territory.Value.Mount)
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);

                if (pString != nint.Zero && !clientState.IsPvPExcludingDen)
                {
                    //	Idk if the actor can ever be null, but if it can, assume that we should print the bubble just in case.  Otherwise, only don't print if the actor is a player.
                    if (pActor == null && !voiceNext || (byte)pActor->ObjectKind != (byte)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player && !voiceNext)
                    {
                        var eventId = NpcDataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonBubble);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found EntityId: {pActor->GetGameObjectId().ObjectId}", eventId);
                        var currentTime_mSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        var speakerName = SeString.Empty;
                        if (pActor != null && pActor->Name != null)
                        {
                            speakerName = MemoryHelper.ReadSeStringNullTerminated((nint)pActor->GetName());
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
                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Found bubble: {speakerName} - {text}", eventId);
                                    var actorObject = objects.CreateObjectReference((nint)pActor);
                                    echokraut.Say(eventId, actorObject, speakerName, text.ToString());
                                }
                                else
                                {

                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Bubble already played in the last <5 seconds. Skipping: {speakerName} - {text}", eventId);
                                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                                }

                                extantMatch.TimeLastSeen_mSec = currentTime_mSec;
                            }
                            else
                            {
                                mSpeechBubbleInfo.Add(bubbleInfo);
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Found bubble: {speakerName} - {text}", eventId);
                                var actorObject = objects.CreateObjectReference((nint)pActor);
                                echokraut.Say(eventId, actorObject, speakerName, text.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", new EKEventId(0, TextSource.AddonBubble));
            }

            return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);
        }

        public void Dispose()
        {
            ManagedBass.Bass.Free();
            mOpenChatBubbleHook?.Dispose();
        }
    }
}
