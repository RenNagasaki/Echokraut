using System;
using System.Text.Json;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Reads the two things an update needs out of a GitHub <c>releases/latest</c> response: the release
/// tag and the download URL of its zip asset.
///
/// <para>Both must come from the SAME release. Taking only the tag from the API and keeping the URL
/// from <c>RemoteUrls.json</c> would install the old zip under the new version's name — the version
/// bookkeeping would then claim an update that never happened.</para>
///
/// <para>Pure so the awkward half (asset selection, missing fields, malformed payloads) is testable
/// without touching the network; the HTTP call lives in <c>EchokrauTtsInstanceService</c>.</para>
/// </summary>
public static class GitHubReleaseParser
{
    /// <summary>Parsed result of a <c>releases/latest</c> lookup.</summary>
    public sealed record Release(string Tag, string DownloadUrl);

    /// <summary>
    /// Extracts tag + zip asset URL. Returns false (and a reason) when the payload is unusable —
    /// malformed JSON, no tag, or a release that ships no zip. A release without a usable asset is
    /// deliberately a failure and not "no update": we could not install it even if we wanted to, and
    /// reporting "up to date" there would hide a broken release from the user.
    /// </summary>
    public static bool TryParseLatestRelease(string? json, out Release? release, out string failureReason)
    {
        release = null;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            failureReason = "empty response";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failureReason = "unexpected response shape";
                return false;
            }

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                failureReason = "release has no tag";
                return false;
            }

            var url = FindZipAssetUrl(root);
            if (string.IsNullOrWhiteSpace(url))
            {
                failureReason = $"release {tag} has no downloadable zip";
                return false;
            }

            release = new Release(tag!.Trim(), url!);
            return true;
        }
        catch (JsonException ex)
        {
            failureReason = $"malformed response ({ex.Message})";
            return false;
        }
    }

    /// <summary>
    /// Picks the zip to download. A release can carry several assets (checksums, source archives,
    /// per-platform builds), so: first zip whose name mentions EchokrauTTS, otherwise the first zip
    /// at all. GitHub's auto-generated source archives are not listed under <c>assets</c>, so they
    /// cannot be picked up by accident.
    /// </summary>
    private static string? FindZipAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? firstZip = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
            if (!name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            if (name.Contains("echokrautts", StringComparison.OrdinalIgnoreCase))
                return url;
            firstZip ??= url;
        }

        return firstZip;
    }
}
