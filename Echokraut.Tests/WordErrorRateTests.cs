using Echokraut.Helper.Functional.GameVoice;
using Xunit;

namespace Echokraut.Tests;

public class WordErrorRateTests
{
    [Fact]
    public void Identical_IgnoringCasePunctuationAndDiacritics_IsZero()
    {
        Assert.Equal(0, WordErrorRate.Compute("Où est-il, Y'shtola?", "ou est il yshtola", "FR"));
    }

    [Fact]
    public void OneSubstitutionInFourWords_IsQuarter()
    {
        Assert.Equal(0.25, WordErrorRate.Compute("we must go now", "we must run now", "EN"), 6);
    }

    [Fact]
    public void Japanese_IsComparedPerCharacter()
    {
        Assert.Equal(0.25, WordErrorRate.Compute("行きます", "行きまし", "JA"), 6);
    }

    [Fact]
    public void EmptyReference()
    {
        Assert.Equal(0, WordErrorRate.Compute("", "", "EN"));
        Assert.Equal(1, WordErrorRate.Compute("", "noise", "EN"));
    }

    [Fact]
    public void MuchLongerHypothesis_CanExceedOne()
    {
        Assert.True(WordErrorRate.Compute("yes", "no no no", "EN") > 1);
    }
}
