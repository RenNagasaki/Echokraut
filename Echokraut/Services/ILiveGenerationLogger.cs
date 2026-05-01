using Echokraut.DataClasses;

namespace Echokraut.Services;

/// <summary>
/// Records a <c>voice_clip_generations</c> row for an audio file produced by the live
/// playback path (chat / addon talk / bubble) once it has been written to disk.
/// The bulk Voice Clip Manager path has its own equivalent in <c>VoiceClipManagerService</c>;
/// extracting this responsibility keeps <c>AudioPlaybackService</c> from having to know
/// about <c>IDatabaseService</c> and <c>IGameObjectService</c> directly.
/// </summary>
public interface ILiveGenerationLogger
{
    /// <summary>
    /// Logs a generation row for the just-saved live audio, attributed to <paramref name="voiceClipId"/>.
    /// <paramref name="hasPlayerPlaceholder"/> selects the same <c>player_content_id</c> the UI uses
    /// in <c>VoiceClipManagerService.GetEffectivePlayerId</c>: 0 for shareable clips, the local
    /// player's content id for placeholder clips. Storing under a different id leaves the row
    /// invisible to the manager UI.
    /// No-op when <paramref name="voiceClipId"/> is 0 (e.g. VoiceTest playback or harvest-only clips).
    /// Failures are swallowed and logged at warning level — never bubbles up to the playback loop.
    /// </summary>
    void LogIfApplicable(int voiceClipId, bool hasPlayerPlaceholder, string savePath, EKEventId eventId);
}
