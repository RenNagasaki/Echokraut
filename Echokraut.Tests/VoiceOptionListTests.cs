using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class VoiceOptionListTests
{
    private static EchokrautVoice Voice(string fileName)
        => new() { BackendVoice = fileName, VoiceName = System.IO.Path.GetFileNameWithoutExtension(fileName) };

    [Fact]
    public void BuildLabels_UsesDisplayName_WhenUnique()
    {
        var voices = new List<EchokrautVoice> { Voice("Male_All_NPC001.wav"), Voice("Male_All_NPC002.wav") };

        var labels = VoiceOptionList.BuildLabels(voices);

        Assert.Equal(new[] { "NPC001", "NPC002" }, labels);
    }

    [Fact]
    public void BuildLabels_DisambiguatesDuplicateDisplayNames()
    {
        // Both derive the display name "NPC001" — the body-type token is stripped.
        var voices = new List<EchokrautVoice> { Voice("Male_All_NPC001.wav"), Voice("Male_All-Elder_NPC001.wav") };

        var labels = VoiceOptionList.BuildLabels(voices);

        Assert.Equal(2, labels.Count);
        Assert.NotEqual(labels[0], labels[1]);
        Assert.Contains("Male_All_NPC001", labels[0]);
        Assert.Contains("Male_All-Elder_NPC001", labels[1]);
    }

    [Fact]
    public void BuildLabels_EmptyDisplayName_FallsBackToFileName()
    {
        // Every segment is a grammar token, so VoiceName resolves to "".
        var voices = new List<EchokrautVoice> { Voice("Male_All.wav") };

        var labels = VoiceOptionList.BuildLabels(voices);

        Assert.Equal(new[] { "Male_All" }, labels);
    }

    [Fact]
    public void BuildLabels_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(VoiceOptionList.BuildLabels(null));
        Assert.Empty(VoiceOptionList.BuildLabels(new List<EchokrautVoice>()));
    }

    [Fact]
    public void Resolve_PicksTheEntryTheLabelBelongsTo()
    {
        var first  = Voice("Male_All_NPC001.wav");
        var second = Voice("Male_All-Elder_NPC001.wav");
        var voices = new List<EchokrautVoice> { first, second };
        var labels = VoiceOptionList.BuildLabels(voices);

        // The bug this guards: a name lookup returned `first` for BOTH labels, so picking the
        // second voice silently applied the first one — the selection looked ignored.
        Assert.Same(first, VoiceOptionList.Resolve(voices, labels[0]));
        Assert.Same(second, VoiceOptionList.Resolve(voices, labels[1]));
    }

    [Fact]
    public void Resolve_UnknownLabel_ReturnsNull()
    {
        var voices = new List<EchokrautVoice> { Voice("Male_All_NPC001.wav") };

        Assert.Null(VoiceOptionList.Resolve(voices, "NPC999"));
        Assert.Null(VoiceOptionList.Resolve(voices, null));
        Assert.Null(VoiceOptionList.Resolve(null, "NPC001"));
    }

    [Fact]
    public void LabelFor_RoundTripsThroughResolve()
    {
        var target = Voice("Male_All-Elder_NPC001.wav");
        var voices = new List<EchokrautVoice> { Voice("Male_All_NPC001.wav"), target };

        var label = VoiceOptionList.LabelFor(voices, target);

        Assert.NotEmpty(label);
        Assert.Same(target, VoiceOptionList.Resolve(voices, label));
    }

    [Fact]
    public void LabelFor_VoiceNotInList_ReturnsEmpty()
    {
        var voices = new List<EchokrautVoice> { Voice("Male_All_NPC001.wav") };

        Assert.Equal(string.Empty, VoiceOptionList.LabelFor(voices, Voice("Female_All_NPC050.wav")));
        Assert.Equal(string.Empty, VoiceOptionList.LabelFor(voices, null));
    }
}
