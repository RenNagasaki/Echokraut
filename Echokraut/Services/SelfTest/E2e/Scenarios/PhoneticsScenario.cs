using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Phonetic corrections (test plan section 6) through the same service the Phonetics tab saves with: add a
/// correction for a word of the test sentence → the queued (spoken) text has the replacement; change it → the
/// new replacement; delete it → the original word again. Lines are cancelled once queued. A correction for that
/// word that existed before is put back.
/// </summary>
public sealed class PhoneticsScenario : E2eScenario
{
    internal const string FirstReplacement = "Zebrafant";
    internal const string SecondReplacement = "Kolibrimaus";

    private readonly Configuration _config;
    private readonly INpcDataService _npcData;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public PhoneticsScenario(Configuration config, INpcDataService npcData, IAudioPlaybackService playback, E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _npcData = npcData;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "phonetics";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(1);

    /// <summary>The first word of at least four letters (whole words are what users correct); the first two characters for scripts without spaces.</summary>
    internal static string WordOf(string text) =>
        Regex.Matches(text, @"\b\p{L}{4,}\b").Select(m => m.Value).FirstOrDefault() ?? text[..Math.Min(2, text.Length)];

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        var text = _speaker.TestText;
        var word = WordOf(text);
        var before = _npcData.GetPhoneticCorrections().FirstOrDefault(c => string.Equals(c.OriginalText, word, StringComparison.OrdinalIgnoreCase));
        SelfTestSpeech? last = null;
        try
        {
            _npcData.UpsertPhoneticCorrection(word, FirstReplacement);
            last = await SendAsync(text).ConfigureAwait(false);
            if (Judge(last, "added", FirstReplacement, word) is { } added) return added;

            ct.ThrowIfCancellationRequested();
            _npcData.UpsertPhoneticCorrection(word, SecondReplacement);
            last = await SendAsync(text).ConfigureAwait(false);
            if (Judge(last, "changed", SecondReplacement, FirstReplacement) is { } changed) return changed;

            ct.ThrowIfCancellationRequested();
            _npcData.DeletePhoneticCorrection(word);
            last = await SendAsync(text).ConfigureAwait(false);
            if (Judge(last, "deleted", word, SecondReplacement) is { } deleted) return deleted;
            return SelfTestResult.Pass($"'{word}' spoken as '{FirstReplacement}', then '{SecondReplacement}', then as itself after the delete.");
        }
        finally
        {
            if (before != null) _npcData.UpsertPhoneticCorrection(before.OriginalText, before.CorrectedText);
            else if (_npcData.GetPhoneticCorrections().Any(c => string.Equals(c.OriginalText, word, StringComparison.OrdinalIgnoreCase)))
                _npcData.DeletePhoneticCorrection(word);
            _speaker.RemoveTraces(last);
        }
    }

    /// <summary>Null when the queued text has <paramref name="expected"/> and not <paramref name="gone"/>; else the fail.</summary>
    internal static SelfTestResult? Judge(SelfTestSpeech speech, string step, string expected, string gone)
    {
        if (speech.Queued is not { } message) return SelfTestResult.Fail($"Correction {step}: line not queued ({speech.WhyNotQueued()}).");
        var spoken = message.Text;
        if (!spoken.Contains(expected, StringComparison.OrdinalIgnoreCase))
            return SelfTestResult.Fail($"Correction {step}: '{expected}' not in the queued text \"{spoken}\".");
        return spoken.Contains(gone, StringComparison.OrdinalIgnoreCase)
            ? SelfTestResult.Fail($"Correction {step}: '{gone}' still in the queued text \"{spoken}\".")
            : null;
    }

    private async Task<SelfTestSpeech> SendAsync(string text)
    {
        var speech = await _speaker.SendAsync(TextSource.VoiceTest, text).ConfigureAwait(false);
        _playback.ClearQueue(TextSource.VoiceTest);
        return speech;
    }
}
