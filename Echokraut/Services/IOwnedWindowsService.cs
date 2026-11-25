using System;

namespace Echokraut.Services;

/// <summary>
/// Lifetime of the plugin's own native windows, for code outside the UI layer (the E2E self-reload).
/// A native window outlives its <c>Close()</c> by several frames (closing animation, then the game
/// finalizes it); if the plugin unloads in between, the game finalizes it through code of the
/// unloaded plugin and crashes (E2E run 20261123-130938). The window manager reports every frame
/// whether any window still has a native addon, and closes them all on request.
/// </summary>
public interface IOwnedWindowsService
{
    /// <summary>True while any own window still has a native addon (open, or closing and not yet finalized).</summary>
    bool AnyAlive { get; }

    /// <summary>Raised by <see cref="RequestCloseAll"/>; the window manager closes every own window.</summary>
    event Action? CloseAllRequested;

    /// <summary>Asks the window manager to close every own window. Framework thread only (KamiToolKit).</summary>
    void RequestCloseAll();

    /// <summary>Called by the window manager once per frame.</summary>
    void ReportAlive(bool anyAlive);
}
