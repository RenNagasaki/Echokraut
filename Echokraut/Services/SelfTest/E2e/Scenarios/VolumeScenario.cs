using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// The game's voice volume slider (test plan section 5): one line at <see cref="High"/>, one at <see cref="Low"/>.
/// While each plays, the volume BASS reports for its channel (a float readback — never a <c>Vector3D</c>, see
/// CLAUDE.md › Rules) must equal the line's computed volume, and the two must scale like the slider. The original
/// slider value is listed in <see cref="E2eState.CreatedIds"/> first, so the cleanup stage restores it after a crash.
/// </summary>
public sealed class VolumeScenario : E2eScenario
{
    internal const uint High = 100;
    internal const uint Low = 50;
    internal const float Tolerance = 0.02f;
    /// <summary>The sound manager picks a changed slider up on a later frame.</summary>
    internal static TimeSpan SettleDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    internal static TimeSpan PlayingWait { get; set; } = TimeSpan.FromSeconds(60);
    internal static TimeSpan ChannelWait { get; set; } = TimeSpan.FromSeconds(5);

    private readonly Configuration _config;
    private readonly IE2eGameSound _sound;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public VolumeScenario(Configuration config, IE2eGameSound sound, IVoiceMessageQueue queue, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _sound = sound;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "volume";
    public override TimeSpan Timeout => PlayingWait * 2 + TimeSpan.FromMinutes(1);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        if (_sound.Muted) return SelfTestResult.Skip("Master or voice sound is muted in the game.");
        if (_config.GlobalVolume <= 0) return SelfTestResult.Skip("Echokraut's own volume is 0.");
        if (_sound.VoiceVolume is not { } original) return SelfTestResult.Fail("The game's voice volume (SoundVoice) is unreadable.");
        var id = E2eGameSoundIds.Id(original);
        ctx.State.CreatedIds.Add(id);
        ctx.Save();
        try
        {
            var (high, highError) = await PlayAtAsync(High, ct).ConfigureAwait(false);
            if (highError != null) return SelfTestResult.Fail($"Slider {High}: {highError}");
            var (low, lowError) = await PlayAtAsync(Low, ct).ConfigureAwait(false);
            if (lowError != null) return SelfTestResult.Fail($"Slider {Low}: {lowError}");
            return Judge(high, low);
        }
        finally
        {
            await _sound.SetVoiceVolumeAsync(original).ConfigureAwait(false);
            if (_sound.VoiceVolume == original) ctx.State.CreatedIds.Remove(id);
            ctx.Save();
        }
    }

    /// <summary>The two channel volumes must scale like <see cref="Low"/>/<see cref="High"/>.</summary>
    internal static SelfTestResult Judge(float high, float low)
    {
        if (high <= 0) return SelfTestResult.Fail(FormattableString.Invariant($"Channel volume {high:0.###} at slider {High}."));
        var ratio = low / high;
        var expected = (float)Low / High;
        return Math.Abs(ratio - expected) <= 0.1f
            ? SelfTestResult.Pass(FormattableString.Invariant($"Channel volume {high:0.###} at {High}, {low:0.###} at {Low} (ratio {ratio:0.00})."))
            : SelfTestResult.Fail(FormattableString.Invariant($"Channel volume {high:0.###} at {High}, {low:0.###} at {Low}: ratio {ratio:0.00} instead of {expected:0.00}."));
    }

    private async Task<(float Volume, string? Error)> PlayAtAsync(uint slider, CancellationToken ct)
    {
        await _sound.SetVoiceVolumeAsync(slider).ConfigureAwait(false);
        await Task.Delay(SettleDelay, ct).ConfigureAwait(false);
        var speech = await _speaker.SendAsync(TextSource.VoiceTest, _speaker.TestText).ConfigureAwait(false);
        try
        {
            if (speech.Queued is not { } message) return (0, $"line not queued ({speech.WhyNotQueued()})");
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(PlayingWait);
            VoiceMessageEntry entry;
            try
            {
                entry = await PlaybackWatch.WaitForPlayingAsync(_queue, e => ReferenceEquals(e.Message, message), wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (0, $"line did not play within {PlayingWait.TotalSeconds:0} s");
            }
            // The queue marks the entry playing before the stream exists and before its volume is set.
            float? channel = null;
            var until = DateTime.UtcNow + ChannelWait;
            while (DateTime.UtcNow < until)
            {
                channel = _playback.ChannelVolume(entry.Message.StreamId);
                if (channel is { } now && Math.Abs(now - message.Volume) <= Tolerance) return (now, null);
                await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
            }
            if (channel is not { } read) return (0, "no BASS channel while playing");
            return (read, FormattableString.Invariant($"BASS channel volume {read:0.###} but the line's volume is {message.Volume:0.###}"));
        }
        finally
        {
            _playback.ClearQueue(TextSource.VoiceTest);
            _speaker.RemoveTraces(speech);
        }
    }
}
