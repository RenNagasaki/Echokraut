using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IBackendService
{
    /// <summary>Fired after voices are mapped/refreshed so the UI can refresh its voice list.</summary>
    event Action? VoicesMapped;

    /// <summary>Fired when a new NPC/player/bubble mapping is added so the UI can refresh its lists.</summary>
    event Action? CharacterMapped;

    bool IsBackendAvailable();
    void ProcessVoiceMessage(VoiceMessage voiceMessage);
    Task<bool> GenerateVoice(VoiceMessage message);
    Task<string> CheckReady(EKEventId eventId);
    void CancelAll();
    void Cancel(VoiceMessage message);
    void Pause(VoiceMessage message);
    void Resume(VoiceMessage message);
    void GetVoiceOrRandom(EKEventId eventId, NpcMapData npcData);
    void RefreshBackend();
    void SetBackendType(TTSBackends backendType);
    bool ReloadService(string reloadModel, EKEventId eventId);

    /// <summary>Called by NpcDataHelper when a new character mapping is created.</summary>
    void NotifyCharacterMapped();
}
