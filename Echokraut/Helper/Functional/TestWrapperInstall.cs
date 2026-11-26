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
/// <para><b>The gate: a dev load plus a source-checkout layout, never a setting.</b> The zip is only
/// looked for when Dalamud loaded the plugin as a dev plugin (<c>IsDev</c>), and only in the
/// wrapper checkout that sits next to the plugin checkout the DLL was built in
/// (<see cref="ZipPathFor"/>): <c>&lt;repos&gt;/Echokraut/Echokraut/bin/x64/Debug/Echokraut.dll</c> →
/// <c>&lt;repos&gt;/Echokrautts/wrapper/EchokrauTTS.zip</c>. An installed copy is no dev load, so the
/// button never appears. It must not become a configuration value: this unpacks and executes an
/// unreviewed archive, so a user-steerable path would turn a developer convenience into a way to
/// talk someone into running a foreign zip. (Until 2026-11-22 the gate was an absolute path of the
/// developer's machine committed here; it went stale when the checkouts moved, and a machine path
/// does not belong in a public repo.)</para>
///
/// <para>The gate deliberately involves <b>nothing about the player</b> — no character name, no
/// content id. An identifying value committed here would tie the author to a game account for
/// anyone reading the repository, and it would buy nothing.</para>
/// </summary>
public static class TestWrapperInstall
{
    /// <summary>File name of the locally built wrapper zip (what <c>build-release-zip.py</c> writes).</summary>
    public const string ZipFileName = "EchokrauTTS.zip";

    /// <summary>Folder name of the wrapper checkout next to the plugin checkout.</summary>
    public const string WrapperCheckoutName = "Echokrautts";

    /// <summary>
    /// Folder levels from the DLL's folder up to the folder holding the checkouts:
    /// <c>Debug</c> → <c>x64</c> → <c>bin</c> → project <c>Echokraut</c> → repo <c>Echokraut</c> → repos.
    /// </summary>
    internal const int LevelsToCheckouts = 5;

    /// <summary>
    /// Engine catalog shipped inside the test zip, at its root. A release zip has none (the plugin
    /// reads <c>engines.json</c> from the wrapper repo at the release tag), but a test build is not at
    /// any tag: the tag's catalog would hide exactly the engines the build is meant to test.
    /// <c>tools/e2e/push-test-wrapper.sh</c> adds the checkout's <c>engines.json</c> to the zip; after a
    /// test install it lies in <c>echokrautts/</c> and <see cref="CatalogPath"/> finds it there.
    /// </summary>
    public const string CatalogFileName = "engines.json";

    /// <summary>
    /// Recorded as <c>InstalledWrapperVersion</c> after a test install. Deliberately not a tag: the
    /// Backend tab then says plainly that a test build is installed, and because
    /// <c>WrapperUpdatePolicy</c> compares tags as opaque strings, any real release still reads as
    /// "update available" — so the test build can always be replaced by a published one.
    /// </summary>
    public const string VersionMarker = "test (local zip)";

    /// <summary>
    /// Where the test zip would lie for a plugin loaded from <paramref name="pluginDllPath"/>, or null
    /// when there is none to look for: no dev load, no DLL path, or a folder too shallow for the
    /// checkout layout. The result is a candidate — <see cref="IsAvailable"/> says whether it exists.
    /// </summary>
    public static string? ZipPathFor(string? pluginDllPath, bool isDev)
    {
        if (!isDev || string.IsNullOrWhiteSpace(pluginDllPath)) return null;
        var dir = Path.GetDirectoryName(pluginDllPath);
        for (var i = 0; i < LevelsToCheckouts && !string.IsNullOrEmpty(dir); i++)
            dir = Path.GetDirectoryName(dir);
        if (string.IsNullOrEmpty(dir)) return null;
        return Path.Combine(dir, WrapperCheckoutName, "wrapper", ZipFileName);
    }

    /// <summary>
    /// True when the test install may be offered, i.e. there is a candidate path and the zip is
    /// actually there. Testable overload — <paramref name="fileExists"/> is <see cref="File.Exists"/>
    /// in production.
    /// </summary>
    public static bool IsAvailable(string? zipPath, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return zipPath != null && fileExists(zipPath);
    }

    /// <inheritdoc cref="IsAvailable(string?, Func{string, bool})"/>
    public static bool IsAvailable(string? zipPath) => IsAvailable(zipPath, File.Exists);

    /// <summary>True when <paramref name="installedVersion"/> is the marker a test install records.</summary>
    public static bool IsTestInstall(string? installedVersion) =>
        string.Equals(installedVersion, VersionMarker, StringComparison.Ordinal);

    /// <summary>
    /// Where a test install's own catalog lies (<paramref name="wrapperFolder"/> = <c>echokrautts/</c>
    /// under the install root), or null when the installed wrapper is no test build — a released
    /// wrapper always reads the catalog of its tag.
    /// </summary>
    public static string? CatalogPath(string? installedVersion, string? wrapperFolder) =>
        IsTestInstall(installedVersion) && !string.IsNullOrWhiteSpace(wrapperFolder)
            ? Path.Combine(wrapperFolder, CatalogFileName)
            : null;
}
