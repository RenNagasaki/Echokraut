using Echotools.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The part of <see cref="NodeState"/> that can be checked without the game: which flags a plain
/// node loses when it is disabled. Everything else needs a live ATK node.
/// </summary>
public class NodeStateTests
{
    [Fact]
    public void InteractiveFlagsOf_KeepsOnlyTheMouseFlags()
    {
        // Visible/Enabled are not about input and must survive being disabled.
        const NodeFlags current = NodeFlags.Visible | NodeFlags.Enabled
                                | NodeFlags.RespondToMouse | NodeFlags.HasCollision;

        Assert.Equal(NodeFlags.RespondToMouse | NodeFlags.HasCollision,
            NodeState.InteractiveFlagsOf(current));
    }

    [Fact]
    public void InteractiveFlagsOf_NodeWithoutMouseFlags_ReturnsNothing()
    {
        // Nothing to take away, so enabling must not hand out flags the node never had.
        Assert.Equal((NodeFlags)0, NodeState.InteractiveFlagsOf(NodeFlags.Visible));
    }

    [Fact]
    public void InteractiveFlagsOf_NeverReturnsAFlagTheNodeDoesNotCarry()
    {
        var result = NodeState.InteractiveFlagsOf(NodeFlags.EmitsEvents);
        Assert.Equal(NodeFlags.EmitsEvents, result);
        Assert.False(result.HasFlag(NodeFlags.HasCollision));
        Assert.False(result.HasFlag(NodeFlags.RespondToMouse));
    }
}
