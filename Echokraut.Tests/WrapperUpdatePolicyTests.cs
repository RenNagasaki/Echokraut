using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The EchokrauTTS wrapper version handshake: when is the Update button offered, and what does the
/// version label read? Pure rules — the actual update runs through the installer.
/// </summary>
public class WrapperUpdatePolicyTests
{
    [Fact]
    public void IsUpdateAvailable_NoLocalInstall_IsFalse()
    {
        // Remote-only / None users have no wrapper on disk — nothing to update.
        Assert.False(WrapperUpdatePolicy.IsUpdateAvailable(localInstall: false, "0.0.0.1", "0.0.0.2"));
    }

    [Fact]
    public void IsUpdateAvailable_VersionsMatch_IsFalse()
    {
        Assert.False(WrapperUpdatePolicy.IsUpdateAvailable(localInstall: true, "0.0.0.2", "0.0.0.2"));
    }

    [Fact]
    public void IsUpdateAvailable_NewerRemoteTag_IsTrue()
    {
        Assert.True(WrapperUpdatePolicy.IsUpdateAvailable(localInstall: true, "0.0.0.2", "0.0.0.3"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void IsUpdateAvailable_InstalledVersionUnknown_IsTrue(string? installed)
    {
        // Installs made before the handshake existed record no tag. Their wrapper predates the
        // current release by definition, so offering the update is the correct default.
        Assert.True(WrapperUpdatePolicy.IsUpdateAvailable(localInstall: true, installed, "0.0.0.3"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void IsUpdateAvailable_NoRemoteTag_IsFalse(string? latest)
    {
        // No published wrapper release (or RemoteUrls not maintained) → never offer an update,
        // even though "installed != latest" would technically hold.
        Assert.False(WrapperUpdatePolicy.IsUpdateAvailable(localInstall: true, "0.0.0.2", latest));
    }

    [Fact]
    public void IsUpdateAvailable_RemoteTagRolledBack_IsTrue()
    {
        // Comparison is deliberately "differs", not "is greater": the remote tag is what we want
        // installed, so a rollback published upstream must also be offered.
        Assert.True(WrapperUpdatePolicy.IsUpdateAvailable(localInstall: true, "0.0.0.9", "0.0.0.2"));
    }

    [Fact]
    public void Display_UnknownVersion_FallsBackToMarker()
    {
        Assert.Equal(WrapperUpdatePolicy.UnknownVersion, WrapperUpdatePolicy.Display(null));
        Assert.Equal(WrapperUpdatePolicy.UnknownVersion, WrapperUpdatePolicy.Display(""));
        Assert.Equal("0.0.0.2", WrapperUpdatePolicy.Display("0.0.0.2"));
    }

    [Fact]
    public void BuildVersionLabel_BothKnown_ShowsInstalledAndLatest()
    {
        var label = WrapperUpdatePolicy.BuildVersionLabel("0.0.0.2", "0.0.0.3", "Wrapper", "latest");
        Assert.Equal("Wrapper: 0.0.0.2 (latest: 0.0.0.3)", label);
    }

    [Fact]
    public void BuildVersionLabel_NoRemoteTag_OmitsLatestHalf()
    {
        var label = WrapperUpdatePolicy.BuildVersionLabel("0.0.0.2", "", "Wrapper", "latest");
        Assert.Equal("Wrapper: 0.0.0.2", label);
    }

    [Fact]
    public void BuildVersionLabel_UnknownInstalled_ShowsMarkerNotEmptyString()
    {
        var label = WrapperUpdatePolicy.BuildVersionLabel("", "0.0.0.3", "Wrapper", "latest");
        Assert.Equal($"Wrapper: {WrapperUpdatePolicy.UnknownVersion} (latest: 0.0.0.3)", label);
    }

    [Fact]
    public void StateAfterInstall_ReturnsToTheCheckOffer()
    {
        // The reported bug: after the update wrote the new tag, the button kept offering "Update".
        // It now goes back to "Check for updates" — an always-actionable state, which a greyed-out
        // "Update" is not (see IsButtonActionable).
        Assert.Equal(WrapperUpdateState.NotChecked, WrapperUpdatePolicy.StateAfterInstall());
    }

    [Theory]
    [InlineData(WrapperUpdateState.NotChecked)]
    [InlineData(WrapperUpdateState.CheckFailed)]
    [InlineData(WrapperUpdateState.UpdateAvailable)]
    public void IsButtonActionable_OfferedStates_AreClickable(WrapperUpdateState state)
    {
        Assert.True(WrapperUpdatePolicy.IsButtonActionable(state));
    }

    [Theory]
    [InlineData(WrapperUpdateState.Checking)]
    [InlineData(WrapperUpdateState.UpToDate)]
    public void IsButtonActionable_DeadStates_AreRejected(WrapperUpdateState state)
    {
        // Dimming a node only lowers its alpha — ATK still delivers the click, so the click handler
        // has to reject these itself. Same rule drives the dimming, so the two cannot drift apart.
        Assert.False(WrapperUpdatePolicy.IsButtonActionable(state));
    }
}
