using System;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Buffer sizing rules for progressive (streamed) TTS audio — the decisions
/// <c>Live3DAudioEngine.Source</c> makes while feeding its BASS push stream, factored out as
/// pure functions so they can be reasoned about and tested without an audio device.
///
/// <para><b>The problem.</b> EchokrauTTS streams raw PCM as the model produces it. On a GPU that
/// synthesises slower than real time, the stream delivers less than a second of audio per second
/// of playback. A fixed prebuffer cushion only postpones that: playback eats the cushion and then
/// starves, and every starve/refill boundary is an audible glitch — the line stutters into
/// unintelligibility. A one-off "wait longer at the start" cannot fix it, because the deficit
/// grows for as long as the clip lasts.</para>
///
/// <para><b>The rule.</b> Start with a modest cushion so fast sources stay low-latency, and treat
/// a starve as evidence that the cushion is too small for this source: pause, refill to a doubled
/// cushion, resume. After one or two rebuffers the cushion exceeds the source's deficit and the
/// rest of the clip plays through in one piece. That converts many tiny glitches into at most a
/// couple of clean pauses.</para>
/// </summary>
public static class StreamBufferPolicy
{
    /// <summary>Cushion the first playback start waits for.</summary>
    public const int InitialCushionMs = 1000;

    /// <summary>Upper bound for the grown cushion — past this, waiting costs more than it buys.</summary>
    public const int MaxCushionMs = 6000;

    /// <summary>Buffered audio below this is treated as "about to starve".</summary>
    public const int LowWaterMs = 200;

    /// <summary>Safety cap for the initial prebuffer wait (a source that never delivers).</summary>
    public const int PrebufferTimeoutMs = 6000;

    /// <summary>
    /// Shortest time allowed between two rebuffers of the same stream. A rebuffer is a pause, a
    /// refill and a resume — it can never sensibly happen more than a couple of times a second, so
    /// anything faster is the watchdog reacting to its own resume rather than to the source. The
    /// interval bounds that structurally: whatever the fill level says, at most one rebuffer (and
    /// one log line) per this window.
    /// </summary>
    public const int MinRebufferIntervalMs = 500;

    /// <summary>Bytes that hold <paramref name="ms"/> of audio at the given rate and frame size.</summary>
    public static int BytesForMs(int ms, int sampleRate, int frameBytes)
    {
        if (ms <= 0 || sampleRate <= 0 || frameBytes <= 0) return 0;
        var bytes = (long)sampleRate * frameBytes * ms / 1000;
        return (int)Math.Clamp(bytes, frameBytes, int.MaxValue);
    }

    /// <summary>
    /// Audio buffered ahead of the playhead: everything handed to the output minus everything it
    /// has already played. Never negative — the two counters are sampled independently, so the play
    /// position can briefly read past the pushed total.
    ///
    /// <para><b>Why this is counted instead of asked.</b> The obvious source for "how much is
    /// buffered" is the audio library itself (BASS: <c>BASS_DATA_AVAILABLE</c>), but that reports
    /// the PLAYBACK buffer, which a push stream only fills while it is actually playing. Before the
    /// first start it stays at a small constant however much is pushed into it, so a cushion
    /// measured that way is never reached and playback falls back to its only other release
    /// condition — "the whole clip has arrived". That is precisely how progressive streaming turns
    /// back into "wait for the full audio" (measured 2026-08-01: bytes arriving at 2.42x real-time
    /// for 3.4s, playback starting 4ms after the backend reported the clip finished).</para>
    /// </summary>
    public static int BufferedAhead(long pushedBytes, long playedBytes)
        => (int)Math.Clamp(pushedBytes - playedBytes, 0, int.MaxValue);

    /// <summary>Doubled cushion, capped at <see cref="MaxCushionMs"/>.</summary>
    public static int NextCushionMs(int currentMs)
    {
        if (currentMs <= 0) return InitialCushionMs;
        return Math.Min(currentMs * 2, MaxCushionMs);
    }

    /// <summary>
    /// True when playback may (re)start: the cushion is filled, or the whole clip has already
    /// arrived — a short line is fully buffered long before it reaches the cushion and must not
    /// be made to wait for audio that will never come. The elapsed-time cap keeps a stalled
    /// source from blocking forever.
    /// </summary>
    public static bool ShouldStartPlayback(long availableBytes, long targetBytes, bool readerDone,
                                           long elapsedMs, int timeoutMs)
    {
        if (readerDone) return true;
        if (availableBytes >= targetBytes) return true;
        return elapsedMs >= timeoutMs;
    }

    /// <summary>
    /// True when playback should pause to refill. Only meaningful while the source is still
    /// delivering — once the reader is done, a draining buffer is simply the end of the clip.
    /// </summary>
    public static bool ShouldRebuffer(long availableBytes, long lowWaterBytes, bool readerDone)
        => !readerDone && availableBytes < lowWaterBytes;

    /// <summary>
    /// True when enough time has passed since the last rebuffer for another one to be plausible.
    /// The first rebuffer of a stream (<paramref name="msSinceLastRebuffer"/> below zero, i.e. none
    /// yet) is always allowed. See <see cref="MinRebufferIntervalMs"/>.
    /// </summary>
    public static bool MayRebufferAgain(long msSinceLastRebuffer)
        => msSinceLastRebuffer < 0 || msSinceLastRebuffer >= MinRebufferIntervalMs;
}
