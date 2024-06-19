using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Interface.Utility.Table;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Data.Parsing;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static Dalamud.Plugin.Services.IFramework;

namespace Echokraut.Helper
{
    internal class VolumeHelper
    {
        public nint BaseAddress { get; private set; }
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate nint GetVolumeDelegate(nint baseAddress, ulong kind, ulong value, ulong unk1, ulong unk2, ulong unk3);
        private readonly Hook<GetVolumeDelegate>? getVolumeHook;

        public VolumeHelper(ISigScanner scanner, IGameInteropProvider gameInterop)
        {
            try
            {
                // I thought I'd need the user to change the settings manually once to get the the base address,
                // but the function is automatically called once when the player is initialized, so I'll settle for that.
                // Note to self: Cheat Engine's "Select current function" tool is unreliable, don't waste time with it.
                // This signature is probably stable, but the option struct offsets need to be updated after some patches.
                var setConfigurationPtr =
                    scanner.ScanText(
                        "89 54 24 10 53 55 57 41 54 41 55 41 56 48 83 EC 48 8B C2 45 8B E0 44 8B D2 45 32 F6 44 8B C2 45 32 ED");
                var getVolume = Marshal.GetDelegateForFunctionPointer<GetVolumeDelegate>(setConfigurationPtr);
                this.getVolumeHook = gameInterop.HookFromAddress<GetVolumeDelegate>(setConfigurationPtr,
                    (baseAddress, kind, value, unk1, unk2, unk3) =>
                    {
                        if (BaseAddress != baseAddress)
                        {
                            LogHelper.Info($"Updated Volume BaseAdress: {baseAddress}");
                            BaseAddress = baseAddress;
                        }

                        return this.getVolumeHook!.Original(baseAddress, kind, value, unk1, unk2, unk3);
                    });
                this.getVolumeHook.Enable();
            }
            catch (Exception e)
            {
                LogHelper.Error($"Failed to hook configuration set method! Full error:\n{e}");
            }
        }

        public float GetVoiceVolume()
        {
            var voiceVolume = 100;
            var masterVolume = 100;
            if (BaseAddress != IntPtr.Zero)
            {
                masterVolume = Marshal.ReadByte(BaseAddress, Constants.MASTERVOLUMEOFFSET);
                voiceVolume = Marshal.ReadByte(BaseAddress, Constants.VOICEVOLUMEOFFSET);
            }

            var volumeFloat = (masterVolume / 100f) * (voiceVolume / 100f);
            LogHelper.Info($"Voice Volume = {volumeFloat}");
            return volumeFloat;
        }

        public void Dispose()
        {
            this.getVolumeHook?.Disable();
            this.getVolumeHook?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
