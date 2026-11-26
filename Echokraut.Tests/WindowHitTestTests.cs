using System.Numerics;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Hit-test for "did the click land in one of our own windows?". A miss here does not look like a
/// UI bug — it advances the NPC dialogue underneath, so clicking around in an Echokraut window
/// during a conversation skips lines.
/// </summary>
public class WindowHitTestTests
{
    [Fact]
    public void Contains_InsideAndOnTheEdges()
    {
        var origin = new Vector2(100, 200);
        var size = new Vector2(400, 300);

        Assert.True(WindowHitTest.Contains(new Vector2(300, 350), origin, size));
        Assert.True(WindowHitTest.Contains(origin, origin, size));                        // top-left corner
        Assert.True(WindowHitTest.Contains(new Vector2(500, 500), origin, size));         // bottom-right corner
    }

    [Fact]
    public void Contains_OutsideOnEverySide()
    {
        var origin = new Vector2(100, 200);
        var size = new Vector2(400, 300);

        Assert.False(WindowHitTest.Contains(new Vector2(99, 350), origin, size));
        Assert.False(WindowHitTest.Contains(new Vector2(501, 350), origin, size));
        Assert.False(WindowHitTest.Contains(new Vector2(300, 199), origin, size));
        Assert.False(WindowHitTest.Contains(new Vector2(300, 501), origin, size));
    }

    [Fact]
    public void Contains_NoExtent_ContainsNothing()
    {
        // A window that reports no size is not on screen; treating it as a point-sized target would
        // suppress a click that belongs to the game.
        Assert.False(WindowHitTest.Contains(Vector2.Zero, Vector2.Zero, Vector2.Zero));
        Assert.False(WindowHitTest.Contains(new Vector2(5, 5), Vector2.Zero, new Vector2(0, 100)));
        Assert.False(WindowHitTest.Contains(new Vector2(5, 5), Vector2.Zero, new Vector2(100, -1)));
    }

    [Fact]
    public void ScreenExtent_PrefersTheAddonsOwnMeasurement()
    {
        var extent = WindowHitTest.ScreenExtent(new Vector2(970, 650), scale: 1.25f,
                                                scaledWidth: 1212f, scaledHeight: 812f);

        Assert.Equal(new Vector2(1212f, 812f), extent);
    }

    [Fact]
    public void ScreenExtent_FallsBackToSizeTimesScale()
    {
        // The addon reports 0 between open and its first layout pass; the configured size scaled by
        // the HUD scale is the right stand-in. This is the case the bug was about: comparing against
        // the unscaled size made the rectangle too small at any HUD scale above 100%.
        var extent = WindowHitTest.ScreenExtent(new Vector2(400, 200), scale: 1.5f,
                                                scaledWidth: 0f, scaledHeight: 0f);

        Assert.Equal(new Vector2(600, 300), extent);
    }

    [Fact]
    public void ScreenExtent_NonPositiveScale_TreatedAsOneToOne()
    {
        var extent = WindowHitTest.ScreenExtent(new Vector2(400, 200), scale: 0f,
                                                scaledWidth: 0f, scaledHeight: 0f);

        Assert.Equal(new Vector2(400, 200), extent);
    }

    [Fact]
    public void ScaledWindow_ClickInTheLowerRight_IsInside()
    {
        // The regression, end to end: a 970x650 config window at 125% HUD scale covers 1212x812
        // screen pixels. A click at (1000, 700) is inside it but outside the unscaled rectangle.
        var origin = new Vector2(0, 0);
        var configured = new Vector2(970, 650);
        var scaled = WindowHitTest.ScreenExtent(configured, 1.25f, 1212f, 812f);

        Assert.True(WindowHitTest.Contains(new Vector2(1000, 700), origin, scaled));
        Assert.False(WindowHitTest.Contains(new Vector2(1000, 700), origin, configured));
    }
}
