using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The game-voice part of the stage <c>remote</c> (plan › Stages row 7 [H7]): one line for a voiced NPC goes
/// through the real pipeline and must be generated with that NPC's own game voice. Everything game-audio
/// related stays inside <see cref="IGameVoiceReferenceService"/> (root CLAUDE.md › hard constraint): it picks
/// the NPC from the text index, ranks that NPC's clips (decoded in memory, only ids stored) and hands the
/// reference to the backend. The line is self-test speech (never saved, never on Drive); its clip row and,
/// when the run created it, the character are deleted by id right after.
/// </summary>
public sealed class E2eGameVoiceGeneration
{
    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly IDatabaseService _db;
    private readonly INpcDataService _npcData;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public E2eGameVoiceGeneration(IGameVoiceReferenceService gameVoice, IDatabaseService db, INpcDataService npcData,
        IVoiceMessageQueue queue, IAudioPlaybackService playback, E2eSelfTestSpeaker speaker)
    {
        _gameVoice = gameVoice;
        _db = db;
        _npcData = npcData;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    /// <summary>Ranking with Whisper on the remote plus one generation.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromMinutes(10);

    public async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var language = _speaker.Language;
        var name = await _gameVoice.SampleVoicedNpcNameAsync(language, ct).ConfigureAwait(false);
        if (name == null) return SelfTestResult.Fail($"No voiced NPC with its own token in the text index ({language}).");
        ctx.Progress($"game voice: NPC {name}, ranking its clips");

        var eventId = new EKEventId(0, TextSource.VoiceTest);
        var stored = await _gameVoice.RankAsync(new GameVoiceSpeaker(0, name, 0, language), eventId, ct).ConfigureAwait(false);
        if (stored == 0) return SelfTestResult.Fail($"Ranking {name} stored no clip (see log).");
        ctx.Progress($"game voice: {stored} clip(s) ranked for {name}");

        var newCharacter = !_npcData.MappedNpcs.Any(n => n.Name == name);
        SelfTestSpeech? speech = null;
        try
        {
            speech = await _speaker.SendAsync(TextSource.VoiceTest, _speaker.TestText, name).ConfigureAwait(false);
            if (newCharacter) ctx.State.CreatedIds.Add($"character:{name}");
            if (speech.Queued is not { } message)
                return SelfTestResult.Fail($"The line for {name} was not queued: {speech.WhyNotQueued()}.");
            if (message.VoiceClipId > 0) ctx.State.CreatedIds.Add($"clip:{message.VoiceClipId}");
            ctx.Save();

            var entry = await PlaybackWatch.WaitForEntryAsync(_queue, e => ReferenceEquals(e.Message, message), ct).ConfigureAwait(false);
            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "game-voice line", ct).ConfigureAwait(false) is { } notPlayed)
                return SelfTestResult.Fail(notPlayed);

            return message.ReferenceSource == ReferenceSource.GameVoice
                ? SelfTestResult.Pass($"{name}: {stored} clip(s) ranked, line spoken with the game voice (clip {message.VoiceClipId}, voice {message.Speaker.voice}).")
                : SelfTestResult.Fail($"{name}: {stored} clip(s) ranked, but the line was spoken with {message.ReferenceSource} (server refused the reference? see log).");
        }
        finally
        {
            if (speech?.Queued is { } queued && ct.IsCancellationRequested) _playback.StopPlaying(queued);
            RemoveTraces(ctx, speech, newCharacter, name);
            ctx.Save();
        }
    }

    private void RemoveTraces(E2eContext ctx, SelfTestSpeech? speech, bool newCharacter, string name)
    {
        if (speech?.Queued is { VoiceClipId: > 0 } message && ctx.State.CreatedIds.Remove($"clip:{message.VoiceClipId}"))
            _db.DeleteVoiceClip(message.VoiceClipId);
        if (newCharacter && ctx.State.CreatedIds.Remove($"character:{name}"))
            SelfTestTraces.RemoveCharacter(_db, _npcData, speech?.Speaker, name);
    }
}
