using System;
using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Developer-only escape hatch for testing an EchokrauTTS wrapper build that has no GitHub release
/// yet.
///
/// <para><b>Why this exists.</b> The normal install path can only ever fetch a published release:
/// the download URL comes from <c>RemoteUrls.json</c>, which is loaded from the plugin's <c>main</c>
/// branch and beats the embedded copy, so editing the local file changes nothing at runtime; and the
/// update check asks GitHub for <c>/releases/latest</c>, which ignores drafts and pre-releases. So
/// an untagged wrapper could be run (by copying it into place and pressing Start) but never
/// <i>installed</i> — the very path most likely to break went untested until release day.</para>
///
/// <para><b>The path is the whole gate, and it is hard-coded on purpose.</b> It is a developer
/// machine's absolute path inside a source checkout, so on any installed copy of the plugin the file
/// simply is not there and the button never appears. It must not become a configuration value
/// either: this unpacks and executes an unreviewed archive, so a user-steerable path would turn a
/// developer convenience into a way to talk someone into running a foreign zip.</para>
///
/// <para>The gate deliberately involves <b>nothing about the player</b> — no character name, no
/// content id. An identifying value committed here would tie the author to a game account for
/// anyone reading the repository, and it would buy nothing: the path already restricts this to one
/// machine, and a character name is freely chosen, so it never was a boundary in the first place.</para>
/// </summary>
public static class TestWrapperInstall
{
    /// <summary>
    /// The locally built wrapper zip. This is the artifact the wrapper repo produces from its
    /// <c>wrapper/</c> folder — its entries sit at the zip root (<c>bootstrap/</c>, <c>src/</c>,
    /// <c>config.json</c>, …), with no wrapping directory, exactly like a release zip.
    /// </summary>
    public const string ZipPath = @"F:\Git-Repositories\Dalamud\Echokrautts\wrapper\EchokrauTTS.zip";

    /// <summary>
    /// Recorded as <c>InstalledWrapperVersion</c> after a test install. Deliberately not a tag: the
    /// Backend tab then says plainly that a test build is installed, and because
    /// <c>WrapperUpdatePolicy</c> compares tags as opaque strings, any real release still reads as
    /// "update available" — so the test build can always be replaced by a published one.
    /// </summary>
    public const string VersionMarker = "test (local zip)";

    /// <summary>
    /// True when the test install may be offered, i.e. the locally built zip is actually there.
    /// Testable overload — <paramref name="fileExists"/> is <see cref="File.Exists"/> in production.
    /// </summary>
    public static bool IsAvailable(Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return fileExists(ZipPath);
    }

    /// <inheritdoc cref="IsAvailable(Func{string, bool})"/>
    public static bool IsAvailable() => IsAvailable(File.Exists);
}
