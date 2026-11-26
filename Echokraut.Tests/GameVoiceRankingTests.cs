using System;
using System.Linq;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using EchoScd;
using Xunit;

namespace Echokraut.Tests;

public class GameVoiceRankingTests
{
    [Theory]
    [InlineData(2.9, false)]
    [InlineData(3.0, true)]
    [InlineData(15.0, true)]
    [InlineData(15.1, false)]
    public void Duration_HardLimits(double seconds, bool ok) =>
        Assert.Equal(ok, GameVoiceRanking.IsAcceptableDuration(seconds));

    [Theory]
    [InlineData(6.0, 1.0)]
    [InlineData(8.0, 1.0)]
    [InlineData(10.0, 1.0)]
    [InlineData(4.5, 0.5)]
    [InlineData(12.5, 0.5)]
    [InlineData(20.0, 0.0)]
    public void DurationScore_PeaksInOptimum(double seconds, double expected) =>
        Assert.Equal(expected, GameVoiceRanking.DurationScore(seconds), 6);

    [Fact]
    public void TempoScore_IsBestAtLanguageMedian()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 26));
        var atMedian = GameVoiceRanking.TempoScore(words, 10.0, "EN");      // 2.6 w/s
        var rushed = GameVoiceRanking.TempoScore(words, 5.0, "EN");         // 5.2 w/s
        var dragged = GameVoiceRanking.TempoScore(words, 20.0, "EN");       // 1.3 w/s
        Assert.Equal(1.0, atMedian, 6);
        Assert.True(rushed < 0.3 && dragged < 0.3);
    }

    [Fact]
    public void TempoScore_JapaneseCountsCharacters()
    {
        var text = new string('あ', 75);
        Assert.Equal(1.0, GameVoiceRanking.TempoScore(text, 10.0, "JA"), 6);
    }

    [Fact]
    public void SignalScore_ClippingCosts()
    {
        var clean = Sine(0.3);
        var clipped = Sine(1.5); // hard-clipped at full scale
        var cleanScore = Score(clean);
        var clippedScore = Score(clipped);
        Assert.True(clippedScore < cleanScore, $"{clippedScore} !< {cleanScore}");
    }

    [Fact]
    public void FinalScore_WithoutAsr_IsPreScore()
    {
        Assert.Equal(0.7, GameVoiceRanking.FinalScore(0.7, null), 6);
    }

    [Fact]
    public void FinalScore_HighWerLowersScore()
    {
        Assert.True(GameVoiceRanking.FinalScore(0.7, 0.9) < GameVoiceRanking.FinalScore(0.7, 0.0));
        Assert.Equal(GameVoiceRanking.FinalScore(0.7, 1.0), GameVoiceRanking.FinalScore(0.7, 3.0), 6);
    }

    private static double Score(PcmBuffer pcm)
    {
        var m = SignalAnalyzer.Analyze(pcm);
        return GameVoiceRanking.SignalScore(m, SignalAnalyzer.Check(m));
    }

    internal static PcmBuffer Sine(double amplitude, double seconds = 8.0, double hz = 220)
    {
        var rate = InlineVoiceReference.SampleRate;
        var n = (int)(rate * seconds);
        var s = new short[n];
        for (var i = 0; i < n; i++)
        {
            var v = amplitude * Math.Sin(2 * Math.PI * hz * i / rate);
            s[i] = (short)Math.Clamp(v * short.MaxValue, short.MinValue, short.MaxValue);
        }
        return new PcmBuffer(s, rate, 1);
    }
}
