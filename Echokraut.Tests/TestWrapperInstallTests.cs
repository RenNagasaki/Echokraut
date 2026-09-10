using System;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The developer-only gate in front of the "Install Test" button. The gate is the hard-coded zip
/// path and nothing else — deliberately nothing about the player, so no identifying value is
/// committed. The file check is injected so these tests never touch the real path.
/// </summary>
public class TestWrapperInstallTests
{
    [Fact]
    public void IsAvailable_ZipPresent_IsTrue()
    {
        Assert.True(TestWrapperInstall.IsAvailable(_ => true));
    }

    [Fact]
    public void IsAvailable_NoZip_IsFalse()
    {
        // The state on every machine but the developer's — and on that one until a build was made.
        Assert.False(TestWrapperInstall.IsAvailable(_ => false));
    }

    [Fact]
    public void IsAvailable_ChecksTheDeclaredPath()
    {
        // The gate must ask about ZipPath itself — not some other file that happens to exist.
        string? asked = null;
        TestWrapperInstall.IsAvailable(p => { asked = p; return true; });
        Assert.Equal(TestWrapperInstall.ZipPath, asked);
    }

    [Fact]
    public void ZipPath_IsAbsoluteAndNotRelativeToAnInstall()
    {
        // A relative or plugin-relative path would resolve on a user's machine too; the whole gate
        // rests on this being an absolute path inside a source checkout.
        Assert.True(System.IO.Path.IsPathFullyQualified(TestWrapperInstall.ZipPath));
    }

    [Fact]
    public void VersionMarker_IsNotAReleaseTag()
    {
        // WrapperUpdatePolicy compares tags as opaque strings, so the marker only does its job (the
        // Backend tab says "test build", and a real release still reads as an available update) as
        // long as it can never equal a published tag.
        Assert.NotEqual(WrapperUpdatePolicy.AssumedLegacyVersion, TestWrapperInstall.VersionMarker);
        Assert.NotEqual(WrapperUpdatePolicy.UnknownVersion, TestWrapperInstall.VersionMarker);
        Assert.True(WrapperUpdatePolicy.IsUpdateAvailable(
            localInstall: true, TestWrapperInstall.VersionMarker, "0.0.0.5"));
    }

    [Fact]
    public void IsAvailable_NullProbe_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TestWrapperInstall.IsAvailable(null!));
    }
}
