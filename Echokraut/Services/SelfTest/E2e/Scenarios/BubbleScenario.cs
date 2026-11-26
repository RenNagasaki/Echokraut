using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Speech bubbles (test plan section 5), passive: the character stays where it is; with bubbles on (also in
/// cities) the scenario waits up to <see cref="BubbleWait"/> for a bubble line of a nearby NPC and needs it to
/// play. No bubble in that time (inn, empty area) is a skip, not a fail. The line is a real one; its rows stay
/// in the E2E database, which the restore replaces.
/// </summary>
public sealed class BubbleScenario : E2eScenario
{
    internal static TimeSpan BubbleWait { get; set; } = TimeSpan.FromMinutes(2);

    internal static readonly string[] Options = [nameof(Configuration.VoiceBubble), nameof(Configuration.VoiceBubblesInCity)];

    private readonly Configuration _config;
    private readonly IVoiceMessageQueue _queue;

    public BubbleScenario(Configuration config, IVoiceMessageQueue queue)
    {
        _config = config;
        _queue = queue;
    }

    public override string Id => "bubble";
    public override TimeSpan Timeout => BubbleWait + TimeSpan.FromMinutes(2);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        if (!_config.Enabled) return SelfTestResult.Skip("Echokraut is switched off (/ekt).");
        foreach (var option in Options) E2eOptions.Set(ctx.State, _config, option, true);
        ctx.Save();
        try
        {
            var since = DateTime.UtcNow;
            ctx.Progress($"waiting up to {BubbleWait.TotalSeconds:0} s for a speech bubble");
            VoiceMessageEntry? entry = null;
            var until = since + BubbleWait;
            while (entry == null && DateTime.UtcNow < until)
            {
                entry = PlaybackWatch.FindEntry(_queue, e => e.Message.Source == TextSource.AddonBubble && e.QueuedAt >= since);
                if (entry == null) await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
            }
            if (entry == null) return SelfTestResult.Skip($"No speech bubble within {BubbleWait.TotalSeconds:0} s here.");
            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "bubble line", ct).ConfigureAwait(false) is { } notPlayed)
                return SelfTestResult.Fail(notPlayed);
            return SelfTestResult.Pass($"Bubble of '{entry.Message.Speaker?.Name}' played ({(entry.Message.Is3D ? "3D" : "2D")}).");
        }
        finally
        {
            E2eOptions.Restore(ctx.State, _config, Options);
            ctx.Save();
        }
    }
}
