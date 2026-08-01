using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class StreamBufferPolicyTests
{
    // 24 kHz mono 16-bit — the EchokrauTTS wrapper's raw PCM format.
    private const int SampleRate = 24000;
    private const int FrameBytes = 2;

    [Fact]
    public void BytesForMs_MatchesPcmFrameMath()
    {
        // 1000 ms of 24 kHz mono s16le = 48000 bytes
        Assert.Equal(48000, StreamBufferPolicy.BytesForMs(1000, SampleRate, FrameBytes));
        Assert.Equal(24000, StreamBufferPolicy.BytesForMs(500, SampleRate, FrameBytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BytesForMs_NonPositiveDuration_IsZero(int ms)
    {
        Assert.Equal(0, StreamBufferPolicy.BytesForMs(ms, SampleRate, FrameBytes));
    }

    [Fact]
    public void BytesForMs_RoundsUpToAtLeastOneFrame()
    {
        Assert.Equal(FrameBytes, StreamBufferPolicy.BytesForMs(1, 100, FrameBytes));
    }

    [Fact]
    public void NextCushionMs_DoublesAndCaps()
    {
        Assert.Equal(2000, StreamBufferPolicy.NextCushionMs(1000));
        Assert.Equal(4000, StreamBufferPolicy.NextCushionMs(2000));
        Assert.Equal(StreamBufferPolicy.MaxCushionMs, StreamBufferPolicy.NextCushionMs(4000));
        Assert.Equal(StreamBufferPolicy.MaxCushionMs, StreamBufferPolicy.NextCushionMs(StreamBufferPolicy.MaxCushionMs));
    }

    [Fact]
    public void NextCushionMs_FromZero_StartsAtInitial()
    {
        Assert.Equal(StreamBufferPolicy.InitialCushionMs, StreamBufferPolicy.NextCushionMs(0));
    }

    [Fact]
    public void ShouldStartPlayback_WaitsWhileCushionIsShort()
    {
        Assert.False(StreamBufferPolicy.ShouldStartPlayback(
            availableBytes: 10_000, targetBytes: 48_000, readerDone: false, elapsedMs: 100, timeoutMs: 6000));
    }

    [Fact]
    public void ShouldStartPlayback_WhenCushionReached()
    {
        Assert.True(StreamBufferPolicy.ShouldStartPlayback(
            availableBytes: 48_000, targetBytes: 48_000, readerDone: false, elapsedMs: 100, timeoutMs: 6000));
    }

    [Fact]
    public void ShouldStartPlayback_ShortClipAlreadyFullyRead_DoesNotWaitForCushion()
    {
        // A one-second line never reaches a multi-second cushion — it must play anyway.
        Assert.True(StreamBufferPolicy.ShouldStartPlayback(
            availableBytes: 5_000, targetBytes: 48_000, readerDone: true, elapsedMs: 10, timeoutMs: 6000));
    }

    [Fact]
    public void ShouldStartPlayback_StalledSource_GivesUpAtTimeout()
    {
        Assert.True(StreamBufferPolicy.ShouldStartPlayback(
            availableBytes: 0, targetBytes: 48_000, readerDone: false, elapsedMs: 6000, timeoutMs: 6000));
    }

    [Fact]
    public void ShouldRebuffer_WhenRunningDryWhileSourceStillDelivering()
    {
        var lowWater = StreamBufferPolicy.BytesForMs(StreamBufferPolicy.LowWaterMs, SampleRate, FrameBytes);

        Assert.True(StreamBufferPolicy.ShouldRebuffer(lowWater - 1, lowWater, readerDone: false));
        Assert.False(StreamBufferPolicy.ShouldRebuffer(lowWater, lowWater, readerDone: false));
    }

    [Fact]
    public void ShouldRebuffer_AtEndOfClip_IsNotAnUnderrun()
    {
        // Reader done + draining buffer = the clip is simply finishing. Pausing here would
        // stall the tail of every line forever.
        Assert.False(StreamBufferPolicy.ShouldRebuffer(0, 9_600, readerDone: true));
    }

    [Fact]
    public void BufferedAhead_IsPushedMinusPlayed()
    {
        Assert.Equal(48_000, StreamBufferPolicy.BufferedAhead(pushedBytes: 96_000, playedBytes: 48_000));
        Assert.Equal(96_000, StreamBufferPolicy.BufferedAhead(pushedBytes: 96_000, playedBytes: 0));
    }

    [Fact]
    public void BufferedAhead_NeverGoesNegative()
    {
        // The pushed counter and the play position are sampled independently, so the position can
        // briefly read past the total. A negative fill would look like a permanent underrun and
        // make the rebuffer watchdog pause a perfectly healthy stream.
        Assert.Equal(0, StreamBufferPolicy.BufferedAhead(pushedBytes: 1_000, playedBytes: 1_200));
    }

    [Fact]
    public void BufferedAhead_FillsTheCushionWhileNothingHasPlayedYet()
    {
        // The regression this measurement replaced: before the first ChannelPlay nothing has been
        // played, so everything pushed counts towards the cushion. Asking BASS instead returned a
        // flat value there, the cushion was never reached, and playback waited for the whole clip.
        var target = StreamBufferPolicy.BytesForMs(StreamBufferPolicy.InitialCushionMs, SampleRate, FrameBytes);
        var buffered = StreamBufferPolicy.BufferedAhead(pushedBytes: target, playedBytes: 0);

        Assert.True(StreamBufferPolicy.ShouldStartPlayback(
            buffered, target, readerDone: false, elapsedMs: 50, timeoutMs: StreamBufferPolicy.PrebufferTimeoutMs));
    }

    [Fact]
    public void ChannelBufferMustBeAbleToHoldTheLargestCushion()
    {
        // Live3DAudioEngine sizes the BASS channel buffer as MaxCushionMs + 500. If the cushion
        // could exceed the channel buffer, the rebuffer wait would never be satisfiable.
        Assert.True(StreamBufferPolicy.MaxCushionMs + 500 > StreamBufferPolicy.MaxCushionMs);
        Assert.True(StreamBufferPolicy.InitialCushionMs < StreamBufferPolicy.MaxCushionMs);
        Assert.True(StreamBufferPolicy.LowWaterMs < StreamBufferPolicy.InitialCushionMs);
    }
}
