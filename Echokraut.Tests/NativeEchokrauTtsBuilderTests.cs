using Echokraut.Windows.Native;
using Xunit;

namespace Echokraut.Tests;

public class NativeEchokrauTtsBuilderTests
{
    [Fact]
    public void StatusLine_ShowsWhyTheLaunchEnded()
        => Assert.Equal("EchokrauTTS stopped: no model",
            NativeEchokrauTtsBuilder.LocalInstanceNodes.StatusLine(true, "bad path", "EchokrauTTS stopped: no model"));

    [Fact]
    public void StatusLine_BadPathComesFirst()
        => Assert.Equal("bad path",
            NativeEchokrauTtsBuilder.LocalInstanceNodes.StatusLine(false, "bad path", "EchokrauTTS stopped: no model"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void StatusLine_NothingToSay_IsHidden(string? launchFailure)
        => Assert.Null(NativeEchokrauTtsBuilder.LocalInstanceNodes.StatusLine(true, "bad path", launchFailure));
}
