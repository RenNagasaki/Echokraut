using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// One-shot backfill of the wrapper tag for local installs that predate the version handshake.
/// Without it those users see "?" and are offered an update that re-downloads the build they
/// already run.
/// </summary>
public class WrapperVersionMigrationTests
{
    private static Configuration WithLocalInstall(string installedVersion) => new()
    {
        EchokrauTts = new EchokrauTtsData
        {
            LocalInstall = true,
            InstalledWrapperVersion = installedVersion,
        },
    };

    [Fact]
    public void LocalInstallWithoutVersion_GetsTheAssumedLegacyVersion()
    {
        var config = WithLocalInstall("");

        config.MigrateWrapperVersionForExistingInstalls();

        Assert.Equal(WrapperUpdatePolicy.AssumedLegacyVersion, config.EchokrauTts.InstalledWrapperVersion);
    }

    [Fact]
    public void MigratedInstall_IsNotOfferedAnUpdateForTheSameRelease()
    {
        // The point of the migration: with RemoteUrls still on the assumed release, the Update
        // button must stay hidden instead of proposing a pointless re-download.
        var config = WithLocalInstall("");

        config.MigrateWrapperVersionForExistingInstalls();

        Assert.False(WrapperUpdatePolicy.IsUpdateAvailable(
            config.EchokrauTts.LocalInstall,
            config.EchokrauTts.InstalledWrapperVersion,
            WrapperUpdatePolicy.AssumedLegacyVersion));
    }

    [Fact]
    public void MigratedInstall_IsStillOfferedANewerRelease()
    {
        var config = WithLocalInstall("");

        config.MigrateWrapperVersionForExistingInstalls();

        Assert.True(WrapperUpdatePolicy.IsUpdateAvailable(
            config.EchokrauTts.LocalInstall, config.EchokrauTts.InstalledWrapperVersion, "0.0.0.3"));
    }

    [Fact]
    public void RecordedVersion_IsNeverOverwritten()
    {
        var config = WithLocalInstall("0.0.0.7");

        config.MigrateWrapperVersionForExistingInstalls();

        Assert.Equal("0.0.0.7", config.EchokrauTts.InstalledWrapperVersion);
    }

    [Fact]
    public void WithoutLocalInstall_NothingIsAssumed()
    {
        // Remote / None users have no wrapper on disk — inventing a tag for them would later make a
        // fresh install look up to date before it has run.
        var config = new Configuration { EchokrauTts = new EchokrauTtsData { LocalInstall = false } };

        config.MigrateWrapperVersionForExistingInstalls();

        Assert.Equal("", config.EchokrauTts.InstalledWrapperVersion);
    }

    [Fact]
    public void RunningTwice_IsIdempotent()
    {
        // Migrations run on every plugin start.
        var config = WithLocalInstall("");

        config.MigrateWrapperVersionForExistingInstalls();
        config.MigrateWrapperVersionForExistingInstalls();

        Assert.Equal(WrapperUpdatePolicy.AssumedLegacyVersion, config.EchokrauTts.InstalledWrapperVersion);
    }

    [Fact]
    public void AssumedLegacyVersion_MatchesTheWrapperReleaseWeShip()
    {
        // The assumption is only sound while RemoteUrls still points at that release. Once a newer
        // wrapper is published this test flips to red — which is the reminder NOT to bump the
        // constant (existing installs are then genuinely behind and must be offered the update),
        // but to delete this assertion along with it.
        var shipped = Services.RemoteUrlService.LoadEmbeddedFallback().EchokrauTtsVersion;

        Assert.Equal(shipped, WrapperUpdatePolicy.AssumedLegacyVersion);
    }
}
