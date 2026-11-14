using System.Linq;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Xunit;

namespace Echokraut.Tests;

public class InlineReferenceAssemblerTests
{
    private static DecodedReferenceClip Clip(string id, double seconds, short fill = 1) =>
        new(id, "text " + id, Enumerable.Repeat(fill, (int)(seconds * InlineVoiceReference.SampleRate)).ToArray());

    [Fact]
    public void Empty_IsNull() => Assert.Null(InlineReferenceAssembler.Assemble(System.Array.Empty<DecodedReferenceClip>()));

    [Fact]
    public void JoinsInRankOrderWithPause()
    {
        var r = InlineReferenceAssembler.Assemble(new[] { Clip("a", 4, 1), Clip("b", 5, 2) })!;

        var gap = InlineReferenceAssembler.GapMilliseconds * InlineVoiceReference.SampleRate / 1000;
        Assert.Equal(9 * InlineVoiceReference.SampleRate + gap, r.Pcm.Length);
        Assert.Equal(new[] { "a", "b" }, r.ClipIds);
        Assert.Equal("text a text b", r.Text);
        Assert.Equal(0, r.Pcm[4 * InlineVoiceReference.SampleRate]);      // inside the pause
        Assert.Equal(2, r.Pcm[^1]);
    }

    [Fact]
    public void StaysWithinWrapperLimit_SkippingClipsThatDoNotFit()
    {
        var r = InlineReferenceAssembler.Assemble(new[] { Clip("a", 9), Clip("b", 8), Clip("c", 4) })!;

        Assert.Equal(new[] { "a", "c" }, r.ClipIds);
        Assert.True(r.Seconds <= InlineReferenceAssembler.MaxSeconds);
    }

    [Fact]
    public void BestClipIsAlwaysIncluded()
    {
        var r = InlineReferenceAssembler.Assemble(new[] { Clip("a", 15) })!;
        Assert.Equal(new[] { "a" }, r.ClipIds);
    }
}
