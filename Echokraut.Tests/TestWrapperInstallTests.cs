using System;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The developer-only gate in front of the "Install Test" button: a dev load, the zip in the wrapper
/// checkout next to the plugin checkout, and nothing else — deliberately nothing about the player.
/// The file check is injected so these tests never touch a real path.
/// </summary>
public class TestWrapperInstallTests
{
    private static readonly string Dll = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "repos", "Echokraut", "Echokraut", "bin", "x64", "Debug", "Echokraut.dll");

    [Fact]
    public void ZipPathFor_DevLoad_IsTheWrapperCheckoutNextToThePluginCheckout()
    {
        var expected = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "repos", "Echokrautts", "wrapper", "EchokrauTTS.zip");
        Assert.Equal(expected, TestWrapperInstall.ZipPathFor(Dll, isDev: true));
    }

    [Fact]
    public void ZipPathFor_NoDevLoad_IsNull()
    {
        // An installed copy is never a dev load: there is nothing to look for, not even a probe.
        Assert.Null(TestWrapperInstall.ZipPathFor(Dll, isDev: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Echokraut.dll")] // too shallow for the checkout layout
    public void ZipPathFor_NoUsableDllPath_IsNull(string? dll)
    {
        Assert.Null(TestWrapperInstall.ZipPathFor(dll, isDev: true));
    }

    [Fact]
    public void IsAvailable_ZipPresent_IsTrue()
    {
        Assert.True(TestWrapperInstall.IsAvailable("x.zip", _ => true));
    }

    [Fact]
    public void IsAvailable_NoZip_IsFalse()
    {
        // The state on every dev machine until a build was made.
        Assert.False(TestWrapperInstall.IsAvailable("x.zip", _ => false));
    }

    [Fact]
    public void IsAvailable_NoCandidatePath_IsFalseWithoutProbing()
    {
        var probed = false;
        Assert.False(TestWrapperInstall.IsAvailable(null, _ => probed = true));
        Assert.False(probed);
    }

    [Fact]
    public void IsAvailable_ChecksTheGivenPath()
    {
        string? asked = null;
        TestWrapperInstall.IsAvailable("some.zip", p => { asked = p; return true; });
        Assert.Equal("some.zip", asked);
    }

    [Fact]
    public void CatalogPath_OnlyForATestInstall()
    {
        Assert.Equal(System.IO.Path.Combine("root", "echokrautts", "engines.json"),
            TestWrapperInstall.CatalogPath(TestWrapperInstall.VersionMarker, System.IO.Path.Combine("root", "echokrautts")));
        // A released wrapper reads the catalog of its tag, never a file that happens to lie there.
        Assert.Null(TestWrapperInstall.CatalogPath("0.0.1.9", "root"));
        Assert.Null(TestWrapperInstall.CatalogPath(TestWrapperInstall.VersionMarker, ""));
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
        Assert.Throws<ArgumentNullException>(() => TestWrapperInstall.IsAvailable("x.zip", null!));
    }
}
