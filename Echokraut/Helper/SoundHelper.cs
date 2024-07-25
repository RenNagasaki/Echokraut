using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using FFXIVClientStructs.FFXIV.Client.Sound;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Echokraut.Helper;

public class SoundHelper : IDisposable
{
    // Signature strings drawn from Anna Clemens's Sound Filter plugin -
    // https://git.anna.lgbt/ascclemens/SoundFilter/src/commit/3b8512b4cd2f3ea0a0d162db4fa251ccb61f7dc4/SoundFilter/Filter.cs#L12
    private const string LoadSoundFileSig = "E8 ?? ?? ?? ?? 48 85 C0 75 04 B0 F6";

    private const string PlaySpecificSoundSig =
        "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 33 F6 8B DA 48 8B F9 0F BA E2 0F";

    private delegate nint LoadSoundFileDelegate(nint resourceHandlePtr, uint arg2);

    private delegate nint PlaySpecificSoundDelegate(nint soundPtr, int arg2);

    private readonly Hook<LoadSoundFileDelegate>? loadSoundFileHook;
    private readonly Hook<PlaySpecificSoundDelegate>? playSpecificSoundHook;

    private static readonly int ResourceDataOffset = Marshal.SizeOf<ResourceHandle>();
    private static readonly int SoundDataOffset = Marshal.SizeOf<nint>();

    private const string SoundContainerFileNameSuffix = ".scd";

    private static readonly Regex IgnoredSoundFileNameRegex = new(
        @"^(bgcommon|music|sound/(battle|foot|instruments|strm|vfx|voice/Vo_Emote|zingle))/");

    private static readonly Regex VoiceLineFileNameRegex = new(@"^cut/.*/(vo_|voice)");
    private static readonly Regex BattleVoiceLineFileNameRegex = new(@"^sound/.*/(Vo_Line)");
    private readonly HashSet<nint> knownVoiceLinePtrs = new();

    private readonly AddonTalkHelper addonTalkHelper;
    private readonly AddonBattleTalkHelper addonBattleTalkHelper;

    public SoundHelper(AddonTalkHelper addonTalkHelper, AddonBattleTalkHelper addonBattleTalkHelper,
        ISigScanner sigScanner, IGameInteropProvider gameInterop)
    {

        this.addonTalkHelper = addonTalkHelper;
        this.addonBattleTalkHelper = addonBattleTalkHelper;

        if (sigScanner.TryScanText(LoadSoundFileSig, out var loadSoundFilePtr))
        {
            this.loadSoundFileHook =
                gameInterop.HookFromAddress<LoadSoundFileDelegate>(loadSoundFilePtr, LoadSoundFileDetour);
            this.loadSoundFileHook.Enable();
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Hooked into LoadSoundFile", new EKEventId(0, TextSource.None));
        }
        else
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to hook into LoadSoundFile", new EKEventId(0, TextSource.None));
        }

        if (sigScanner.TryScanText(PlaySpecificSoundSig, out var playSpecificSoundPtr))
        {
            this.playSpecificSoundHook =
                gameInterop.HookFromAddress<PlaySpecificSoundDelegate>(playSpecificSoundPtr, PlaySpecificSoundDetour);
            this.playSpecificSoundHook.Enable();
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, "Hooked into PlaySpecificSound", new EKEventId(0, TextSource.None));
        }
        else
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Failed to hook into PlaySpecificSound", new EKEventId(0, TextSource.None));
        }
    }

    public void Dispose()
    {
        this.loadSoundFileHook?.Dispose();
        this.playSpecificSoundHook?.Dispose();
    }

    private nint LoadSoundFileDetour(nint resourceHandlePtr, uint arg2)
    {
        var result = this.loadSoundFileHook!.Original(resourceHandlePtr, arg2);

        try
        {
            string fileName;
            unsafe
            {
                fileName = ((ResourceHandle*)resourceHandlePtr)->FileName.ToString();
            }

            if (fileName.EndsWith(SoundContainerFileNameSuffix))
            {
                var resourceDataPtr = Marshal.ReadIntPtr(resourceHandlePtr + ResourceDataOffset);
                if (resourceDataPtr != nint.Zero)
                {
                    var isVoiceLine = false;

                    if (!IgnoredSoundFileNameRegex.IsMatch(fileName))
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Loaded sound: {fileName}", new EKEventId(0, TextSource.None));

                        if (VoiceLineFileNameRegex.IsMatch(fileName) || BattleVoiceLineFileNameRegex.IsMatch(fileName))
                        {
                            isVoiceLine = true;
                        }
                    }

                    if (isVoiceLine)
                    {
                        LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Discovered voice line at address {resourceDataPtr:x}", new EKEventId(0, TextSource.None));
                        this.knownVoiceLinePtrs.Add(resourceDataPtr);
                    }
                    else
                    {
                        // Addresses can be reused, so a non-voice-line sound may be loaded to an address previously
                        // occupied by a voice line.
                        if (this.knownVoiceLinePtrs.Remove(resourceDataPtr))
                        {
                            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, 
                                $"Cleared voice line from address {resourceDataPtr:x} (address reused by: {fileName})", new EKEventId(0, TextSource.None));
                        }
                    }
                }
            }
        }
        catch (Exception exc)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error in LoadSoundFile detour: {exc}", new EKEventId(0, TextSource.None));
        }

        return result;
    }

    private nint PlaySpecificSoundDetour(nint soundPtr, int arg2)
    {
        var result = this.playSpecificSoundHook!.Original(soundPtr, arg2);

        try
        {
            var soundDataPtr = Marshal.ReadIntPtr(soundPtr + SoundDataOffset);
            // Assume that a voice line will be played only once after it's loaded. Then the set can be pruned as voice
            // lines are played.
            if (this.knownVoiceLinePtrs.Remove(soundDataPtr))
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Caught playback of known voice line at address {soundDataPtr:x}", new EKEventId(0, TextSource.None));
                this.addonTalkHelper.PollAddon(AddonPollSource.VoiceLinePlayback);
                this.addonBattleTalkHelper.PollAddon(AddonPollSource.VoiceLinePlayback);
            }
        }
        catch (Exception exc)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error in PlaySpecificSound detour: {exc}", new EKEventId(0, TextSource.None));
        }

        return result;
    }
}
