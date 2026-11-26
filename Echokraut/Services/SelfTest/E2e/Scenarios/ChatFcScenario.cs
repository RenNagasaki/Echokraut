using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Chat through the real pipeline (plan › Chat scenario): an FC line with the own name is printed into
/// the local chat log and must reach the queue as chat and play. What it creates — the own character (if new),
/// the clip row, the WAV — goes into <see cref="E2eState.CreatedIds"/> first and is removed at the end;
/// an id stays listed only when its removal did not happen (crash), for the cleanup stage.
/// </summary>
public sealed class ChatFcScenario : E2eScenario
{
    internal static readonly string[] Options =
        [nameof(Configuration.VoiceChat), nameof(Configuration.VoiceChatPlayer), nameof(Configuration.VoiceChatFreeCompany)];

    private readonly Configuration _config;
    private readonly E2eChatLines _lines;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public ChatFcScenario(Configuration config, E2eChatLines lines, IVoiceMessageQueue queue, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _lines = lines;
        _queue = queue;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "chat-fc";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (_lines.Unusable() is { } unusable) return unusable;
        var name = _lines.OwnName!;

        foreach (var option in Options) E2eOptions.Set(ctx.State, _config, option, true);
        ctx.Save();
        VoiceMessage? message = null;
        string? wav = null;
        var newCharacter = _lines.IsNewCharacter(name);
        try
        {
            var text = _speaker.TestText;
            var since = DateTime.UtcNow;
            await _lines.PrintAsync(XivChatType.FreeCompany, name, text).ConfigureAwait(false);
            var entry = await _lines.WaitForEntryAsync(text, since, E2eChatLines.QueueWait, ct).ConfigureAwait(false);
            if (entry == null)
                return SelfTestResult.Fail($"The printed FC line did not reach the queue within {E2eChatLines.QueueWait.TotalSeconds:0} s (see log).");
            message = entry.Message;
            _lines.Record(ctx, message, newCharacter, name);

            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "chat line", ct).ConfigureAwait(false) is { } notPlayed)
                return SelfTestResult.Fail(notPlayed);

            wav = await _lines.WaitForWavAsync(ctx, message, ct).ConfigureAwait(false);
            var saved = message.LoadedLocally ? "no WAV (loaded from cache)"
                : !_config.SaveToLocal ? "local saving off"
                : wav != null ? "WAV saved" : $"no WAV within {E2eChatLines.WavWait.TotalSeconds:0} s";
            return SelfTestResult.Pass($"Chat line by {name} played, clip {message.VoiceClipId}, {saved}.");
        }
        finally
        {
            if (message != null && ct.IsCancellationRequested) _playback.StopPlaying(message);
            E2eOptions.Restore(ctx.State, _config, Options);
            _lines.RemoveTraces(ctx, message, wav, newCharacter, name);
            ctx.Save();
        }
    }
}
