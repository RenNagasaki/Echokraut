using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Which voice is the narrator: the shipped <c>Narrator.wav</c> by default, whatever the user picked
/// when they picked one, and back to the shipped one whenever that pick no longer works.
/// </summary>
public class NarratorVoicePolicyTests
{
    private static EchokrautVoice Voice(string backendVoice, bool isDefault = false, bool enabled = true)
        => new() { BackendVoice = backendVoice, voiceName = backendVoice, IsDefault = isDefault, IsEnabled = enabled };

    [Fact]
    public void Resolve_NoUserPick_UsesTheFlaggedDefault()
    {
        var flagged = Voice("Narrator.wav", isDefault: true);
        var other = Voice("A.wav");

        Assert.Same(flagged, NarratorVoicePolicy.Resolve(new List<EchokrautVoice> { other, flagged }, ""));
    }

    [Fact]
    public void Resolve_UserPick_OverrulesTheFlaggedDefault()
    {
        var flagged = Voice("Narrator.wav", isDefault: true);
        var chosen = Voice("A.wav");

        Assert.Same(chosen, NarratorVoicePolicy.Resolve(new List<EchokrautVoice> { flagged, chosen }, "A.wav"));
    }

    [Fact]
    public void Resolve_UserPick_IsCaseInsensitive()
    {
        var chosen = Voice("Female_Hyur_NPC001.wav");

        Assert.Same(chosen, NarratorVoicePolicy.Resolve(
            new List<EchokrautVoice> { chosen }, "female_hyur_npc001.WAV"));
    }

    [Fact]
    public void Resolve_UnknownUserPick_FallsBackToTheFlaggedDefault()
    {
        var flagged = Voice("Narrator.wav", isDefault: true);

        Assert.Same(flagged, NarratorVoicePolicy.Resolve(
            new List<EchokrautVoice> { flagged }, "replaced-by-a-new-voice-pack.wav"));
    }

    [Fact]
    public void Resolve_DisabledUserPick_FallsBackToTheFlaggedDefault()
    {
        var flagged = Voice("Narrator.wav", isDefault: true);
        var chosen = Voice("A.wav", enabled: false);

        Assert.Same(flagged, NarratorVoicePolicy.Resolve(
            new List<EchokrautVoice> { flagged, chosen }, "A.wav"));
    }

    [Fact]
    public void Resolve_NothingFlaggedAndNothingPicked_IsNull()
    {
        Assert.Null(NarratorVoicePolicy.Resolve(new List<EchokrautVoice> { Voice("A.wav") }, ""));
        Assert.Null(NarratorVoicePolicy.Resolve(new List<EchokrautVoice>(), "A.wav"));
        Assert.Null(NarratorVoicePolicy.Resolve(null, "A.wav"));
    }

    [Fact]
    public void IsFilterExempt_CoversBothThePickAndTheFlaggedDefault()
    {
        var flagged = Voice("Narrator.wav", isDefault: true);
        var chosen = Voice("A.wav");
        var ordinary = Voice("B.wav");

        // While a pick stands, BOTH stay exempt from the race/gender filter — the flagged default is
        // still the fallback if the pick goes away, so revoking its pass would re-roll every NPC
        // already sitting on it.
        Assert.True(NarratorVoicePolicy.IsFilterExempt(chosen, "A.wav"));
        Assert.True(NarratorVoicePolicy.IsFilterExempt(flagged, "A.wav"));
        Assert.False(NarratorVoicePolicy.IsFilterExempt(ordinary, "A.wav"));

        // Without a pick only the flagged default is exempt.
        Assert.True(NarratorVoicePolicy.IsFilterExempt(flagged, ""));
        Assert.False(NarratorVoicePolicy.IsFilterExempt(chosen, ""));
        Assert.False(NarratorVoicePolicy.IsFilterExempt(null, ""));
    }
}
