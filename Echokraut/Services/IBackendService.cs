using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IBackendService
{
    /// <summary>Fired after voices are mapped/refreshed so the UI can refresh its voice list.</summary>
    event Action? VoicesMapped;

    /// <summary>Fired when a new NPC/player/bubble mapping is added so the UI can refresh its lists.</summary>
    event Action? CharacterMapped;

    /// <summary>
    /// Config-only check — returns true as long as a backend is configured (URL set, instance type chosen).
    /// Does NOT verify the backend is actually responding. Use <see cref="IsBackendReachableAsync"/> for that.
    /// </summary>
    bool IsBackendAvailable();

    /// <summary>
    /// True health check — pings the backend (HTTP GET on AllTalk's ready endpoint, or queries the local
    /// instance status). Result is cached for 30 seconds so callers may invoke freely. For UI polling
    /// without forcing a network round-trip, use <see cref="CachedReachability"/>.
    /// </summary>
    Task<bool> IsBackendReachableAsync();

    /// <summary>
    /// Last cached reachability result, or null if no check has been performed within the cache TTL.
    /// Sync, no network calls. Intended for per-frame UI polling.
    /// </summary>
    bool? CachedReachability { get; }

    /// <summary>Force the next reachability check to bypass the cache (e.g. after a config change).</summary>
    void InvalidateReachabilityCache();
    void ProcessVoiceMessage(VoiceMessage voiceMessage);
    Task<bool> GenerateVoice(VoiceMessage message);
    Task<string> CheckReady(EKEventId eventId);
    void CancelAll();
    void Cancel(VoiceMessage message);
    void Pause(VoiceMessage message);
    void Resume(VoiceMessage message);
    void GetVoiceOrRandom(EKEventId eventId, NpcMapData npcData);
    /// <summary>
    /// Pure voice resolution: picks a voice for the given NPC from the supplied voice list
    /// without writing to the database, refreshing caches, or emitting log entries.
    /// Used by bulk operations (harvest) where the caller persists the result itself.
    /// </summary>
    EchokrautVoice? PickVoice(NpcMapData npcData, IList<EchokrautVoice> voices);

    /// <summary>
    /// True iff the voice's race/gender/body-type constraints accept the NPC.
    /// Doesn't consider name-substring or default-fallback paths.
    /// </summary>
    bool IsVoiceFittingForNpc(EchokrautVoice? voice, NpcMapData npc);

    /// <summary>
    /// Ensures the NPC has a voice assigned that actually fits its race/gender/body-type. If the
    /// current assignment is missing, disabled or no longer fits, re-picks via <see cref="PickVoice"/>
    /// and persists the new assignment to the character. Returns true if a change was made.
    /// </summary>
    bool EnsureFittingVoice(NpcMapData npcData, EKEventId eventId);
    void RefreshBackend();
    void SetBackendType(TTSBackends backendType);
    bool ReloadService(string reloadModel, EKEventId eventId);

    /// <summary>Called by NpcDataHelper when a new character mapping is created.</summary>
    void NotifyCharacterMapped();
}
