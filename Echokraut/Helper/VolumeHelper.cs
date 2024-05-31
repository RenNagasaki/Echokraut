using Dalamud.Game;
using Dalamud.Hooking;
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
        IPluginLog Log;
        public nint BaseAddress { get; private set; }
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        public delegate nint GetVolumeDelegate(nint baseAddress, ulong kind, ulong value, ulong unk1, ulong unk2, ulong unk3);
        private readonly Hook<GetVolumeDelegate>? getVolumeHook;

        public VolumeHelper(ISigScanner scanner, IGameInteropProvider gameInterop, IPluginLog log)
        {
            this.Log = log;
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
                        BaseAddress = baseAddress;

#if DEBUG
                        Log.Debug($"Volume BaseAdress: {baseAddress}");
#endif
                        return this.getVolumeHook!.Original(baseAddress, kind, value, unk1, unk2, unk3);
                    });
                this.getVolumeHook.Enable();
            }
            catch (Exception e)
            {
                Log.Error($"Failed to hook configuration set method! Full error:\n{e}");
            }
        }

        public int GetVoiceVolume()
        {
            var volume = 100;
            if (BaseAddress != IntPtr.Zero)
                volume = Marshal.ReadByte(BaseAddress, Constants.VOICEOFFSET);

            Log.Info($"Voice Volume = {volume}");
            return volume;
        }

        public void Dispose()
        {
            this.getVolumeHook?.Disable();
            this.getVolumeHook?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
