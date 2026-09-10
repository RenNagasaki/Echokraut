using System;
using Echokraut.Enums;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Pure rules for the EchokrauTTS wrapper version handshake: is a newer wrapper release available
/// for the local install, and how is that shown to the user? Kept separate from
/// <see cref="LocalInstallerProvisioner"/> because that one governs the *installer* exe — this one
/// governs the *wrapper* under <c>{TtsInstallRoot}\echokrautts</c>.
///
/// <para>Tags are compared as opaque strings (ordinal), never parsed as versions: the wrapper repo
/// tags releases however it likes, and "different from what we shipped" is the only signal that
/// actually matters. A downgrade therefore also reads as "update available", which is intentional —
/// the remote value is the one we want installed.</para>
/// </summary>
public static class WrapperUpdatePolicy
{
    /// <summary>Shown in place of a tag that was never recorded and could not be assumed either.</summary>
    public const string UnknownVersion = "?";

    /// <summary>
    /// Wrapper release assumed for a local install that carries no recorded tag. Installs predating
    /// the handshake can only have this one — it is the only wrapper release published so far, and
    /// the one <c>RemoteUrls.json</c> has always pointed at. Assuming it (via
    /// <c>Configuration.MigrateWrapperVersionForExistingInstalls</c>) is better than showing "?" and
    /// offering an update that would re-download the very build the user already runs.
    /// <para>When a newer wrapper is published, do NOT bump this constant: existing installs are
    /// then genuinely behind and the normal handshake must offer them the update. That case is now
    /// live — <c>RemoteUrls.json</c> ships <c>0.0.1.6</c> while this stays at the release those
    /// installs actually ran.</para>
    /// </summary>
    public const string AssumedLegacyVersion = "0.0.0.2";

    /// <summary>
    /// True when the Update button should be offered: a local install exists, the remote tag is
    /// known, and it differs from what is recorded as installed. An empty <paramref name="latest"/>
    /// disables the offer entirely (no wrapper release published / RemoteUrls not maintained), and
    /// an empty <paramref name="installed"/> counts as a mismatch — those installs predate the
    /// handshake and their wrapper is by definition older than the current release.
    /// </summary>
    public static bool IsUpdateAvailable(bool localInstall, string? installed, string? latest)
    {
        if (!localInstall) return false;
        if (string.IsNullOrWhiteSpace(latest)) return false;
        return !string.Equals(installed, latest, StringComparison.Ordinal);
    }

    /// <summary>
    /// Button state after an install/update wrote a new tag to disk: back to
    /// <see cref="WrapperUpdateState.NotChecked"/>, i.e. the button offers the check again.
    /// <para>Without resetting, a finished update left the state at
    /// <see cref="WrapperUpdateState.UpdateAvailable"/> and the button kept offering an install that
    /// already ran. Going to <see cref="WrapperUpdateState.UpToDate"/> instead would be the more
    /// informative answer, but it only *looks* disabled — see <see cref="IsButtonActionable"/> — and
    /// the freshly installed tag is anyway the newest thing we know, so "ask again if you want to
    /// know" is both honest and always actionable.</para>
    /// </summary>
    public static WrapperUpdateState StateAfterInstall() => WrapperUpdateState.NotChecked;

    /// <summary>
    /// Whether a click on the wrapper button may do anything. <b>The greyed-out look is not a
    /// guard</b>: dimming a KamiToolKit node only lowers its alpha, ATK still delivers the click to
    /// the component — so every caller must ask this before acting, and the same answer drives the
    /// dimming so look and behaviour cannot drift apart.
    /// </summary>
    public static bool IsButtonActionable(WrapperUpdateState state)
        => state is WrapperUpdateState.NotChecked
                 or WrapperUpdateState.CheckFailed
                 or WrapperUpdateState.UpdateAvailable;

    /// <summary>Display form of a possibly-unrecorded tag.</summary>
    public static string Display(string? version)
        => string.IsNullOrWhiteSpace(version) ? UnknownVersion : version;

    /// <summary>
    /// One-line "installed vs. available" label for the Backend tab, e.g.
    /// <c>Wrapper: 0.0.0.2 (latest: 0.0.0.3)</c>. <paramref name="installedCaption"/> /
    /// <paramref name="latestCaption"/> come from the localization layer so this stays free of any
    /// UI dependency; the "latest" half is omitted when no remote tag is known.
    /// </summary>
    public static string BuildVersionLabel(string? installed, string? latest,
        string installedCaption, string latestCaption)
    {
        var head = $"{installedCaption}: {Display(installed)}";
        return string.IsNullOrWhiteSpace(latest) ? head : $"{head} ({latestCaption}: {latest})";
    }

    /// <summary>
    /// Ordered comparison of two wrapper tags, used ONLY to gate engines behind their
    /// <c>minVersion</c> (see <see cref="TtsEngineAvailability"/>). The update offer above keeps its
    /// opaque string comparison on purpose — "different from what we ship" is the signal there, and a
    /// downgrade must still read as "update available". Here the question is genuinely ordered ("can
    /// the installed wrapper already run this engine?"), so the tag is parsed.
    /// <para>Tags are <c>X.Y.Z.W</c> with any number of segments; a missing segment counts as 0, so
    /// <c>0.1</c> equals <c>0.1.0.0</c>. Anything unparseable answers <b>true</b>: a tag scheme we do
    /// not understand must not hide engines the user may well be able to run — a wrong "not yet
    /// available" is the more expensive mistake, since it silently removes a working choice.</para>
    /// </summary>
    public static bool IsAtLeast(string? installed, string? required)
    {
        if (string.IsNullOrWhiteSpace(required)) return true;
        if (!TryParseVersion(required, out var req)) return true;
        if (!TryParseVersion(installed, out var have)) return true;

        var length = Math.Max(have.Length, req.Length);
        for (var i = 0; i < length; i++)
        {
            var a = i < have.Length ? have[i] : 0;
            var b = i < req.Length ? req[i] : 0;
            if (a != b) return a > b;
        }

        return true; // equal — the release that introduced the engine does have it.
    }

    /// <summary>Parses a dot-separated numeric tag. Any non-numeric segment fails the whole parse.</summary>
    private static bool TryParseVersion(string? version, out int[] segments)
    {
        segments = [];
        if (string.IsNullOrWhiteSpace(version)) return false;

        var parts = version.Trim().Split('.');
        var parsed = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None,
                              System.Globalization.CultureInfo.InvariantCulture, out parsed[i]))
                return false;
        }

        segments = parsed;
        return true;
    }
}
