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
    public void MayRebufferAgain_HoldsBackAStormOfRebuffers()
    {
        // The log storm this rule bounds: the watchdog reacted to its own resume and paused/logged
        // thousands of times within a single millisecond.
        Assert.False(StreamBufferPolicy.MayRebufferAgain(0));
        Assert.False(StreamBufferPolicy.MayRebufferAgain(StreamBufferPolicy.MinRebufferIntervalMs - 1));
    }

    [Fact]
    public void MayRebufferAgain_AllowsTheFirstOneAndTheNextAfterTheInterval()
    {
        Assert.True(StreamBufferPolicy.MayRebufferAgain(-1));   // none yet
        Assert.True(StreamBufferPolicy.MayRebufferAgain(StreamBufferPolicy.MinRebufferIntervalMs));
        Assert.True(StreamBufferPolicy.MayRebufferAgain(10_000));
    }

    [Fact]
    public void MinRebufferIntervalMs_StaysBelowTheInitialCushion()
    {
        // A rebuffer refills to at least InitialCushionMs, so a genuine one always takes longer
        // than the interval — the guard can only ever suppress spurious repeats, never real ones.
        Assert.True(StreamBufferPolicy.MinRebufferIntervalMs < StreamBufferPolicy.InitialCushionMs);
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

    // ── Sub-real-time sources: wait for the whole clip instead of streaming it ──────────────

    [Fact]
    public void DeliveryRate_CountsSecondsOfAudioPerSecondOfWaiting()
    {
        var bytesPerSecond = SampleRate * FrameBytes;

        // Half a second of audio arrived in one second of waiting → 0.5x real-time.
        Assert.Equal(0.5, StreamBufferPolicy.DeliveryRate(bytesPerSecond / 2, 1000, bytesPerSecond), 3);
        // Two seconds of audio in one → 2x.
        Assert.Equal(2.0, StreamBufferPolicy.DeliveryRate(bytesPerSecond * 2, 1000, bytesPerSecond), 3);
    }

    [Fact]
    public void DeliveryRate_DegenerateInputs_AreZero()
    {
        Assert.Equal(0, StreamBufferPolicy.DeliveryRate(1000, 0, 48000));
        Assert.Equal(0, StreamBufferPolicy.DeliveryRate(1000, 1000, 0));
        Assert.Equal(0, StreamBufferPolicy.DeliveryRate(0, 1000, 48000));
        Assert.Equal(0, StreamBufferPolicy.DeliveryRate(-5, 1000, 48000));
    }

    [Fact]
    public void ShouldWaitForFullClip_SlowSource_AfterTheMeasureWindow()
    {
        var bytesPerSecond = SampleRate * FrameBytes;
        var elapsed = StreamBufferPolicy.RateMeasureWindowMs;
        // 0.6x real-time: the deficit grows for the whole clip, so no cushion can save it.
        var delivered = (long)(bytesPerSecond * 0.6 * elapsed / 1000);

        Assert.True(StreamBufferPolicy.ShouldWaitForFullClip(elapsed, delivered, bytesPerSecond, readerDone: false));
    }

    [Fact]
    public void ShouldWaitForFullClip_NotBeforeTheMeasureWindow()
    {
        var bytesPerSecond = SampleRate * FrameBytes;
        // Same slow rate, judged too early: the first block's latency dominates and says nothing.
        var elapsed = StreamBufferPolicy.RateMeasureWindowMs - 1;
        var delivered = (long)(bytesPerSecond * 0.6 * elapsed / 1000);

        Assert.False(StreamBufferPolicy.ShouldWaitForFullClip(elapsed, delivered, bytesPerSecond, readerDone: false));
    }

    [Fact]
    public void ShouldWaitForFullClip_FastSource_KeepsStreaming()
    {
        var bytesPerSecond = SampleRate * FrameBytes;
        var elapsed = StreamBufferPolicy.RateMeasureWindowMs * 2;
        var delivered = (long)(bytesPerSecond * 2.4 * elapsed / 1000); // the measured AllTalk/fast case

        Assert.False(StreamBufferPolicy.ShouldWaitForFullClip(elapsed, delivered, bytesPerSecond, readerDone: false));
    }

    [Fact]
    public void ShouldWaitForFullClip_ClipAlreadyComplete_NeverWaits()
    {
        // readerDone means there is nothing left to wait FOR — the short-line low-latency path.
        Assert.False(StreamBufferPolicy.ShouldWaitForFullClip(
            StreamBufferPolicy.RateMeasureWindowMs * 4, deliveredBytes: 1, bytesPerSecond: SampleRate * FrameBytes,
            readerDone: true));
    }

    [Fact]
    public void ShouldWaitForFullClip_ExactlyRealTime_StillCountsAsTooSlow()
    {
        // At 1.0x playback catches up with the source and starves at every jitter, so the threshold
        // deliberately sits above real time.
        var bytesPerSecond = SampleRate * FrameBytes;
        var elapsed = StreamBufferPolicy.RateMeasureWindowMs;
        var delivered = (long)bytesPerSecond * elapsed / 1000;

        Assert.True(StreamBufferPolicy.MinStreamableRate > 1.0);
        Assert.True(StreamBufferPolicy.ShouldWaitForFullClip(elapsed, delivered, bytesPerSecond, readerDone: false));
    }

    [Fact]
    public void FullClipWaitTimeout_IsLongerThanThePrebufferTimeout()
    {
        // It replaces a garbled line, not a fast start — but it stays finite so a stalled source
        // still produces something.
        Assert.True(StreamBufferPolicy.FullClipWaitTimeoutMs > StreamBufferPolicy.PrebufferTimeoutMs);
    }
}
