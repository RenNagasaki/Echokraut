using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Sentence continuation [H1]: two consecutive sheet boxes (no player placeholder) go through the
/// pipeline as <see cref="TextSource.AddonTalk"/> self-test speech. Box 1 must get the head of box 2
/// appended, box 2 must lose it. Asserted on the queued (cleaned) text against the same boxes sent with
/// the option off; the lines are cancelled, not played.
/// </summary>
public sealed class ContinuationScenario : E2eScenario
{
    private readonly Configuration _config;
    private readonly IDialogContinuationService _continuation;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public ContinuationScenario(Configuration config, IDialogContinuationService continuation,
        IAudioPlaybackService playback, E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _continuation = continuation;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "continuation";

    // Building the index scans every dialogue sheet (~30 s on the gaming PC).
    public override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var sent = new List<SelfTestSpeech>();
        const string option = nameof(Configuration.JoinSplitSentences);
        try
        {
            var language = _speaker.Language;
            await _continuation.Prepare(language).WaitAsync(ct).ConfigureAwait(false);
            if (_continuation.SamplePair(language) is not { } pair)
                return SelfTestResult.Fail($"The continuation index ({language}) has no pair of boxes without placeholders.");

            // Baseline: the same boxes with the option off. The spoken text is cleaned (roman numerals,
            // numbers, phonetics), so the pipeline itself says what each box sounds like on its own.
            E2eOptions.Set(ctx.State, _config, option, false);
            ctx.Save();
            var alone = await SendPairAsync(pair, sent).ConfigureAwait(false);
            E2eOptions.Set(ctx.State, _config, option, true);
            ctx.Save();
            ct.ThrowIfCancellationRequested();
            // In display order, one after the other: the service strips box 2 by what it appended to box 1.
            var joined = await SendPairAsync(pair, sent).ConfigureAwait(false);
            return Judge(pair, alone.First, alone.Second, joined.First, joined.Second);
        }
        finally
        {
            _playback.ClearQueue(TextSource.AddonTalk);
            E2eOptions.Restore(ctx.State, _config, [option]);
            ctx.Save();
            foreach (var s in sent) _speaker.RemoveTraces(s);
        }
    }

    private async Task<(SelfTestSpeech First, SelfTestSpeech Second)> SendPairAsync(ContinuationPair pair, List<SelfTestSpeech> sent)
    {
        var first = await _speaker.SendAsync(TextSource.AddonTalk, pair.First).ConfigureAwait(false);
        sent.Add(first);
        var second = await _speaker.SendAsync(TextSource.AddonTalk, pair.Second).ConfigureAwait(false);
        sent.Add(second);
        _playback.ClearQueue(TextSource.AddonTalk);
        return (first, second);
    }

    /// <summary>
    /// Joined box 1 = box 1 alone plus more; joined box 2 = the end of box 2 alone, shorter. Compared
    /// on the spoken (cleaned) text, normalised to letters and digits.
    /// </summary>
    internal static SelfTestResult Judge(ContinuationPair pair, SelfTestSpeech alone1, SelfTestSpeech alone2,
        SelfTestSpeech joined1, SelfTestSpeech joined2)
    {
        if (alone1.Queued is not { } a1 || alone2.Queued is not { } a2)
            return SelfTestResult.Fail($"A box was not queued with the option off ({(alone1.Queued == null ? alone1 : alone2).WhyNotQueued()}).");
        if (joined1.Queued is not { } box1)
            return SelfTestResult.Fail($"Box 1 was not queued ({joined1.WhyNotQueued()}).");
        if (joined2.Queued is not { } box2)
            return SelfTestResult.Fail($"Box 2 was not queued ({joined2.WhyNotQueued()}).");

        var base1 = SentenceContinuation.Normalize(a1.Text);
        var base2 = SentenceContinuation.Normalize(a2.Text);
        var text1 = SentenceContinuation.Normalize(box1.Text);
        var text2 = SentenceContinuation.Normalize(box2.Text);
        if (!box1.SentenceJoined || text1.Length <= base1.Length || !text1.StartsWith(base1, StringComparison.Ordinal))
            return SelfTestResult.Fail($"Box 1 did not get \"{pair.Head}\" appended: \"{Short(box1.Text)}\".");
        if (text2.Length >= base2.Length || !base2.EndsWith(text2, StringComparison.Ordinal))
            return SelfTestResult.Fail($"Box 2 did not lose \"{pair.Head}\": \"{Short(box2.Text)}\".");
        return SelfTestResult.Pass($"Box 1 + \"{Short(pair.Head)}\", box 2 without it: \"{Short(box2.Text)}\".");
    }

    private static string Short(string? text) => text is { Length: > 60 } t ? t[..60] + "…" : text ?? "";
}
