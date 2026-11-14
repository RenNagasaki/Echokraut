using System.Numerics;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Rectangle hit-test for "did this click land inside one of our own native windows?".
///
/// <para>Factored out as a pure function because the decision it feeds is not cosmetic: a click
/// the test misses reaches the Talk addon's own handler and <b>advances the NPC dialogue</b>, so
/// interacting with an Echokraut window during a conversation skips lines. The bug it fixes was
/// exactly that — the caller compared against the window's <i>configured</i> size while the game
/// draws the addon at <c>size * addon-&gt;Scale</c>. At any HUD scale above 100% the rectangle was
/// too small and clicks in the right/bottom part of the window fell through.</para>
/// </summary>
public static class WindowHitTest
{
    /// <summary>
    /// True when <paramref name="pos"/> lies inside the rectangle at <paramref name="origin"/> with
    /// <paramref name="size"/>. A non-positive extent never contains anything — a window that
    /// reports no size is not on screen.
    /// </summary>
    public static bool Contains(Vector2 pos, Vector2 origin, Vector2 size)
    {
        if (size.X <= 0f || size.Y <= 0f) return false;
        return pos.X >= origin.X && pos.X <= origin.X + size.X
            && pos.Y >= origin.Y && pos.Y <= origin.Y + size.Y;
    }

    /// <summary>
    /// On-screen extent of an addon: what the game actually draws, i.e. the configured size times
    /// the addon's scale. <paramref name="scaledWidth"/>/<paramref name="scaledHeight"/> come from
    /// the addon itself and win when they are usable; the scaled fallback covers the frames where
    /// the addon has not measured itself yet (it reports 0 between open and first layout).
    /// </summary>
    public static Vector2 ScreenExtent(Vector2 configuredSize, float scale, float scaledWidth, float scaledHeight)
    {
        if (scaledWidth > 0f && scaledHeight > 0f) return new Vector2(scaledWidth, scaledHeight);
        var s = scale <= 0f ? 1f : scale;
        return configuredSize * s;
    }
}
