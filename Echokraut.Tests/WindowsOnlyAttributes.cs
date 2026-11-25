using System;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The plugin only ever runs on Windows (natively or under Wine), so code built on
/// <see cref="System.IO.Path"/> rules — drive roots, <c>C:\</c> being fully qualified,
/// <c>?</c>/<c>:</c> being invalid file-name characters — is correct there and only there.
/// Tests that pin those rules use these attributes: they run on Windows and are reported as
/// skipped (not silently dropped) when the suite runs on macOS/Linux, e.g. a dev build on a Mac.
/// </summary>
internal static class WindowsOnly
{
    public const string SkipReason = "Windows path semantics — the plugin only runs on Windows/Wine.";

    public static string? Skip => OperatingSystem.IsWindows() ? null : SkipReason;
}

/// <summary><see cref="FactAttribute"/> that is skipped outside Windows. See <see cref="WindowsOnly"/>.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() => Skip = WindowsOnly.Skip;
}

/// <summary><see cref="TheoryAttribute"/> that is skipped outside Windows. See <see cref="WindowsOnly"/>.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute() => Skip = WindowsOnly.Skip;
}
