using Echokraut.Services.SelfTest;
using Xunit;

namespace Echokraut.Tests.SelfTest;

public class SelfTestRunGateTests
{
    [Fact]
    public void OneOwnerAtATime()
    {
        var gate = new SelfTestRunGate();

        Assert.True(gate.TryEnter("self-test"));
        Assert.False(gate.TryEnter("e2e r1"));
        Assert.Equal("self-test", gate.Owner);

        gate.Exit("e2e r1");                 // not the owner: no effect
        Assert.Equal("self-test", gate.Owner);

        gate.Exit("self-test");
        Assert.Null(gate.Owner);
        Assert.True(gate.TryEnter("e2e r1"));
    }
}
