using System;
using Echokraut.DataClasses;
using Xunit;

namespace Echokraut.Tests;

public class VoiceLineSkipGuardTests
{
    [Fact]
    public void Consume_WithoutNotify_ReturnsFalse()
    {
        var g = new VoiceLineSkipGuard(() => DateTime.UnixEpoch);
        Assert.False(g.ConsumeIsVoice(1000));
    }

    [Fact]
    public void Notify_ThenConsume_WithinFreshness_ReturnsTrue()
    {
        var t = DateTime.UnixEpoch;
        var g = new VoiceLineSkipGuard(() => t);
        g.Notify();
        Assert.True(g.ConsumeIsVoice(1000));
    }

    [Fact]
    public void Consume_ClearsFlag_SecondConsumeIsFalse()
    {
        var t = DateTime.UnixEpoch;
        var g = new VoiceLineSkipGuard(() => t);
        g.Notify();
        Assert.True(g.ConsumeIsVoice(1000));
        Assert.False(g.ConsumeIsVoice(1000)); // flag consumed by the first read
    }

    [Fact]
    public void Notify_StaleBeyondFreshness_ReturnsFalse()
    {
        var t = DateTime.UnixEpoch;
        var g = new VoiceLineSkipGuard(() => t);
        g.Notify();                  // stamped at t
        t = t.AddMilliseconds(1500); // 1500ms elapse
        Assert.False(g.ConsumeIsVoice(1000)); // older than the 1000ms window → stale
    }
}
