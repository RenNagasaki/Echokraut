using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Echokraut.Helper
{
    public class AddonBubbleHelper
    {

        private unsafe delegate IntPtr OpenChatBubbleDelegate(IntPtr pThis, GameObject* pActor, IntPtr pString, bool param3);
        private readonly Hook<OpenChatBubbleDelegate> mOpenChatBubbleHook;
        private readonly Object mSpeechBubbleInfoLockObj = new();
        private readonly List<SpeechBubbleInfo> mSpeechBubbleInfo = new();
        private ISigScanner sigScanner;
        private IGameInteropProvider gameInteropProvider;
        private IClientState clientState;

        public AddonBubbleHelper(ISigScanner sigScanner, IGameInteropProvider gameInteropProvider, IClientState clientState)
        {
            this.sigScanner = sigScanner;
            this.gameInteropProvider = gameInteropProvider;
            this.clientState = clientState;

            unsafe
            {
                IntPtr fpOpenChatBubble = sigScanner.ScanText("E8 ?? ?? ?? FF 48 8B 7C 24 48 C7 46 0C 01 00 00 00");
                if (fpOpenChatBubble != IntPtr.Zero)
                {
                    LogHelper.Info("", $"OpenChatBubble function signature found at 0x{fpOpenChatBubble:X}.");
                    mOpenChatBubbleHook = gameInteropProvider.HookFromAddress<OpenChatBubbleDelegate>(fpOpenChatBubble, OpenChatBubbleDetour);
                    mOpenChatBubbleHook?.Enable();
                }
                else
                {
                    throw new Exception("Unable to find the specified function signature for OpenChatBubble.");
                }
            }
        }
        unsafe private IntPtr OpenChatBubbleDetour(IntPtr pThis, GameObject* pActor, IntPtr pString, bool param3)
        {

            if (pString != IntPtr.Zero && !clientState.IsPvPExcludingDen)
            {
                //	Idk if the actor can ever be null, but if it can, assume that we should print the bubble just in case.  Otherwise, only don't print if the actor is a player.
                if (pActor == null || (byte)pActor->ObjectKind != (byte)Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                {
                    long currentTime_mSec = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    SeString speakerName = SeString.Empty;
                    if (pActor != null && pActor->GetName() != null)
                    {
                        speakerName = MemoryHelper.ReadSeStringNullTerminated((IntPtr)pActor->GetName());
                    }
                    var bubbleInfo = new SpeechBubbleInfo(MemoryHelper.ReadSeStringNullTerminated(pString), currentTime_mSec, speakerName);

                    lock (mSpeechBubbleInfoLockObj)
                    {
                        var extantMatch = mSpeechBubbleInfo.Find((x) => { return x.IsSameMessageAs(bubbleInfo); });
                        if (extantMatch != null)
                        {
                            extantMatch.TimeLastSeen_mSec = currentTime_mSec;
                        }
                        else
                        {
                            mSpeechBubbleInfo.Add(bubbleInfo);
                        }
                    }
                }
            }

            return mOpenChatBubbleHook.Original(pThis, pActor, pString, param3);
        }
    }
}
