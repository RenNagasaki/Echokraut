using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>File-system questions the root guard asks; a fake in tests.</summary>
public interface IE2eRootFileSystem
{
    bool DirectoryExists(string path);

    /// <summary>Names (not paths) of the files and folders directly inside <paramref name="path"/>.</summary>
    IReadOnlyList<string> EntryNames(string path);

    /// <summary>The path with every junction/symlink along it resolved; the input when nothing is linked.</summary>
    string ResolveLinks(string path);
}

/// <summary>
/// Hard stop in <c>preconditions</c> (plan › Root guard): the E2E install root must be a plain folder
/// on <c>E:</c> that can never be, contain or sit inside one of the user's real paths, because
/// later stages empty it (D4).
/// </summary>
public static class E2eRootGuard
{
    public const string MarkerFileName = "EkE2E.marker";
    public const string RequiredDrive = "E:";

    /// <summary>Null when <paramref name="root"/> is safe, else the reason (English, for the report).</summary>
    /// <param name="validateInstallPath">The install-path rules of the UI (default
    /// <c>NativeAlltalkBuilder.ValidateInstallPath</c>); tests pass a stub because those rules follow
    /// Windows <see cref="Path"/> semantics.</param>
    public static string? Check(string root, IEnumerable<string?> protectedPaths, IE2eRootFileSystem fs,
        Func<string, (bool isValid, string message)>? validateInstallPath = null)
    {
        if (string.IsNullOrWhiteSpace(root)) return "installRoot is empty.";
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal))
            return "installRoot is a UNC or device path.";
        var norm = Normalize(root);
        if (!norm.StartsWith(RequiredDrive + @"\", StringComparison.OrdinalIgnoreCase) || norm.Length <= 3)
            return $"installRoot must be a folder on drive {RequiredDrive} (not the drive root).";
        if (norm.Split('\\').Any(s => s is "." or ".."))
            return "installRoot must not contain '.' or '..' segments.";

        var (valid, message) = (validateInstallPath ?? Windows.Native.NativeAlltalkBuilder.ValidateInstallPath)(root);
        if (!valid) return $"installRoot is not a valid install path: {message.Replace("\r\n", " ")}";

        var resolvedRoot = Normalize(fs.ResolveLinks(root));
        foreach (var p in protectedPaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var other = Normalize(fs.ResolveLinks(p));
            if (Overlaps(resolvedRoot, other) || Overlaps(norm, Normalize(p)))
                return $"installRoot overlaps a path of the real configuration ({p}).";
        }

        if (fs.DirectoryExists(root))
        {
            var entries = fs.EntryNames(root);
            if (entries.Count > 0 && !entries.Contains(MarkerFileName, StringComparer.OrdinalIgnoreCase))
                return $"installRoot exists and has no {MarkerFileName} — it is not an E2E folder.";
        }
        return null;
    }

    /// <summary>Equal, or one inside the other (case-insensitive, separator-aware).</summary>
    internal static bool Overlaps(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b + @"\", StringComparison.OrdinalIgnoreCase)
        || b.StartsWith(a + @"\", StringComparison.OrdinalIgnoreCase);

    internal static string Normalize(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');
}

/// <summary>The real file system for <see cref="E2eRootGuard"/>.</summary>
public sealed class E2eRootFileSystem : IE2eRootFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EntryNames(string path) =>
        Directory.EnumerateFileSystemEntries(path).Select(e => Path.GetFileName(e)).ToList();

    public string ResolveLinks(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            var current = root;
            foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                var info = new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget != null)
                    current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
            }
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}
