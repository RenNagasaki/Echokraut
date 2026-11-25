using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Phase B of the E2E stages <c>backup</c> and <c>reset</c> (plan <c>docs/plans/ingame-e2e.md</c> ›
/// Stages 1-2). Runs in the <c>Plugin</c> ctor between <c>Loc.Init</c> and <c>GetPluginConfig</c>, i.e.
/// before anything reads the config or opens the DB — and before DI exists, hence static and logging
/// through plain callbacks. It acts only on a dev load, only when <c>state.json</c> is in
/// <see cref="E2ePhase.ResetPending"/>/<see cref="E2ePhase.BackupDone"/>/<see cref="E2ePhase.WipeStarted"/>,
/// was asked for by this very run (<see cref="ResetRequestedMark"/> = state id) and is not stale.
/// <list type="number">
/// <item>reset-pending: copy <c>Echokraut.json</c> + <c>Echokraut\</c> (without <c>selftest\</c>) to
///   <c>Echokraut-e2e\backup-&lt;UTC&gt;\</c>, compare SHA-256 of every copy with its source, write
///   <c>manifest.json</c> and <c>restore.ps1</c> → backup-done. Any problem: no wipe, phase back to
///   requested, <see cref="BackupErrorMark"/> set.</item>
/// <item>backup-done / wipe-started: wipe-started (+ <see cref="E2eState.ConfigIsE2e"/>), delete the
///   config file and everything in the config folder except <c>selftest\</c>, check the config file is
///   gone → reset-done. A failed wipe stays at wipe-started and resumes on the next load (never a second
///   backup).</item>
/// </list>
/// The stages report what happened on the next run-service start (they read the phase and marks).
/// <para>
/// The <c>restore</c> stage uses the same slot in the other direction (<see cref="E2ePhase.RestorePending"/>,
/// <see cref="RestoreRequestedMark"/>): check every backup file against <c>manifest.json</c> first (a mismatch
/// touches nothing), then wipe the E2E config like the reset, copy the backup back, check every copy →
/// restore-done, <see cref="E2eState.ConfigIsE2e"/> false. A failure after the wipe stays at restore-started and
/// resumes on the next load; <c>restore.ps1</c> (game closed) stays the fallback.
/// </para>
/// </summary>
public static class E2eStartupReset
{
    /// <summary>Set by backup phase A to the run id; phase B refuses to act without it.</summary>
    public const string ResetRequestedMark = "reset-requested-id";
    public const string BackupErrorMark = "backup-error";
    public const string ResetErrorMark = "reset-error";
    /// <summary>Number of files in the backup (for the stage detail).</summary>
    public const string BackupFilesMark = "backup-files";
    /// <summary>Set by restore phase A to the run id; the ctor refuses to restore without it.</summary>
    public const string RestoreRequestedMark = "restore-requested-id";
    public const string RestoreErrorMark = "restore-error";
    /// <summary>Number of files put back (for the stage detail).</summary>
    public const string RestoreFilesMark = "restore-files";
    public const string SelfTestFolderName = "selftest";
    public const string ManifestFileName = "manifest.json";
    public const string RestoreScriptName = "restore.ps1";

    private static readonly JsonSerializerOptions ManifestOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>What phase B did; for the tests and one log line.</summary>
    public enum Outcome
    {
        Nothing,
        Refused,
        BackupFailed,
        WipeFailed,
        ResetDone,
        RestoreFailed,
        RestoreDone,
    }

    /// <param name="isDev">Dalamud's <c>IsDev</c>: an installed plugin never touches a file.</param>
    /// <param name="configFile"><c>…\pluginConfigs\Echokraut.json</c>.</param>
    /// <param name="configDirectory"><c>…\pluginConfigs\Echokraut</c>.</param>
    /// <param name="copyFile">Seam for the hash-mismatch test (default <see cref="File.Copy(string,string,bool)"/>).</param>
    public static Outcome RunIfPending(bool isDev, string configFile, string configDirectory,
        Action<string> info, Action<string> warn, Func<DateTime>? utcNow = null,
        Action<string, string>? copyFile = null)
    {
        try
        {
            if (!isDev) return Outcome.Nothing;
            var store = new E2eStateStore(E2eStateStore.DirectoryFor(configDirectory));
            var state = store.Load();
            if (state is not { IsOpen: true }) return Outcome.Nothing;
            var restore = state.Phase is E2ePhase.RestorePending or E2ePhase.RestoreStarted;
            if (!restore && state.Phase is not (E2ePhase.ResetPending or E2ePhase.BackupDone or E2ePhase.WipeStarted))
                return Outcome.Nothing;

            var now = (utcNow ?? (() => DateTime.UtcNow))();
            var remote = state.Request?.RemoteUrl;
            var (mark, what) = restore ? (RestoreRequestedMark, "restore") : (ResetRequestedMark, "reset");
            if (!state.Marks.TryGetValue(mark, out var requestedBy) || requestedBy != state.Id)
            {
                warn(E2eLogFormat.Stage(state.Id, what, "progress", $"state.json asks for a {what} this run did not request - no file touched.", remote));
                return Outcome.Refused;
            }
            if (now - state.UpdatedUtc > E2eRunService.StaleAfter)
            {
                warn(E2eLogFormat.Stage(state.Id, what, "progress", $"state.json last written {state.UpdatedUtc:u}, stale - no file touched.", remote));
                return Outcome.Refused;
            }

            void Save()
            {
                state.UpdatedUtc = now;
                store.Save(state);
            }

            if (restore)
                return Restore(state, Save, configFile, configDirectory, info, warn,
                    copyFile ?? ((s, d) => File.Copy(s, d, overwrite: true)));

            if (state.Phase == E2ePhase.ResetPending)
            {
                try
                {
                    var (dir, files) = Backup(state.Id, configFile, configDirectory, store.Directory, now, copyFile ?? ((s, d) => File.Copy(s, d, overwrite: true)));
                    state.BackupDir = dir;
                    state.Marks[BackupFilesMark] = files.ToString(CultureInfo.InvariantCulture);
                    state.Phase = E2ePhase.BackupDone;
                    Save();
                    info(E2eLogFormat.Stage(state.Id, "backup", "progress", $"phase B: {files} files backed up to {dir}, hashes match, restore.ps1 written", remote));
                }
                catch (Exception ex)
                {
                    state.Marks[BackupErrorMark] = $"{ex.GetType().Name}: {ex.Message}";
                    state.Marks.Remove(ResetRequestedMark);
                    state.Phase = E2ePhase.Requested;
                    Save();
                    warn(E2eLogFormat.Stage(state.Id, "backup", "progress", $"phase B: backup failed, nothing wiped: {ex.Message}", remote));
                    return Outcome.BackupFailed;
                }
            }

            state.Phase = E2ePhase.WipeStarted;
            state.ConfigIsE2e = true;
            Save();
            try
            {
                Wipe(configFile, configDirectory);
                if (File.Exists(configFile)) throw new IOException($"{Path.GetFileName(configFile)} is still there after the wipe.");
                state.Marks.Remove(ResetErrorMark);
                state.Phase = E2ePhase.ResetDone;
                Save();
                info(E2eLogFormat.Stage(state.Id, "reset", "progress", "phase B: config file and config folder wiped (selftest kept)", remote));
                return Outcome.ResetDone;
            }
            catch (Exception ex)
            {
                state.Marks[ResetErrorMark] = $"{ex.GetType().Name}: {ex.Message}";
                Save(); // stays wipe-started: the next load resumes the wipe
                warn(E2eLogFormat.Stage(state.Id, "reset", "progress", $"phase B: wipe failed, resumes on the next load: {ex.Message}", remote));
                return Outcome.WipeFailed;
            }
        }
        catch (Exception ex)
        {
            // Never break the plugin ctor; the stages report the unchanged phase.
            warn($"{E2eLogFormat.Prefix} - reset failed before touching a file: {ex.GetType().Name}: {ex.Message}");
            return Outcome.Refused;
        }
    }

    private static Outcome Restore(E2eState state, Action save, string configFile, string configDirectory,
        Action<string> info, Action<string> warn, Action<string, string> copyFile)
    {
        var remote = state.Request?.RemoteUrl;
        List<(string Source, string Target, string Sha256)> files;
        try
        {
            files = VerifiedBackup(state.BackupDir, configDirectory);
        }
        catch (Exception ex)
        {
            // Nothing touched yet: the plugin loads the E2E config again and the stage reports the error.
            state.Marks[RestoreErrorMark] = $"{ex.GetType().Name}: {ex.Message}";
            state.Marks.Remove(RestoreRequestedMark);
            state.Phase = E2ePhase.Stages;
            save();
            warn(E2eLogFormat.Stage(state.Id, "restore", "progress", $"backup check failed, nothing touched: {ex.Message}", remote));
            return Outcome.RestoreFailed;
        }

        state.Phase = E2ePhase.RestoreStarted;
        save();
        try
        {
            Wipe(configFile, configDirectory);
            foreach (var (source, target, hash) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                copyFile(source, target);
                if (Sha256(target) != hash)
                    throw new IOException($"SHA-256 of the restored {Path.GetFileName(target)} differs from the backup.");
            }
            state.ConfigIsE2e = false;
            state.Marks.Remove(RestoreErrorMark);
            state.Marks[RestoreFilesMark] = files.Count.ToString(CultureInfo.InvariantCulture);
            state.Phase = E2ePhase.RestoreDone;
            save();
            info(E2eLogFormat.Stage(state.Id, "restore", "progress", $"{files.Count} files restored from {state.BackupDir}, hashes match", remote));
            return Outcome.RestoreDone;
        }
        catch (Exception ex)
        {
            state.Marks[RestoreErrorMark] = $"{ex.GetType().Name}: {ex.Message}";
            save(); // stays restore-started: the next load resumes; restore.ps1 is the fallback
            warn(E2eLogFormat.Stage(state.Id, "restore", "progress", $"restore failed, resumes on the next load: {ex.Message}", remote));
            return Outcome.RestoreFailed;
        }
    }

    /// <summary>
    /// Every manifest entry of <paramref name="backupDir"/> with its target under <c>pluginConfigs\</c>, each backup
    /// file checked against its hash. Throws on a missing manifest, a missing or changed file, or a path that
    /// leaves <c>pluginConfigs\</c>.
    /// </summary>
    internal static List<(string Source, string Target, string Sha256)> VerifiedBackup(string? backupDir, string configDirectory)
    {
        if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
            throw new DirectoryNotFoundException($"Backup folder {backupDir ?? "(none)"} not found.");
        var manifest = ReadManifest(backupDir) ?? throw new FileNotFoundException($"{ManifestFileName} missing in {backupDir}.");
        var root = Path.GetFullPath(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(configDirectory))!);
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        var files = new List<(string, string, string)>();
        foreach (var entry in manifest.Files)
        {
            var target = Path.GetFullPath(Path.Combine(root, entry.Path));
            if (Path.IsPathRooted(entry.Path) || !target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Manifest path {entry.Path} leaves the plugin config folder.");
            var source = Path.Combine(backupDir, entry.Path);
            if (!File.Exists(source)) throw new FileNotFoundException($"Backup file {entry.Path} is missing.");
            if (Sha256(source) != entry.Sha256) throw new IOException($"Backup file {entry.Path} does not match its SHA-256.");
            files.Add((source, target, entry.Sha256));
        }
        return files;
    }

    /// <summary>The files phase B backs up: the config file and the config folder without <c>selftest\</c>.</summary>
    internal static List<string> SourceFiles(string configFile, string configDirectory)
    {
        var files = new List<string>();
        if (File.Exists(configFile)) files.Add(configFile);
        if (!Directory.Exists(configDirectory)) return files;
        foreach (var entry in Directory.EnumerateFileSystemEntries(configDirectory))
        {
            if (IsSelfTestFolder(entry)) continue;
            if (Directory.Exists(entry))
                files.AddRange(Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories));
            else
                files.Add(entry);
        }
        return files;
    }

    private static (string Dir, int Files) Backup(string runId, string configFile, string configDirectory,
        string e2eDirectory, DateTime now, Action<string, string> copyFile)
    {
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(configDirectory))!;
        var dir = Path.Combine(e2eDirectory, $"backup-{now:yyyyMMdd'T'HHmmss'Z'}");
        if (Directory.Exists(dir)) dir += "-" + Guid.NewGuid().ToString("N")[..6];
        Directory.CreateDirectory(dir);

        var entries = new List<ManifestEntry>();
        foreach (var src in SourceFiles(configFile, configDirectory))
        {
            var rel = Path.GetRelativePath(root, src);
            var dst = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            var srcHash = Sha256(src);
            copyFile(src, dst);
            var dstHash = Sha256(dst);
            if (dstHash != srcHash)
                throw new IOException($"SHA-256 of the copy of {rel} differs from the original.");
            entries.Add(new ManifestEntry(rel, srcHash, new FileInfo(dst).Length));
        }

        var manifest = new Manifest(runId, now, entries);
        File.WriteAllText(Path.Combine(dir, ManifestFileName), JsonSerializer.Serialize(manifest, ManifestOpts));
        // Written before the wipe [BLK-4]: whatever happens next, the user can put the real config back.
        File.WriteAllText(Path.Combine(dir, RestoreScriptName), E2eRestoreScript.Build(runId), Encoding.ASCII);
        return (dir, entries.Count);
    }

    private static void Wipe(string configFile, string configDirectory)
    {
        if (File.Exists(configFile)) File.Delete(configFile);
        if (!Directory.Exists(configDirectory)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(configDirectory).ToList())
        {
            if (IsSelfTestFolder(entry)) continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
    }

    private static bool IsSelfTestFolder(string entry) =>
        Directory.Exists(entry) && string.Equals(Path.GetFileName(entry), SelfTestFolderName, StringComparison.OrdinalIgnoreCase);

    internal static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary><c>manifest.json</c> next to the backup; <c>restore.ps1</c> reads it.</summary>
    public sealed record Manifest(string Id, DateTime CreatedUtc, IReadOnlyList<ManifestEntry> Files);

    /// <summary>One backed-up file: path relative to <c>pluginConfigs\</c>, upper-case hex SHA-256, size.</summary>
    public sealed record ManifestEntry(string Path, string Sha256, long Size);

    internal static Manifest? ReadManifest(string backupDir)
    {
        var path = Path.Combine(backupDir, ManifestFileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), ManifestOpts) : null;
    }
}
