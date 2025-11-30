using FFXIVClientStructs.FFXIV.Client.Sound;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System.Reflection;
using Echokraut.DataClasses;
using Echokraut.Helper.Data;

namespace Echokraut.Helper.Functional
{
    internal static class VolumeHelper
    {
        private static unsafe SoundManager* SoundManager;
        public static unsafe float GetVoiceVolume(EKEventId eventId)
        {
            var voiceVolume = .5f;
            var masterVolume = .5f;
            if (SoundManager == null)
            {
                var instance = Framework.Instance();
                if (instance != null && instance->SoundManager != null)
                    SoundManager = instance->SoundManager;
            }
                
            masterVolume = SoundManager->MasterVolume;
            voiceVolume = SoundManager->GetEffectiveVolume(FFXIVClientStructs.FFXIV.Client.Sound.SoundManager.SoundChannel.Voice);

            Plugin.GameConfig.System.TryGetBool("IsSndMaster", out var isMasterMuted);
            Plugin.GameConfig.System.TryGetBool("IsSndVoice", out var isVoiceMuted);

            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Master volume: {(isMasterMuted ? 0f : masterVolume)}", eventId);
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Voice volume: {(isVoiceMuted ? 0f : voiceVolume)}", eventId);

            if (isMasterMuted || isVoiceMuted)
                return 0f;

            var volumeFloat = masterVolume * voiceVolume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Real voice volume: {volumeFloat}", eventId);
            return volumeFloat;
        }
    }
}
