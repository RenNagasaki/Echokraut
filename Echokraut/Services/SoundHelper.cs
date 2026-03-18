using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace Echokraut.Services;

public class SoundHelper : ISoundHelper
{
    // Signature strings drawn from Anna Clemens's Sound Filter plugin -
    // https://git.anna.lgbt/ascclemens/SoundFilter/src/commit/3b8512b4cd2f3ea0a0d162db4fa251ccb61f7dc4/SoundFilter/Filter.cs#L12
    private const string LoadSoundFileSig = "E8 ?? ?? ?? ?? 48 85 C0 75 05 40 B7 F6";

    private const string PlaySpecificSoundSig =
        "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 33 F6 8B DA 48 8B F9 0F BA E2 0F";

    private delegate nint LoadSoundFileDelegate(nint resourceHandlePtr, uint arg2);

    private delegate nint PlaySpecificSoundDelegate(nint soundPtr, int arg2);

    private readonly Hook<LoadSoundFileDelegate>? loadSoundFileHook;
    private readonly Hook<PlaySpecificSoundDelegate>? playSpecificSoundHook;
    private readonly ILogService _log;

    private static readonly int ResourceDataOffset = Marshal.SizeOf<ResourceHandle>();
    private static readonly int SoundDataOffset = Marshal.SizeOf<nint>();

    private const string SoundContainerFileNameSuffix = ".scd";

    private static readonly Regex IgnoredSoundFileNameRegex = new(
        @"^(bgcommon|music|sound/(battle|foot|instruments|strm|vfx|voice/Vo_Emote|zingle))/");

    private static readonly Regex VoiceLineFileNameRegex = new(@"^cut/.*/(vo_|voice)");
    private static readonly Regex BattleVoiceLineFileNameRegex = new(@"^sound/.*/(Vo_Line)");
    private readonly HashSet<nint> knownVoiceLinePtrs = new();
    private readonly Dictionary<nint, string> knownVoiceLinesMap = new();

    /// <summary>Invoked when a Talk/dialogue voice line begins playing.</summary>
    public event Action? TalkVoiceLine;

    /// <summary>Invoked when a BattleTalk or Bubble voice line begins playing.</summary>
    public event Action? BattleBubbleVoiceLine;

    public SoundHelper(ILogService log, ISigScanner sigScanner, IGameInteropProvider gameInteropProvider)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));

        if (sigScanner.TryScanText(LoadSoundFileSig, out var loadSoundFilePtr))
        {
            loadSoundFileHook =
                gameInteropProvider.HookFromAddress<LoadSoundFileDelegate>(loadSoundFilePtr, LoadSoundFileDetour);
            loadSoundFileHook.Enable();
            _log.Info(nameof(SoundHelper), "Hooked into LoadSoundFile", new EKEventId(0, TextSource.None));
        }
        else
        {
            _log.Error(nameof(SoundHelper), "Failed to hook into LoadSoundFile", new EKEventId(0, TextSource.None));
        }

        if (sigScanner.TryScanText(PlaySpecificSoundSig, out var playSpecificSoundPtr))
        {
            playSpecificSoundHook =
                gameInteropProvider.HookFromAddress<PlaySpecificSoundDelegate>(playSpecificSoundPtr, PlaySpecificSoundDetour);
            playSpecificSoundHook.Enable();
            _log.Info(nameof(SoundHelper), "Hooked into PlaySpecificSound", new EKEventId(0, TextSource.None));
        }
        else
        {
            _log.Error(nameof(SoundHelper), "Failed to hook into PlaySpecificSound", new EKEventId(0, TextSource.None));
        }
    }

    public void Dispose()
    {
        loadSoundFileHook?.Dispose();
        playSpecificSoundHook?.Dispose();
    }

    private nint LoadSoundFileDetour(nint resourceHandlePtr, uint arg2)
    {
        var result = loadSoundFileHook!.Original(resourceHandlePtr, arg2);

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
                        if (VoiceLineFileNameRegex.IsMatch(fileName) || BattleVoiceLineFileNameRegex.IsMatch(fileName))
                        {
                            isVoiceLine = true;
                        }
                    }

                    if (isVoiceLine)
                    {
                        _log.Debug(nameof(LoadSoundFileDetour), $"Discovered voice line at address {resourceDataPtr:x}: {fileName}", new EKEventId(0, TextSource.None));
                        knownVoiceLinePtrs.Add(resourceDataPtr);
                        knownVoiceLinesMap.Add(resourceDataPtr, fileName);
                    }
                    else
                    {
                        // Addresses can be reused, so a non-voice-line sound may be loaded to an address previously
                        // occupied by a voice line.
                        if (knownVoiceLinePtrs.Remove(resourceDataPtr))
                        {
                            knownVoiceLinesMap.Remove(resourceDataPtr);
                            _log.Debug(nameof(LoadSoundFileDetour), $"Cleared voice line from address {resourceDataPtr:x} (reused by: {fileName})", new EKEventId(0, TextSource.None));
                        }
                    }
                }
            }
        }
        catch (Exception exc)
        {
            _log.Error(nameof(LoadSoundFileDetour), exc.ToString(), new EKEventId(0, TextSource.None));
        }

        return result;
    }

    private nint PlaySpecificSoundDetour(nint soundPtr, int arg2)
    {
        var result = playSpecificSoundHook!.Original(soundPtr, arg2);

        try
        {
            var soundDataPtr = Marshal.ReadIntPtr(soundPtr + SoundDataOffset);
            // Assume that a voice line will be played only once after it's loaded. Then the set can be pruned as voice
            // lines are played.
            if (knownVoiceLinePtrs.Remove(soundDataPtr))
            {
                knownVoiceLinesMap.TryGetValue(soundDataPtr, out var fileName);
                knownVoiceLinesMap.Remove(soundDataPtr);

                if (Path.GetFileNameWithoutExtension(fileName)?.Length == 10)
                {
                    _log.Debug(nameof(PlaySpecificSoundDetour), $"Battle/bubble voice line at {soundDataPtr:x}: {fileName}", new EKEventId(0, TextSource.AddonBattleTalk));
                    BattleBubbleVoiceLine?.Invoke();
                }
                else
                {
                    _log.Debug(nameof(PlaySpecificSoundDetour), $"Talk voice line at {soundDataPtr:x}: {fileName}", new EKEventId(0, TextSource.AddonTalk));
                    TalkVoiceLine?.Invoke();
                }
            }
        }
        catch (Exception exc)
        {
            _log.Error(nameof(PlaySpecificSoundDetour), exc.ToString(), new EKEventId(0, TextSource.None));
        }

        return result;
    }
}
