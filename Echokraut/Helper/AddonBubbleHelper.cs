using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Utils;
using ECommons.Configuration;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using MiniAudioEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Dalamud.Plugin.Services.IFramework;
using static System.Net.Mime.MediaTypeNames;

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
        private Configuration configuration;
        private Echokraut echokraut;

        public AddonBubbleHelper(Echokraut echokraut, IFramework framework, IObjectTable objectTable , ISigScanner sigScanner, IGameInteropProvider gameInteropProvider, IClientState clientState, Configuration config)
        {
            this.objects = objectTable;
            this.echokraut = echokraut;
            this.sigScanner = sigScanner;
            this.gameInteropProvider = gameInteropProvider;
            this.clientState = clientState;
            this.framework = framework;
            this.configuration = config;


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
        void Handle(IFramework f)
        {
            var player = clientState.LocalPlayer;
            //echokraut.listener.Position = new Vector3f(player.Position.X, player.Position.Y, player.Position.Z);
            //echokraut.listener.Direction = new Vector3f(0, 0, player.Rotation);
            //echokraut.soundListener.Position = new RawVector3(player.Position.X, player.Position.Y, player.Position.Z);
            //echokraut.soundListener.OrientFront = new SharpDX.Mathematics.Interop.RawVector3(0, 0, player.Rotation);
        }

        unsafe private IntPtr OpenChatBubbleDetour(IntPtr pThis, GameObject* pActor, IntPtr pString, bool param3)
        {

            try
            {
                if (pString != IntPtr.Zero && !clientState.IsPvPExcludingDen)
                {
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

                        LogHelper.Info("OpenChatBubbleDetour", $"Found bubble: {speakerName} - {text}");
                        var actorObject = objects.CreateObjectReference((IntPtr)pActor);
                        echokraut.Say(actorObject, speakerName, text.ToString(), Enums.TextSource.AddonBubble);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error("OpenChatBubbleDetour", $"Error: {ex}");
            }

            return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3);
        }

        public void Dispose()
        {
            mOpenChatBubbleHook?.Dispose();
        }
    }
}
