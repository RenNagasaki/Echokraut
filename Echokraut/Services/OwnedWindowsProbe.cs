using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Echokraut.Services;

/// <summary>Internal names of the own native windows (the <c>InternalName</c> each window is created with).</summary>
public static class OwnedWindowIds
{
    public const string Config = "EchokrautSettings";
    public const string FirstTime = "EchokrautFirstTime";
    public const string VoiceClipManager = "EchokrautEncounters";
    public const string VoiceClipDetail = "EchokrautEncounterDetail";
    public const string GameDataTools = "EchokrautGameTools";
    public const string Changelog = "EchokrautChangelog";
}

/// <summary>
/// Names of the config window's clicks WITH a side effect (plan › B8): runnable through
/// <see cref="OwnedWindowProbeKind.RunStep"/> but never listed by <see cref="OwnedWindowProbeKind.SweepSteps"/>.
/// </summary>
public static class OwnedWindowActions
{
    /// <summary>The wrapper button on the Backend tab: "Check for updates", then "Update".</summary>
    public const string WrapperUpdate = "action:wrapper-update";

    /// <summary>A pick of that engine's entry in the Backend tab's engine dropdown, through the dropdown's handler.</summary>
    public static string EnginePick(string engineId) => $"action:engine-pick:{engineId}";
}

/// <summary>What a probe asks the window manager to do (plan <c>docs/plans/ingame-e2e.md</c> › B5).</summary>
public enum OwnedWindowProbeKind
{
    /// <summary>State of every own window, with the visible texts of the open ones.</summary>
    Snapshot,
    Open,
    Close,
    /// <summary>Moves the window's top-left corner to <see cref="OwnedWindowsProbe.Position"/> (screen pixels).</summary>
    Move,
    /// <summary>The side-effect-free steps the window offers (tabs, dropdown open/close, pages, safe buttons).</summary>
    SweepSteps,
    /// <summary>Runs <see cref="OwnedWindowsProbe.Step"/> once, like a click on that node.</summary>
    RunStep,
}

/// <summary>
/// One visible text node of a window: its text, the width the node has, the width the text needs and where it
/// sits on the screen (to read lists top to bottom).
/// </summary>
public sealed record OwnedWindowText(string Text, float NodeWidth, float DrawWidth, bool Wraps, float ScreenX = 0, float ScreenY = 0)
{
    /// <summary>A single-line text that needs more room than its node gives it (1 px slack for rounding).</summary>
    public bool Overflows => !Wraps && NodeWidth > 0 && DrawWidth > NodeWidth + 1f;
}

/// <summary>One own window as the game draws it. Position and size in screen pixels (size scaled).</summary>
public sealed record OwnedWindowState(string Id, bool IsOpen, bool Alive, Vector2 Position, Vector2 Size,
    Vector2 Screen, IReadOnlyList<OwnedWindowText> Texts)
{
    /// <summary>Share of the window's width (X) and height (Y) that lies on the screen, 0..1 each.</summary>
    public Vector2 VisibleFraction => VisibleShare(Position, Size, Screen);

    internal static Vector2 VisibleShare(Vector2 position, Vector2 size, Vector2 screen)
    {
        static float Axis(float start, float length, float screenLength)
        {
            if (length <= 0) return 0;
            var visible = Math.Min(start + length, screenLength) - Math.Max(start, 0);
            return Math.Clamp(visible / length, 0, 1);
        }
        return new Vector2(Axis(position.X, size.X, screen.X), Axis(position.Y, size.Y, screen.Y));
    }

    /// <summary>True when one visible text node shows exactly <paramref name="text"/>.</summary>
    public bool Shows(string text) => Texts.Any(t => string.Equals(t.Text, text, StringComparison.Ordinal));
}

/// <summary>
/// A request to the window manager, handled synchronously on the framework thread by
/// <see cref="IOwnedWindowsService.Probe"/>: the caller reads the result fields right after the call.
/// </summary>
public sealed class OwnedWindowsProbe
{
    public OwnedWindowsProbe(OwnedWindowProbeKind kind, string? windowId = null, Vector2 position = default, string? step = null)
    {
        Kind = kind;
        WindowId = windowId;
        Position = position;
        Step = step;
    }

    public OwnedWindowProbeKind Kind { get; }
    public string? WindowId { get; }
    public Vector2 Position { get; }
    public string? Step { get; }

    /// <summary>Set by the handler. False = nobody handled the probe (no window manager).</summary>
    public bool Handled { get; set; }

    /// <summary>Why the probe could not be done (unknown window, unknown step, window closed …), null when it was.</summary>
    public string? Error { get; set; }

    public IReadOnlyList<OwnedWindowState> Windows { get; set; } = [];
    public IReadOnlyList<string> Steps { get; set; } = [];
}
