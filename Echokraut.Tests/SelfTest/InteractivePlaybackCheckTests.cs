using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest;

public class InteractivePlaybackCheckTests
{
    private readonly Mock<IVoiceMessageQueue> _queue = new();
    private string? _skip;

    public InteractivePlaybackCheckTests() => PlaybackWatch.PollInterval = TimeSpan.FromMilliseconds(1);

    private InteractivePlaybackCheck Create() =>
        new(_queue.Object, "talk", TextSource.AddonTalk, "Dialogue (interactive)", "Talk to any NPC and let one line play.", () => _skip);

    private static VoiceMessageEntry Entry(TextSource source, string text = "Hello there.") =>
        new(new VoiceMessage { Source = source, Text = text, Speaker = new NpcMapData(ObjectKind.EventNpc) { Name = "Tataru" } });

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
        var old = Entry(TextSource.AddonTalk); // queued before the check started
        await Task.Delay(5);
        _queue.SetupSequence(q => q.GetCurrentlyPlaying())
            .Returns(Entry(TextSource.AddonBubble))
            .Returns(old)
            .Returns((VoiceMessageEntry?)null);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().RunAsync(cts.Token));
    }
}
