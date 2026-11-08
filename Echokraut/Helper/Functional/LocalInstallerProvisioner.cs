using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Downloads + extracts the shared <c>EchokrautLocalInstaller</c> under the install root, used by
/// both the AllTalk and EchokrauTTS instance services (DRY). Version-aware: re-downloads when a
/// newer installer tag is expected even if the exe already exists, so existing users get an
/// installer that understands new arg modes (BLK-5) instead of silently reusing a stale one.
/// </summary>
public static class LocalInstallerProvisioner
{
    public const string InstallerFolderName = "EchokrautLocalInstaller";
    public const string InstallerExeName = "EchokrautLocalInstaller.exe";

    /// <summary>
    /// Pure decision: should the installer be (re)downloaded? True when the exe is missing, or an
    /// expected version is set and differs from what's installed. An empty
    /// <paramref name="expectedVersion"/> disables the version check (download only when missing).
    /// </summary>
    public static bool ShouldDownload(bool exeExists, string? installedVersion, string? expectedVersion)
    {
        if (!exeExists) return true;
        if (string.IsNullOrWhiteSpace(expectedVersion)) return false;
        return !string.Equals(installedVersion, expectedVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensure the installer exe exists (and is current) under <paramref name="installRoot"/>.
    /// Returns the exe path. When a download happened, <paramref name="downloadedVersion"/> is set to
    /// <paramref name="expectedVersion"/> so the caller can persist it; otherwise it's null.
    /// </summary>
    public static string Ensure(string installRoot, string installerUrl, string? expectedVersion,
        string? installedVersion, ILogService log, EKEventId eventId, out string? downloadedVersion)
    {
        downloadedVersion = null;
        var installerDir = Path.Join(installRoot, InstallerFolderName);
        var exePath = Path.Join(installerDir, InstallerExeName);

        if (!ShouldDownload(File.Exists(exePath), installedVersion, expectedVersion))
        {
            log.Debug(nameof(Ensure), $"Installer up to date at {exePath}", eventId);
            return exePath;
        }

        log.Info(nameof(Ensure), $"Downloading local installer ({expectedVersion ?? "latest"})", eventId);
        Directory.CreateDirectory(installRoot);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var fileName = Path.GetFileName(new Uri(installerUrl).LocalPath);
        var zipPath = Path.Combine(installRoot, fileName);
        var bytes = http.GetByteArrayAsync(installerUrl).GetAwaiter().GetResult();
        File.WriteAllBytes(zipPath, bytes);
        Directory.CreateDirectory(installerDir);
        ZipFile.ExtractToDirectory(zipPath, installerDir, overwriteFiles: true);
        log.Info(nameof(Ensure), $"Installer extracted ({bytes.Length} bytes) to {installerDir}", eventId);

        downloadedVersion = expectedVersion;
        return exePath;
    }
}
