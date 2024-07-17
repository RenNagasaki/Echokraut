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

namespace Echokraut.Helper
{
    public class AddonBubbleHelper
    {

        private unsafe delegate IntPtr OpenChatBubbleDelegate(IntPtr pThis, GameObject* pActor, IntPtr pString, bool param3);
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
            ManagedBass.Bass.Set3DFactors(0.9144f * 50, .2f, 1);
            if (CameraManager.Instance() != null)
                camera = CameraManager.Instance()->GetActiveCamera();
            localPlayer = clientState.LocalPlayer;

            unsafe
            {
                IntPtr fpOpenChatBubble = sigScanner.ScanText("E8 ?? ?? ?? FF 48 8B 7C 24 48 C7 46 0C 01 00 00 00");
                if (fpOpenChatBubble != IntPtr.Zero)
                {
                    LogHelper.Info("AddonBubbleHelper", $"OpenChatBubble function signature found at 0x{fpOpenChatBubble:X}");
                    mOpenChatBubbleHook = gameInteropProvider.HookFromAddress<OpenChatBubbleDelegate>(fpOpenChatBubble, OpenChatBubbleDetour);
                    mOpenChatBubbleHook?.Enable();
                }
                else
                {
                    LogHelper.Error("AddonBubbleHelper", $"Unable to find the specified function signature for OpenChatBubble");
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
            
            if (!configuration.VoiceBubbles) return;

            var territoryRow = clientState.TerritoryType;
            var territory = dataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
            if (!configuration.VoiceBubblesInCity && !territory.Mount) return;
            if (camera != null)
            {
                var position = camera->CameraBase.SceneCamera.Position;
                var rotation = camera->CameraBase.SceneCamera.LookAtVector;
                //LogHelper.Debug("", $"{position}");
                //LogHelper.Debug("", $"{rotation}");
                ManagedBass.Bass.Set3DPosition(
                    new ManagedBass.Vector3D(position.X, position.Y, position.Z),
                    new ManagedBass.Vector3D(),
                    new ManagedBass.Vector3D(rotation.X, rotation.Y, rotation.Z),
                    new ManagedBass.Vector3D(0, -1, 0));
                ManagedBass.Bass.Apply3D();
            }
            //echokraut.soundListener.Position = new RawVector3(player.Position.X, player.Position.Y, player.Position.Z);
            //echokraut.soundListener.OrientFront = new SharpDX.Mathematics.Interop.RawVector3(0, 0, player.Rotation);
        }

        unsafe private IntPtr OpenChatBubbleDetour(IntPtr pThis, GameObject* pActor, IntPtr pString, bool param3)
        {
            try
            {
                if (!configuration.VoiceBubbles)
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3);

                var territoryRow = clientState.TerritoryType;
                var territory = dataManager.GetExcelSheet<TerritoryType>()!.GetRow(territoryRow);
                if (!configuration.VoiceBubblesInCity && !territory.Mount)
                    return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3);

                if (pString != IntPtr.Zero && !clientState.IsPvPExcludingDen)
                {
                    LogHelper.Debug("OpenChatBubbleDetour", $"Found EntityId: {pActor->GetGameObjectId().ObjectId}");
                    //	Idk if the actor can ever be null, but if it can, assume that we should print the bubble just in case.  Otherwise, only don't print if the actor is a player.
                    if (pActor == null || (byte)pActor->ObjectKind != (byte)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                    {
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
                                    Say(speakerName, text, pActor);

                                extantMatch.TimeLastSeen_mSec = currentTime_mSec;
                            }
                            else
                            {
                                mSpeechBubbleInfo.Add(bubbleInfo);
                                Say(speakerName, text, pActor);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("OpenChatBubbleDetour", $"Error: {ex}");
            }

            return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3);
        }
        private unsafe void Say(SeString speakerName, SeString text, GameObject* pActor)
        {
            LogHelper.Info("OpenChatBubbleDetour", $"Found bubble: {speakerName} - {text}");
            var actorObject = objects.CreateObjectReference((IntPtr)pActor);
            echokraut.Say(actorObject, speakerName, text.ToString(), Enums.TextSource.AddonBubble);
        }

        public void Dispose()
        {
            ManagedBass.Bass.Free();
            mOpenChatBubbleHook?.Dispose();
        }
    }
}
