using System;
using System.IO;

namespace Echokraut.Tests;

/// <summary>
/// Lightweight loader for a repo-root <c>.env</c> file.
/// Walks up from the test binary to find <c>.env</c>, parses simple <c>KEY=VALUE</c> lines,
/// and exports them as process environment variables — but only if not already set
/// (so an explicit shell-level value or CI override always wins).
/// </summary>
internal static class TestEnvLoader
{
    private static readonly object _lock = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;

            var path = Locate(".env");
            if (path == null) return;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();

                // Strip optional surrounding quotes.
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                    value = value.Substring(1, value.Length - 2);

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private static string? Locate(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
