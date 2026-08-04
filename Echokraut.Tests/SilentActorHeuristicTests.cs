using System.Collections.Generic;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Unit tests for the silent-actor paren-prefix heuristic in <see cref="DialogHarvestService"/>.
/// Covers: trigger detection (text starts with "(-...-)"), per-scene candidate computation,
/// and the boundary conditions (silent-before, speaks-after).
/// </summary>
public class SilentActorHeuristicTests
{
    // ── HasParenSpeakerPrefix ────────────────────────────────────────────────

    [Theory]
    [InlineData("(-???-)Hello there.", true)]
    [InlineData("(-Sylvie-)Cough cough...", true)]
    [InlineData("(-Mysterious Voice-)Long name with spaces", true)]
    [InlineData("Just plain dialog.", false)]
    [InlineData("(NotParenForm)Hello.", false)]
    [InlineData("", false)]
    public void HasParenSpeakerPrefix_DetectsLeadingParenSpeaker(string text, bool expected)
    {
        var texts = new Dictionary<string, string> { ["en"] = text };
        Assert.Equal(expected, DialogHarvestService.HasParenSpeakerPrefix(texts));
    }

    [Fact]
    public void HasParenSpeakerPrefix_TriggersWhenAnyLanguageMatches()
    {
        // EN side-version got cut off but DE has the prefix — should still trigger.
        var texts = new Dictionary<string, string>
        {
            ["en"] = "Hello there.",
            ["de"] = "(-???-)Hallo.",
        };
        Assert.True(DialogHarvestService.HasParenSpeakerPrefix(texts));
    }

    // ── ComputeSilentActorCandidates ─────────────────────────────────────────

    private static DialogHarvestService.LuaQuestMapping BuildMapping(
        Dictionary<string, uint> actorNameToNpcId,
        List<(int funcIndex, string textKey)> calls,
        Dictionary<string, uint> resolvedTextKeyToNpcId)
    {
        var talkCalls = new List<LuabParser.TalkCall>();
        foreach (var (fi, key) in calls)
            talkCalls.Add(new LuabParser.TalkCall { FunctionIndex = fi, TextKey = key });
        return new DialogHarvestService.LuaQuestMapping(
            resolvedTextKeyToNpcId,
            talkCalls,
            actorNameToNpcId);
    }

    [Fact]
    public void OneCandidate_WhenSingleSilentActorSpeaksAfter()
    {
        // Scene 1: ACTOR0 speaks twice, then unresolved (-???-), then ACTOR1 speaks.
        // Heuristic: ACTOR1 was silent-before and speaks-after → single candidate.
        var actors = new Dictionary<string, uint> { ["ACTOR0"] = 100, ["ACTOR1"] = 200 };
        var calls = new List<(int, string)>
        {
            (1, "K1"), // ACTOR0
            (1, "K2"), // ACTOR0
            (1, "K_UNRESOLVED"), // ???
            (1, "K3"), // ACTOR1
        };
        var resolved = new Dictionary<string, uint> { ["K1"] = 100, ["K2"] = 100, ["K3"] = 200 };

        var mapping = BuildMapping(actors, calls, resolved);
        var candidates = DialogHarvestService.ComputeSilentActorCandidates("K_UNRESOLVED", mapping);

        Assert.Single(candidates);
        Assert.Equal(200u, candidates[0]);
    }

    [Fact]
    public void TwoCandidates_WhenMultipleSilentActorsSpeakAfter()
    {
        // ACTOR0 already spoke. ACTOR1 and ACTOR2 are both silent-before and both speak-after.
        // → ambiguous, two candidates.
        var actors = new Dictionary<string, uint> { ["ACTOR0"] = 100, ["ACTOR1"] = 200, ["ACTOR2"] = 300 };
        var calls = new List<(int, string)>
        {
            (1, "K1"),
            (1, "K_UNRESOLVED"),
            (1, "K2"),
            (1, "K3"),
        };
        var resolved = new Dictionary<string, uint> { ["K1"] = 100, ["K2"] = 200, ["K3"] = 300 };

        var mapping = BuildMapping(actors, calls, resolved);
        var candidates = DialogHarvestService.ComputeSilentActorCandidates("K_UNRESOLVED", mapping);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(200u, candidates);
        Assert.Contains(300u, candidates);
    }

    [Fact]
    public void ZeroCandidates_WhenAllActorsAlreadySpokeBefore()
    {
        // Both actors spoke before the (-???-) line. Neither is "silent before".
        var actors = new Dictionary<string, uint> { ["ACTOR0"] = 100, ["ACTOR1"] = 200 };
        var calls = new List<(int, string)>
        {
            (1, "K1"),
            (1, "K2"),
            (1, "K_UNRESOLVED"),
            (1, "K3"), // ACTOR0 speaks again — doesn't count as candidate (not silent-before)
        };
        var resolved = new Dictionary<string, uint> { ["K1"] = 100, ["K2"] = 200, ["K3"] = 100 };

        var mapping = BuildMapping(actors, calls, resolved);
        var candidates = DialogHarvestService.ComputeSilentActorCandidates("K_UNRESOLVED", mapping);

        Assert.Empty(candidates);
    }

    [Fact]
    public void ZeroCandidates_WhenSilentActorNeverSpeaksAfter()
    {
        // ACTOR1 is in the cast but never speaks. Not a candidate (no speaks-after evidence).
        var actors = new Dictionary<string, uint> { ["ACTOR0"] = 100, ["ACTOR1"] = 200 };
        var calls = new List<(int, string)>
        {
            (1, "K1"),
            (1, "K_UNRESOLVED"),
        };
        var resolved = new Dictionary<string, uint> { ["K1"] = 100 };

        var mapping = BuildMapping(actors, calls, resolved);
        var candidates = DialogHarvestService.ComputeSilentActorCandidates("K_UNRESOLVED", mapping);

        Assert.Empty(candidates);
    }

    [Fact]
    public void PerSceneScope_SilentInPriorScenesDoesNotDisqualify()
    {
        // ACTOR1 spoke in scene 0 (different function), but is silent-before in scene 1.
        // Heuristic operates per-scene → ACTOR1 still qualifies as candidate.
        var actors = new Dictionary<string, uint> { ["ACTOR0"] = 100, ["ACTOR1"] = 200 };
        var calls = new List<(int, string)>
        {
            (0, "S0_K1"), // scene 0: ACTOR1 spoke earlier
            (1, "S1_K1"), // scene 1: ACTOR0 speaks
            (1, "K_UNRESOLVED"), // scene 1: ???
            (1, "S1_K2"), // scene 1: ACTOR1 speaks
        };
        var resolved = new Dictionary<string, uint>
        {
            ["S0_K1"] = 200,
            ["S1_K1"] = 100,
            ["S1_K2"] = 200,
        };

        var mapping = BuildMapping(actors, calls, resolved);
        var candidates = DialogHarvestService.ComputeSilentActorCandidates("K_UNRESOLVED", mapping);

        Assert.Single(candidates);
        Assert.Equal(200u, candidates[0]);
    }

    [Fact]
    public void EmptyMapping_ReturnsEmpty()
    {
        var mapping = BuildMapping(new(), new(), new());
        var candidates = DialogHarvestService.ComputeSilentActorCandidates("K", mapping);
        Assert.Empty(candidates);
    }
}
