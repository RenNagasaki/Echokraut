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
    public void AssumedLegacyVersion_StaysBehindTheShippedRelease()
    {
        // Until 2026-09-09 this asserted equality with RemoteUrls' tag, as the reminder for the day a
        // newer wrapper ships. That day has come (shipped: 0.0.1.6): the constant must NOT follow —
        // pre-handshake installs can only be running the release that was current back then, and
        // raising it would tell them they are up to date while they are two releases behind.
        var shipped = Services.RemoteUrlService.LoadEmbeddedFallback().EchokrauTtsVersion;

        Assert.NotEqual(shipped, WrapperUpdatePolicy.AssumedLegacyVersion);
        Assert.True(WrapperUpdatePolicy.IsAtLeast(shipped, WrapperUpdatePolicy.AssumedLegacyVersion),
            "the shipped release must not be older than the version assumed for legacy installs");
    }
}
