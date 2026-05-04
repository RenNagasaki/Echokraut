using System;
using Echokraut.DataClasses;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <inheritdoc/>
public class LiveGenerationLogger : ILiveGenerationLogger
{
    private readonly IDatabaseService _db;
    private readonly IGameObjectService _gameObjects;
    private readonly ILogService _log;

    public LiveGenerationLogger(IDatabaseService db, IGameObjectService gameObjects, ILogService log)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void LogIfApplicable(int voiceClipId, bool hasPlayerPlaceholder, string savePath, string voiceKey, EKEventId eventId)
    {
        if (voiceClipId <= 0)
        {
            _log.Debug(nameof(LogIfApplicable),
                $"Skipping generation log: voiceClipId=0 (savePath={savePath})", eventId);
            return;
        }

        // Mirror VoiceClipManagerService.GetEffectivePlayerId so the read side (HasLocalAudio,
        // GetAudioPath) finds the row.
        var playerContentId = hasPlayerPlaceholder ? (long)_gameObjects.LocalPlayerContentId : 0L;

        try
        {
            _db.LogVoiceClipGeneration(
                voiceClipId,
                playerContentId,
                _gameObjects.LocalPlayerName,
                savePath,
                voiceKey ?? "");
            _log.Debug(nameof(LogIfApplicable),
                $"Logged generation: clipId={voiceClipId}, playerContentId={playerContentId}, hasPlaceholder={hasPlayerPlaceholder}, savePath={savePath}, voiceKey={voiceKey}",
                eventId);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LogIfApplicable),
                $"Failed to log live voice clip generation (clipId={voiceClipId}): {ex.Message}", eventId);
        }
    }
}
