using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Covers the one-shot migration from the legacy <c>LocalInstance</c>/<c>RemoteInstance</c>/
/// <c>NoInstance</c> booleans to the canonical <see cref="AlltalkInstanceType"/> enum.
/// Old configs persist the booleans; new configs persist <c>InstanceType</c> directly.
/// </summary>
public class AlltalkDataMigrationTests
{
#pragma warning disable CS0618 // Tests intentionally exercise the obsolete legacy fields.

    [Fact]
    public void Migrate_LocalBoolean_SetsInstanceTypeLocal()
    {
        var data = new AlltalkData { LocalInstance = true };

        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(AlltalkInstanceType.Local, data.InstanceType);
        Assert.False(data.LocalInstance);
        Assert.False(data.RemoteInstance);
        Assert.False(data.NoInstance);
    }

    [Fact]
    public void Migrate_RemoteBoolean_SetsInstanceTypeRemote()
    {
        var data = new AlltalkData { RemoteInstance = true };

        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(AlltalkInstanceType.Remote, data.InstanceType);
        Assert.False(data.RemoteInstance);
    }

    [Fact]
    public void Migrate_NoInstanceBoolean_KeepsInstanceTypeNone()
    {
        var data = new AlltalkData { NoInstance = true };

        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(AlltalkInstanceType.None, data.InstanceType);
        Assert.False(data.NoInstance);
    }

    [Fact]
    public void Migrate_AllBooleansFalse_LeavesInstanceTypeNone()
    {
        var data = new AlltalkData();

        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(AlltalkInstanceType.None, data.InstanceType);
        Assert.False(data.LocalInstance);
        Assert.False(data.RemoteInstance);
        Assert.False(data.NoInstance);
    }

    [Fact]
    public void Migrate_InstanceTypeAlreadySet_DoesNotOverride()
    {
        // New configs persist InstanceType directly. Migration must not clobber a non-default
        // enum value with derived data from booleans (which by then are inert anyway).
        var data = new AlltalkData
        {
            InstanceType = AlltalkInstanceType.Remote,
            LocalInstance = true, // Stale boolean — should not win.
        };

        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(AlltalkInstanceType.Remote, data.InstanceType);
        Assert.False(data.LocalInstance); // Cleared regardless.
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var data = new AlltalkData { LocalInstance = true };

        data.MigrateLegacyInstanceTypeFields();
        var afterFirst = data.InstanceType;
        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(afterFirst, data.InstanceType);
        Assert.Equal(AlltalkInstanceType.Local, data.InstanceType);
        Assert.False(data.LocalInstance);
    }

    [Fact]
    public void Migrate_LocalAndRemoteBothTrue_PrefersLocal()
    {
        // Defensive: if a corrupt config has multiple booleans set, Local wins (matching the
        // original computed-property's order: Local → Remote → None).
        var data = new AlltalkData { LocalInstance = true, RemoteInstance = true };

        data.MigrateLegacyInstanceTypeFields();

        Assert.Equal(AlltalkInstanceType.Local, data.InstanceType);
    }

#pragma warning restore CS0618
}
