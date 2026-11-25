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
        Assert.True(r.Seconds <= InlineReferenceAssembler.DefaultMaxSeconds);
    }

    [Fact]
    public void LongerWrapperLimit_TakesTheSecondClip()
    {
        // Estinien, 2026-11-15: 7.9 s + 0.3 s pause + 6.94 s = 15.14 s — one clip under the old 15 s,
        // both under Qwen's 20 s.
        var clips = new[] { Clip("a", 7.9), Clip("b", 6.94) };

        Assert.Equal(new[] { "a" }, InlineReferenceAssembler.Assemble(clips)!.ClipIds);
        Assert.Equal(new[] { "a", "b" }, InlineReferenceAssembler.Assemble(clips, 20.0)!.ClipIds);
    }

    [Fact]
    public void BestClipIsAlwaysIncluded()
    {
        var r = InlineReferenceAssembler.Assemble(new[] { Clip("a", 15) })!;
        Assert.Equal(new[] { "a" }, r.ClipIds);
    }
}
