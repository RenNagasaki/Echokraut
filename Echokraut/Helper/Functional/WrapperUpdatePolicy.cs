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
    /// then genuinely behind and the normal handshake must offer them the update.</para>
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
}
