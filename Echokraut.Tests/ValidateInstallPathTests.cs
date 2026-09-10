using Echokraut.Windows.Native;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Coverage for <see cref="NativeAlltalkBuilder.ValidateInstallPath"/> — the gate that decides
/// whether the AllTalk installer is allowed to run with a given path. A subtle bug had
/// drive-root paths (e.g. <c>"C:\"</c>) passing validation, which made the install task
/// throw <see cref="System.UnauthorizedAccessException"/> deep inside a Task.Run that
/// silently swallowed it; the UI then sat at "Preparing installer..." forever. Pinned here.
/// </summary>
public class ValidateInstallPathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateInstallPath_EmptyOrWhitespace_Rejected(string? path)
    {
        var (ok, _) = NativeAlltalkBuilder.ValidateInstallPath(path);
        Assert.False(ok);
    }

    [Theory]
    [InlineData("alltalk_tts")]            // not rooted
    [InlineData("./relative")]             // not rooted
    [InlineData("../up")]                  // not rooted
    public void ValidateInstallPath_NotRooted_Rejected(string path)
    {
        var (ok, msg) = NativeAlltalkBuilder.ValidateInstallPath(path);
        Assert.False(ok);
        Assert.Contains("absolute", msg, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("C:")]
    [InlineData("D:\\")]
    [InlineData("d:")]
    public void ValidateInstallPath_DriveRootOnly_Rejected(string path)
    {
        // Regression: drive-root paths used to pass and then the install task hung at
        // "Preparing installer..." because writing to the drive root needs admin and
        // the resulting UnauthorizedAccessException wasn't surfaced to the UI.
        var (ok, msg) = NativeAlltalkBuilder.ValidateInstallPath(path);
        Assert.False(ok);
        Assert.Contains("subfolder", msg, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C:\\path with spaces")]
    [InlineData("C:\\with-dash")]
    public void ValidateInstallPath_SpacesOrDashes_Rejected(string path)
    {
        var (ok, msg) = NativeAlltalkBuilder.ValidateInstallPath(path);
        Assert.False(ok);
        Assert.Contains("spaces or dashes", msg);
    }

    [Theory]
    [InlineData("C:\\alltalk_tts")]
    [InlineData("D:\\Echokraut\\alltalk_tts")]
    [InlineData("C:\\subfolder\\alltalk_tts")]
    public void ValidateInstallPath_HappyPath_Accepted(string path)
    {
        var (ok, msg) = NativeAlltalkBuilder.ValidateInstallPath(path);
        Assert.True(ok, $"Expected '{path}' to validate; got: {msg}");
        Assert.Equal(string.Empty, msg);
    }
}
