using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Sound;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System.Reflection;
using Echokraut.DataClasses;

namespace Echokraut.Helper
{
    internal static class VolumeHelper
    {
        private static IGameConfig GameConfig;

        public static void Setup(IGameConfig gameConfig)
        {
            GameConfig = gameConfig;
        }

        public static unsafe float GetVoiceVolume(EKEventId eventId)
        {
            var voiceVolume = .5f;
            var masterVolume = .5f;
            var instance = Framework.Instance();
            if (instance != null && instance->SoundManager != null)
            {
                var soundManager = instance->SoundManager;
                masterVolume = soundManager->MasterVolume;
                voiceVolume = soundManager->GetEffectiveVolume(SoundManager.SoundChannel.Voice);
                var isMasterMuted = false;
                var isVoiceMuted = false;

                GameConfig.System.TryGetBool("IsSndMaster", out isMasterMuted);
                GameConfig.System.TryGetBool("IsSndVoice", out isVoiceMuted);


                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Master volume: {(isMasterMuted ? 0f : masterVolume)}", eventId);
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Voice volume: {(isVoiceMuted ? 0f : voiceVolume)}", eventId);

                if (isMasterMuted || isVoiceMuted)
                    return 0f;
            }

            var volumeFloat = masterVolume * voiceVolume;
            LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Real voice volume: {volumeFloat}", eventId);
            return volumeFloat;
        }
    }
}
