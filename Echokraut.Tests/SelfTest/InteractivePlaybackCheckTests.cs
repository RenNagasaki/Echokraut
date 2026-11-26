using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest;

public class InteractivePlaybackCheckTests
{
    private readonly Mock<IVoiceMessageQueue> _queue = new();
    private string? _skip;

    public InteractivePlaybackCheckTests() => PlaybackWatch.PollInterval = TimeSpan.FromMilliseconds(1);

    private InteractivePlaybackCheck Create(TimeProvider? time = null) =>
        new(_queue.Object, "talk", TextSource.AddonTalk, "Dialogue (interactive)", "Talk to any NPC and let one line play.", () => _skip, time);

    private static VoiceMessageEntry Entry(TextSource source, TimeProvider? time = null, string text = "Hello there.") =>
        new(new VoiceMessage { Source = source, Text = text, Speaker = new NpcMapData(ObjectKind.EventNpc) { Name = "Tataru" } }, time);

    [Fact]
    public void IsInteractive_WithAnInstruction()
    {
        var check = Create();
        Assert.True(check.Interactive);
        Assert.False(string.IsNullOrEmpty(check.Instruction));
    }

    [Fact]
    public async Task SkipReason_SkipsWithoutWaiting()
    {
        _skip = "Dialogue voicing is off (/ekttalk).";
        var result = await Create().RunAsync(CancellationToken.None);
        Assert.Equal(SelfTestResult.Skip(_skip), result);
    }

    [Fact]
    public async Task LineOfTheSource_Passes_WithSpeakerAndText()
    {
        var check = Create();
        var run = check.RunAsync(CancellationToken.None);
        _queue.Setup(q => q.GetCurrentlyPlaying()).Returns(Entry(TextSource.AddonTalk));

        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        Assert.Equal("Tataru: \"Hello there.\"", result.Detail);
    }

    [Fact]
    public async Task OtherSourceOrOlderLine_DoesNotCount()
    {
        var time = new FakeTimeProvider();
        var old = Entry(TextSource.AddonTalk, time); // queued before the check started
        time.Advance(TimeSpan.FromSeconds(1));
        var playing = new Queue<VoiceMessageEntry>(new[] { Entry(TextSource.AddonBubble, time), old });
        using var cts = new CancellationTokenSource();
        // Both lines are shown to the check, then the run is ended: had either counted, it would have passed.
        _queue.Setup(q => q.GetCurrentlyPlaying()).Returns(() =>
        {
            if (playing.Count > 0) return playing.Dequeue();
            cts.Cancel();
            return null;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(time).RunAsync(cts.Token));
        Assert.Empty(playing);
    }
}
