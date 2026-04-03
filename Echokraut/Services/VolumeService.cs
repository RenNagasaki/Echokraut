using Echotools.Logging.Services;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using FFXIVClientStructs.FFXIV.Client.Sound;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace Echokraut.Services;

public class VolumeService : IVolumeService
{
    private readonly IGameConfig _gameConfig;
    private readonly ILogService _logService;
    private readonly Configuration _config;

    public VolumeService(IGameConfig gameConfig, ILogService logService, Configuration config)
    {
        _gameConfig = gameConfig ?? throw new System.ArgumentNullException(nameof(gameConfig));
        _logService = logService ?? throw new System.ArgumentNullException(nameof(logService));
        _config = config ?? throw new System.ArgumentNullException(nameof(config));
    }

    public unsafe float GetVoiceVolume(EKEventId eventId)
    {
        var voiceVolume = 0.5f;
        var masterVolume = 0.5f;
        var instance = Framework.Instance();
        
        if (instance != null && instance->SoundManager != null)
        {
            var soundManager = instance->SoundManager;
            masterVolume = soundManager->MasterVolume;
            voiceVolume = soundManager->GetEffectiveVolume(SoundManager.SoundChannel.Voice);
            
            _gameConfig.System.TryGetBool("IsSndMaster", out var isMasterMuted);
            _gameConfig.System.TryGetBool("IsSndVoice", out var isVoiceMuted);

            _logService.Debug(nameof(GetVoiceVolume), $"Master volume: {(isMasterMuted ? 0f : masterVolume)}", eventId);
            _logService.Debug(nameof(GetVoiceVolume), $"Voice volume: {(isVoiceMuted ? 0f : voiceVolume)}", eventId);

            if (isMasterMuted || isVoiceMuted)
                return 0f;
        }

        var volumeFloat = masterVolume * voiceVolume * _config.GlobalVolume;
        _logService.Info(nameof(GetVoiceVolume), $"Real voice volume: {volumeFloat} (global: {_config.GlobalVolume})", eventId);
        return volumeFloat;
    }
}
