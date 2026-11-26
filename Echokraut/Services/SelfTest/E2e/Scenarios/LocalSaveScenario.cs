using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Local saving (test plan section 7): the same FC chat line twice. The first is generated and written as a
/// WAV; the second must be loaded from that WAV (<see cref="VoiceMessage.LoadedLocally"/>) and play. Chat, not
/// self-test speech: self-test lines are never saved.
/// </summary>
public sealed class LocalSaveScenario : E2eScenario
{
    private readonly Configuration _config;
    private readonly E2eChatLines _lines;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public LocalSaveScenario(Configuration config, E2eChatLines lines, IVoiceMessageQueue queue, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _lines = lines;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "local-save";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(4);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (_lines.Unusable() is { } unusable) return unusable;
        if (!_config.SaveToLocal || string.IsNullOrWhiteSpace(_config.LocalSaveLocation))
            return SelfTestResult.Skip("Local saving is off in the E2E config.");
        var name = _lines.OwnName!;

        foreach (var option in ChatFcScenario.Options) E2eOptions.Set(ctx.State, _config, option, true);
        ctx.Save();
        VoiceMessage? first = null;
        VoiceMessage? second = null;
        string? wav = null;
        var newCharacter = _lines.IsNewCharacter(name);
        var text = _speaker.TestText;
        try
        {
            var entry = await PrintAndWaitAsync(text, ct).ConfigureAwait(false);
            if (entry == null) return NotQueued("first");
            first = entry.Message;
            _lines.Record(ctx, first, newCharacter, name);
            if (first.LoadedLocally)
                return SelfTestResult.Fail("The first line was already loaded from disk - a WAV of an earlier run was left behind.");
            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "first line", ct).ConfigureAwait(false) is { } notPlayed)
                return SelfTestResult.Fail(notPlayed);
            wav = await _lines.WaitForWavAsync(ctx, first, ct).ConfigureAwait(false);
            if (wav == null)
                return SelfTestResult.Fail($"No WAV within {E2eChatLines.WavWait.TotalSeconds:0} s after the first line played.");

            entry = await PrintAndWaitAsync(text, ct).ConfigureAwait(false);
            if (entry == null) return NotQueued("second");
            second = entry.Message;
            if (!second.LoadedLocally)
                return SelfTestResult.Fail($"The second line was generated again instead of loaded from {System.IO.Path.GetFileName(wav)}.");
            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "second line", ct).ConfigureAwait(false) is { } secondNotPlayed)
                return SelfTestResult.Fail(secondNotPlayed);
            return SelfTestResult.Pass($"First line generated and saved, the second loaded from the WAV and played (clip {first.VoiceClipId}).");
        }
        finally
        {
            if (ct.IsCancellationRequested)
            {
                _playback.StopPlaying(first);
                _playback.StopPlaying(second);
            }
            E2eOptions.Restore(ctx.State, _config, ChatFcScenario.Options);
            _lines.RemoveTraces(ctx, first, wav, newCharacter, _lines.OwnName ?? name);
            ctx.Save();
        }
    }

    private async Task<VoiceMessageEntry?> PrintAndWaitAsync(string text, CancellationToken ct)
    {
        var since = DateTime.UtcNow;
        await _lines.PrintAsync(XivChatType.FreeCompany, _lines.OwnName!, text).ConfigureAwait(false);
        return await _lines.WaitForEntryAsync(text, since, E2eChatLines.QueueWait, ct).ConfigureAwait(false);
    }

    private static SelfTestResult NotQueued(string which) =>
        SelfTestResult.Fail($"The {which} printed FC line did not reach the queue within {E2eChatLines.QueueWait.TotalSeconds:0} s.");
}
