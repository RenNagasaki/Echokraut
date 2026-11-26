using System;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// NPC mute: a self-test Talk line is queued; with its speaker muted (the NPC toggle the UI saves,
/// <see cref="NpcMapData.IsEnabled"/>) the same line is not queued; unmuted it is queued again. Talk
/// source because the mute rule is per source and the self-test source has none; the lines are
/// cancelled, not played. The per-instance dialogue mute needs a game object (base id) and is not covered.
/// </summary>
public sealed class MuteScenario : E2eScenario
{
    private readonly Configuration _config;
    private readonly INpcDataService _npcData;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public MuteScenario(Configuration config, INpcDataService npcData, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _npcData = npcData;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "mute";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(1);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        SelfTestSpeech? baseline = null;
        try
        {
            baseline = await SendAsync().ConfigureAwait(false);
            if (baseline.Queued == null || baseline.Speaker is not { } npc)
                return SelfTestResult.Fail($"Unmuted line was not queued ({baseline.WhyNotQueued()}).");

            npc.IsEnabled = false;
            _npcData.SaveCharacter(npc);
            ct.ThrowIfCancellationRequested();
            var muted = await SendAsync().ConfigureAwait(false);
            if (muted.Queued != null)
                return SelfTestResult.Fail("Line of a muted speaker was queued.");
            if (muted.Error != null)
                return SelfTestResult.Fail($"The pipeline threw {muted.Error}");

            npc.IsEnabled = true;
            _npcData.SaveCharacter(npc);
            ct.ThrowIfCancellationRequested();
            var unmuted = await SendAsync().ConfigureAwait(false);
            return unmuted.Queued != null
                ? SelfTestResult.Pass("Queued, not queued while muted, queued again after unmute.")
                : SelfTestResult.Fail($"Line was not queued after unmute ({unmuted.WhyNotQueued()}).");
        }
        finally
        {
            if (baseline?.Speaker is { IsEnabled: false } npc)
            {
                npc.IsEnabled = true;
                _npcData.SaveCharacter(npc);
            }
            _speaker.RemoveTraces(baseline);
        }
    }

    /// <summary>One Talk line, cancelled at once: only "was it queued" matters.</summary>
    private async Task<SelfTestSpeech> SendAsync()
    {
        var speech = await _speaker.SendAsync(TextSource.AddonTalk, _speaker.TestText).ConfigureAwait(false);
        _playback.ClearQueue(TextSource.AddonTalk);
        return speech;
    }
}
