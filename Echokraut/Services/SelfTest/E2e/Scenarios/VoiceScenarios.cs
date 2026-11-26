using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// "Reload voices" (test plan section 6): the same calls as the button on the Reset-Data tab
/// (<see cref="IBackendService.SetBackendType"/> + <see cref="IBackendService.NotifyCharacterMapped"/>, off the
/// framework thread). The voice list must come back with the same keys — a reload must neither drop nor duplicate voices.
/// </summary>
public sealed class ReloadVoicesScenario : E2eScenario
{
    private readonly Configuration _config;
    private readonly IBackendService _backend;
    private readonly INpcDataService _npcData;

    public ReloadVoicesScenario(Configuration config, IBackendService backend, INpcDataService npcData)
    {
        _config = config;
        _backend = backend;
        _npcData = npcData;
    }

    public override string Id => "reload-voices";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(1);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        var before = Keys();
        if (before.Count == 0) return SelfTestResult.Fail("No voices before the reload.");
        await VoiceReload.RunAsync(_config, _backend).ConfigureAwait(false);
        var after = Keys();
        var lost = before.Except(after).ToList();
        var added = after.Except(before).ToList();
        var duplicates = _npcData.GetEchokrautVoices().GroupBy(v => v.BackendVoice, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (lost.Count == 0 && added.Count == 0 && duplicates.Count == 0)
            return SelfTestResult.Pass($"{after.Count} voices before and after the reload, same keys.");
        var problems = new List<string>();
        if (lost.Count > 0) problems.Add($"lost {string.Join(", ", lost.Take(5))}");
        if (added.Count > 0) problems.Add($"new {string.Join(", ", added.Take(5))}");
        if (duplicates.Count > 0) problems.Add($"twice {string.Join(", ", duplicates.Take(5))}");
        return SelfTestResult.Fail($"{before.Count} voices before, {_npcData.GetEchokrautVoices().Count} after: {string.Join("; ", problems)}.");
    }

    private HashSet<string> Keys() =>
        _npcData.GetEchokrautVoices().Select(v => v.BackendVoice).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The "Reload voices" button's calls, off the framework thread (the voice-list request blocks).</summary>
internal static class VoiceReload
{
    public static Task RunAsync(Configuration config, IBackendService backend) => Task.Run(() =>
    {
        backend.SetBackendType(config.BackendSelection);
        backend.NotifyCharacterMapped();
    });
}

/// <summary>
/// Voice reassignment (test plan section 6): the throwaway speaker gets another voice of its selectable list through
/// <see cref="INpcDataService.SaveCharacter"/> (what the voice selection saves); the next line must use it, and
/// it must survive a "reload voices". Never the narrator voice: <c>PickVoice</c> treats it as "no choice yet".
/// </summary>
public sealed class VoiceReassignScenario : E2eScenario
{
    private readonly Configuration _config;
    private readonly IBackendService _backend;
    private readonly INpcDataService _npcData;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public VoiceReassignScenario(Configuration config, IBackendService backend, INpcDataService npcData,
        IAudioPlaybackService playback, E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _backend = backend;
        _npcData = npcData;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "voice-reassign";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(2);

    /// <summary>An enabled voice other than <paramref name="current"/> and the narrator that fits <paramref name="npc"/>.</summary>
    internal static EchokrautVoice? Other(IEnumerable<EchokrautVoice> voices, NpcMapData npc, string? current, string? narrator,
        Func<EchokrautVoice, NpcMapData, bool> fits) =>
        voices.FirstOrDefault(v => v.IsEnabled
            && !string.Equals(v.BackendVoice, current, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(v.BackendVoice, narrator, StringComparison.OrdinalIgnoreCase)
            && fits(v, npc));

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        SelfTestSpeech? first = null;
        try
        {
            first = await SendAsync().ConfigureAwait(false);
            if (first.Speaker is not { } npc || first.Queued == null)
                return SelfTestResult.Fail($"First line not queued ({first.WhyNotQueued()}).");
            // The NPC's selectable list (what the voice dropdown offers): a voice outside it reads as unfitting and is re-rolled.
            var voices = npc.Voices ?? [];
            var narrator = NarratorVoicePolicy.Resolve(_npcData.GetEchokrautVoices(), _config.NarratorVoice)?.BackendVoice;
            var target = Other(voices, npc, npc.voice, narrator, _backend.IsVoiceFittingForNpc);
            if (target == null) return SelfTestResult.Skip($"No second selectable voice fits '{npc.Name}' ({voices.Count} selectable).");
            var old = npc.voice;

            npc.Voice = target;
            _npcData.SaveCharacter(npc);
            ct.ThrowIfCancellationRequested();
            var second = await SendAsync().ConfigureAwait(false);
            if (Mismatch(second, target.BackendVoice, "after the reassignment") is { } wrong) return wrong;

            await VoiceReload.RunAsync(_config, _backend).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var third = await SendAsync().ConfigureAwait(false);
            if (Mismatch(third, target.BackendVoice, "after 'reload voices'") is { } lost) return lost;
            return SelfTestResult.Pass($"'{old}' -> '{target.BackendVoice}': the next line and the one after 'reload voices' use it.");
        }
        finally
        {
            _speaker.RemoveTraces(first);
        }
    }

    internal static SelfTestResult? Mismatch(SelfTestSpeech speech, string expected, string when)
    {
        if (speech.Queued is not { } message) return SelfTestResult.Fail($"Line {when} not queued ({speech.WhyNotQueued()}).");
        var used = message.Speaker?.voice;
        return string.Equals(used, expected, StringComparison.OrdinalIgnoreCase)
            ? null
            : SelfTestResult.Fail($"Line {when} uses '{used}' instead of '{expected}'.");
    }

    private async Task<SelfTestSpeech> SendAsync()
    {
        // Talk, not VoiceTest: VoiceTest lines map to no character cache, every one is a new speaker with a random voice.
        var speech = await _speaker.SendAsync(TextSource.AddonTalk, _speaker.TestText).ConfigureAwait(false);
        _playback.ClearQueue(TextSource.AddonTalk);
        return speech;
    }
}
