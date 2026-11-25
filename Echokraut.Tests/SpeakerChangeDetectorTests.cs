using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Voice-change detection (§11.3) on synthetic vectors — never real game data.</summary>
public sealed class SpeakerChangeDetectorTests
{
    private static readonly Random Rng = new(42);

    /// <summary>A voice = a direction; each clip = voice + noise.</summary>
    private static float[] Clip(int voice, double noise = 0.25)
    {
        var v = new float[16];
        v[voice] = 1f;
        v[15] = 3f; // shared component every encoder has (why centring matters)
        for (var i = 0; i < v.Length; i++) v[i] += (float)((Rng.NextDouble() - 0.5) * noise);
        return v;
    }

    private static List<SpeakerSample> Token(params (string Patch, int Voice)[] patches) =>
        patches.SelectMany(p => Enumerable.Range(0, 3).Select(_ => new SpeakerSample(p.Patch, Clip(p.Voice)))).ToList();

    private static float[] RunMean(params List<SpeakerSample>[] tokens) =>
        SpeakerChangeDetector.Mean(tokens.SelectMany(t => t).Select(s => s.Vector));

    [Fact]
    public void FindsTheBoundary()
    {
        var yshtola = Token(("05000", 1), ("05100", 1), ("06000", 1), ("06100", 2), ("06200", 2), ("06300", 2));
        var others = Token(("05000", 5), ("06000", 6), ("07000", 7));
        var found = SpeakerChangeDetector.Detect(yshtola, RunMean(yshtola, others), SpeakerChangeDetector.ThresholdFor("xtts-v2-speaker"));
        var s = Assert.Single(found);
        Assert.Equal("06100", s.BoundaryToken);
        Assert.Equal((3, 3), (s.PatchesBefore, s.PatchesAfter));
    }

    [Fact]
    public void SteadyVoice_NoBoundary()
    {
        var alphinaud = Token(("02200", 3), ("03000", 3), ("04000", 3), ("05000", 3), ("06000", 3), ("07000", 3));
        var others = Token(("05000", 5), ("06000", 6));
        Assert.Empty(SpeakerChangeDetector.Detect(alphinaud, RunMean(alphinaud, others), 0.32));
    }

    [Fact]
    public void OnlyTheLastPatchDiffers_IsNotABoundary()
    {
        // The measured false positive: the newest session alone sounds "different".
        var t = Token(("05000", 3), ("06000", 3), ("07000", 3), ("07500", 4));
        Assert.Empty(SpeakerChangeDetector.Detect(t, RunMean(t, Token(("05000", 5))), 0.32));
    }

    [Fact]
    public void TwoChanges_AreBothFound()
    {
        var t = Token(("03000", 1), ("03100", 1), ("04000", 2), ("04100", 2), ("05000", 3), ("05100", 3));
        var found = SpeakerChangeDetector.Detect(t, RunMean(t, Token(("05000", 6))), 0.32);
        Assert.Equal(new[] { "04000", "05000" }, found.Select(f => f.BoundaryToken));
    }

    [Fact]
    public void TooFewPatches_NoBoundary()
    {
        var t = Token(("03000", 1), ("05000", 2), ("05100", 2));
        Assert.Empty(SpeakerChangeDetector.Detect(t, RunMean(t), 0.0));
    }

    [Fact]
    public void Thresholds_AreStrictForUnknownEncoders()
    {
        Assert.True(SpeakerChangeDetector.IsCalibrated("xtts-v2-speaker"));
        Assert.False(SpeakerChangeDetector.IsCalibrated("qwen-1.7b-xvector"));
        Assert.True(SpeakerChangeDetector.ThresholdFor("qwen-1.7b-xvector") > SpeakerChangeDetector.ThresholdFor("xtts-v2-speaker"));
    }
}
