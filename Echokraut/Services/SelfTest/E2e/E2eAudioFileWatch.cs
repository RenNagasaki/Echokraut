using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Build step B6 (plan › Coverage, R5 "no WAV on disk"): proves a game-voice part wrote no audio file. Before the
/// part every audio file under the watched roots (install root, <c>pluginConfigs</c>, <c>%TEMP%</c>) is listed with
/// size and write time, and a <see cref="FileSystemWatcher"/> per root records every audio file created, changed or
/// renamed while the part runs — that also catches a temp file deleted again before the part ends (f5's
/// <c>NamedTemporaryFile</c> path, which the wrapper avoids). <see cref="Stop"/> lists again and judges both.
/// Reported paths start with the root's label (<c>&lt;install&gt;\…</c>), never with a user folder.
/// </summary>
public sealed class E2eAudioFileWatch : IDisposable
{
    /// <summary>What counts as audio: the plugin's and the wrapper's formats plus the game's container.</summary>
    internal static readonly string[] AudioExtensions = [".wav", ".ogg", ".flac", ".mp3", ".pcm", ".raw", ".scd", ".opus"];

    private readonly IReadOnlyList<(string Label, string Path)> _roots;
    private readonly Dictionary<string, (long Size, DateTime Write)> _before;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _errors = new();

    private E2eAudioFileWatch(IReadOnlyList<(string Label, string Path)> roots, bool watch)
    {
        _roots = roots;
        _before = Snapshot(roots);
        if (!watch) return;
        foreach (var (label, path) in roots)
        {
            try
            {
                var w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                w.Created += (_, e) => Record(e.FullPath);
                w.Changed += (_, e) => Record(e.FullPath);
                w.Renamed += (_, e) => Record(e.FullPath);
                w.Error += (_, e) => _errors.Enqueue($"{label}: {e.GetException().Message}");
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or PlatformNotSupportedException)
            {
                _errors.Enqueue($"{label}: not watched ({ex.Message})");
            }
        }
    }

    /// <summary>
    /// Lists and starts watching. Roots that do not exist are left out; <paramref name="watch"/> false = snapshot only
    /// (unit tests: the watcher's events arrive on their own thread).
    /// </summary>
    public static E2eAudioFileWatch Start(IEnumerable<(string Label, string? Path)> roots, bool watch = true)
    {
        var existing = roots
            .Where(r => !string.IsNullOrWhiteSpace(r.Path) && Directory.Exists(r.Path))
            .Select(r => (r.Label, Path: System.IO.Path.GetFullPath(r.Path!)))
            .ToList();
        return new E2eAudioFileWatch(existing, watch);
    }

    /// <summary>The roots of the E2E run: install root, <c>pluginConfigs</c> (parent of the plugin's config dir), temp.</summary>
    public static IEnumerable<(string Label, string? Path)> RunRoots(string? installRoot, string? pluginConfigDir) =>
    [
        ("<install>", installRoot),
        ("<pluginConfigs>", string.IsNullOrWhiteSpace(pluginConfigDir) ? null : Directory.GetParent(pluginConfigDir.TrimEnd('\\', '/'))?.FullName),
        ("<temp>", System.IO.Path.GetTempPath()),
    ];

    /// <summary>
    /// Runs <paramref name="part"/> inside a watch over <paramref name="roots"/>: an audio file on disk fails it, else
    /// the watch's one-liner is appended to its detail.
    /// </summary>
    public static async System.Threading.Tasks.Task<SelfTestResult> RunAsync(IEnumerable<(string Label, string? Path)> roots,
        Func<System.Threading.Tasks.Task<SelfTestResult>> part)
    {
        var watch = Start(roots);
        SelfTestResult result;
        try
        {
            result = await part().ConfigureAwait(false);
        }
        finally
        {
            watch.Dispose();
        }
        var (problems, files) = watch.Stop();
        if (problems.Count > 0) return SelfTestResult.Fail($"{string.Join("; ", problems)} | {result.Detail}");
        return new SelfTestResult(result.Outcome, $"{result.Detail} | {files}");
    }

    /// <summary>A path the watcher reported (internal for the unit tests).</summary>
    internal void Record(string fullPath)
    {
        if (IsAudio(fullPath)) _seen.TryAdd(fullPath, 0);
    }

    internal static bool IsAudio(string path) =>
        AudioExtensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Ends the watch: the files that appeared or changed, and a one-line detail.</summary>
    public (IReadOnlyList<string> Problems, string Detail) Stop()
    {
        Dispose();
        var after = Snapshot(_roots);
        var touched = Diff(_before, after).Concat(_seen.Keys)
            .Select(Labelled)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var problems = new List<string>();
        if (touched.Count > 0)
            problems.Add($"audio file(s) written during the game-voice part: {string.Join(", ", touched.Take(5))}" +
                         (touched.Count > 5 ? $" (+{touched.Count - 5})" : ""));
        var errors = _errors.ToList();
        // A lost watcher buffer can hide a file: the snapshot still judges what is left on disk.
        var detail = $"no audio file written ({_roots.Count} root(s), {_before.Count} audio file(s) before)" +
                     (errors.Count > 0 ? $"; watcher: {string.Join("; ", errors.Take(2))}" : "");
        return (problems, problems.Count > 0 ? string.Join("; ", problems) : detail);
    }

    /// <summary>Files new in <paramref name="after"/> or with another size or write time.</summary>
    internal static IEnumerable<string> Diff(IReadOnlyDictionary<string, (long Size, DateTime Write)> before,
        IReadOnlyDictionary<string, (long Size, DateTime Write)> after) =>
        after.Where(a => !before.TryGetValue(a.Key, out var b) || b != a.Value).Select(a => a.Key);

    private static Dictionary<string, (long Size, DateTime Write)> Snapshot(IEnumerable<(string Label, string Path)> roots)
    {
        var files = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var (_, root) in roots)
        {
            try
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
                    if (IsAudio(file.Name)) files.TryAdd(file.FullName, (file.Length, file.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A root that vanished mid-walk: what was listed still counts.
            }
        }
        return files;
    }

    /// <summary><c>C:\Users\x\AppData\Local\Temp\a.wav</c> → <c>&lt;temp&gt;\a.wav</c>.</summary>
    private string Labelled(string fullPath)
    {
        foreach (var (label, root) in _roots.OrderByDescending(r => r.Path.Length))
        {
            var prefix = root.TrimEnd('\\', '/');
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return label + fullPath[prefix.Length..];
        }
        return "<other>\\" + System.IO.Path.GetFileName(fullPath);
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
