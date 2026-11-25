using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Speaker tracking in <see cref="DialogStateService"/>. It is written from the async speech
/// pipeline and cleared from the addon lifecycle (framework thread), so the point of these is
/// the accessor contract and that concurrent use doesn't throw.
/// </summary>
/// <remarks>
/// Each test builds its own instance — since the state moved out of a static class and behind
/// DI, the tests no longer share process-wide state and the "clear first" dance is gone.
/// </remarks>
public class DialogStateTests
{
    private static DialogStateService NewState() => new();

    [Fact]
    public void MarkSpeakerResolved_MakesTheSpeakerKnown()
    {
        var state = NewState();

        Assert.False(state.WasSpeakerResolved(42));
        state.MarkSpeakerResolved(42);
        Assert.True(state.WasSpeakerResolved(42));
    }

    [Fact]
    public void MarkSpeakerResolved_IsIdempotent()
    {
        var state = NewState();

        state.MarkSpeakerResolved(7);
        state.MarkSpeakerResolved(7);

        Assert.True(state.WasSpeakerResolved(7));
    }

    [Fact]
    public void ClearResolvedSpeakers_ForgetsEveryone()
    {
        var state = NewState();
        state.MarkSpeakerResolved(1);
        state.MarkSpeakerResolved(2);

        state.ClearResolvedSpeakers();

        Assert.False(state.WasSpeakerResolved(1));
        Assert.False(state.WasSpeakerResolved(2));
    }

    [Fact]
    public async Task ConcurrentMarkAndClear_DoesNotThrow()
    {
        // The dialogue pipeline marks speakers from a thread-pool thread while the addon
        // lifecycle can clear on the framework thread. With the previous plain HashSet this
        // overlap could throw or corrupt the set.
        var state = NewState();

        var work = new List<Task>();
        for (var i = 0; i < 8; i++)
        {
            var start = i * 500;
            work.Add(Task.Run(() =>
            {
                for (var id = start; id < start + 500; id++)
                {
                    state.MarkSpeakerResolved(id);
                    state.WasSpeakerResolved(id);
                }
            }));
        }
        work.Add(Task.Run(() =>
        {
            for (var i = 0; i < 50; i++) state.ClearResolvedSpeakers();
        }));

        // The assertion is that this completes at all — no exception from any worker.
        await Task.WhenAll(work);
        Assert.True(work.All(t => t.IsCompletedSuccessfully));
    }
}
