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
using System.Runtime.InteropServices;
using static Dalamud.Plugin.Services.IFramework;
using static System.Net.Mime.MediaTypeNames;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.Configuration;
using Lumina.Excel.GeneratedSheets;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Echokraut.Enums;

namespace Echokraut.Helper
{
    public class AddonBubbleHelper
    {

        private unsafe delegate IntPtr OpenChatBubbleDelegate(IntPtr self, GameObject* actor, IntPtr textPtr, bool notSure, int attachmentPointID);
        private readonly Hook<OpenChatBubbleDelegate> mOpenChatBubbleHook;
        private readonly Object mSpeechBubbleInfoLockObj = new();
        private readonly List<SpeechBubbleInfo> mSpeechBubbleInfo = new();
        private OnUpdateDelegate updateHandler;
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

        public unsafe AddonBubbleHelper(Echokraut echokraut, IDataManager dataManager, IFramework framework, IObjectTable objectTable , ISigScanner sigScanner, IGameInteropProvider gameInteropProvider, IClientState clientState, Configuration config)
        {
            this.objects = objectTable;
            this.echokraut = echokraut;
            this.sigScanner = sigScanner;
            this.gameInteropProvider = gameInteropProvider;
            this.clientState = clientState;
            this.framework = framework;
            this.dataManager = dataManager;
            this.configuration = config;
            ManagedBass.Bass.Init(Flags: ManagedBass.DeviceInitFlags.Device3D);
            //ManagedBass.Bass.CurrentDevice = 1;
            ManagedBass.Bass.Set3DFactors(0.9144f, 1f, 1);

            unsafe
            {
                IntPtr fpOpenChatBubble = sigScanner.ScanText("E8 ?? ?? ?? FF 48 8B 7C 24 48 C7 46 0C 01 00 00 00");
                if (fpOpenChatBubble != IntPtr.Zero)
                {
                    LogHelper.Info("AddonBubbleHelper", $"OpenChatBubble function signature found at 0x{fpOpenChatBubble:X}", new EKEventId(0, TextSource.AddonBubble));
                    mOpenChatBubbleHook = gameInteropProvider.HookFromAddress<OpenChatBubbleDelegate>(fpOpenChatBubble, OpenChatBubbleDetour);
                    mOpenChatBubbleHook?.Enable();
                }
                else
                {
                    LogHelper.Error("AddonBubbleHelper", $"Unable to find the specified function signature for OpenChatBubble", new EKEventId(0, TextSource.AddonBubble));
                }
            }

            HookIntoFrameworkUpdate();
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

                var territory = DataHelper.GetTerritory();
                if (territory == null || (!configuration.VoiceBubblesInCity && !territory.Mount)) return;

                if (camera == null && CameraManager.Instance() != null)
                    camera = CameraManager.Instance()->GetActiveCamera();

                localPlayer = clientState.LocalPlayer!;

                if (camera != null &&localPlayer != null)
                {
                    var position = new Vector3();
                    if (configuration.VoiceSourceCam)
                        position = camera->CameraBase.SceneCamera.Position;
                    else
                        position = localPlayer.Position;

                    var matrix = camera->CameraBase.SceneCamera.ViewMatrix;
                    ManagedBass.Bass.Set3DPosition(
                        new ManagedBass.Vector3D(position.X, position.Z, -position.Y),
                        new ManagedBass.Vector3D(),
                        new ManagedBass.Vector3D(matrix[2], matrix[1], matrix[0]),
                        new ManagedBass.Vector3D(0, 1, 0));
                    ManagedBass.Bass.Apply3D();
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error: {ex}", new EKEventId(0, TextSource.None));
            }
        }

        unsafe private IntPtr OpenChatBubbleDetour(IntPtr pThis, GameObject* pActor, IntPtr pString, bool param3, int attachmentPointID)
        {
            try
            {
                if (!configuration.Enabled || !configuration.VoiceBubble)
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);

                if (SoundHelper.VoiceLinesToCome > 0)
                {
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Skipping bubble because voice line to come", new EKEventId(0, TextSource.AddonBubble));
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);
                }

                var territoryRow = clientState.TerritoryType;
                var territory = dataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
                if (!configuration.VoiceBubblesInCity && !territory.Mount)
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3, attachmentPointID);

                if (pString != IntPtr.Zero && !clientState.IsPvPExcludingDen)
                {
                    //	Idk if the actor can ever be null, but if it can, assume that we should print the bubble just in case.  Otherwise, only don't print if the actor is a player.
                    if (pActor == null || (byte)pActor->ObjectKind != (byte)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                    {
                        EKEventId eventId = DataHelper.EventId(MethodBase.GetCurrentMethod().Name, TextSource.AddonBubble);
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Found EntityId: {pActor->GetGameObjectId().ObjectId}", eventId);
                        long currentTime_mSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        SeString speakerName = SeString.Empty;
                        if (pActor != null && pActor->Name != null)
                        {
                            speakerName = MemoryHelper.ReadSeStringNullTerminated((IntPtr)pActor->GetName());
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
                                    var actorObject = objects.CreateObjectReference((IntPtr)pActor);
                                    echokraut.Say(eventId, actorObject, speakerName, text.ToString());
                                }
                                else
                                {

                                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Bubble already played in the last <5 seconds: {speakerName} - {text}", eventId);
                                    LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                                }

                                extantMatch.TimeLastSeen_mSec = currentTime_mSec;
                            }
                            else
                            {
                                mSpeechBubbleInfo.Add(bubbleInfo);
                                LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Found bubble: {speakerName} - {text}", eventId);
                                var actorObject = objects.CreateObjectReference((IntPtr)pActor);
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
