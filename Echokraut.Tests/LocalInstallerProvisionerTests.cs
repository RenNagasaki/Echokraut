using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>The version-aware "(re)download the installer?" decision (BLK-5).</summary>
public class LocalInstallerProvisionerTests
{
    [Fact]
    public void ShouldDownload_WhenExeMissing_IsTrue()
    {
        Assert.True(LocalInstallerProvisioner.ShouldDownload(exeExists: false, "ELI-1.1.0.0", "ELI-1.1.0.0"));
    }

    [Fact]
    public void ShouldDownload_WhenExePresentAndVersionsMatch_IsFalse()
    {
        Assert.False(LocalInstallerProvisioner.ShouldDownload(exeExists: true, "ELI-1.1.0.0", "ELI-1.1.0.0"));
    }

    [Fact]
    public void ShouldDownload_WhenExePresentButVersionDiffers_IsTrue()
    {
        // The exact stale-installer case: existing AllTalk user has ELI-1.0.0.1, a newer installer
        // with EchokrauTTS modes is expected — must re-download even though the exe exists.
        Assert.True(LocalInstallerProvisioner.ShouldDownload(exeExists: true, "ELI-1.0.0.1", "ELI-1.1.0.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ShouldDownload_NoExpectedVersion_OnlyDownloadsWhenMissing(string? expected)
    {
        Assert.False(LocalInstallerProvisioner.ShouldDownload(exeExists: true, "anything", expected));
        Assert.True(LocalInstallerProvisioner.ShouldDownload(exeExists: false, "anything", expected));
    }

    [Fact]
    public void ShouldDownload_InstalledVersionEmpty_ExpectedSet_IsTrue()
    {
        // Fresh config (no recorded installed version) + an expected version → (re)download.
        Assert.True(LocalInstallerProvisioner.ShouldDownload(exeExists: true, "", "ELI-1.1.0.0"));
    }
}
