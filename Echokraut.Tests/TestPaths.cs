namespace Echokraut.Tests;

/// <summary>
/// Resolves on-disk repo paths from the test runner's working directory. Walks up
/// from <see cref="AppContext.BaseDirectory"/> until it finds a parent containing
/// the well-known marker. Resilient to SDK changes that toggle TFM-subfolder
/// output (e.g. <c>bin/Debug/</c> vs. <c>bin/Debug/net10.0/</c>) — counting
/// <c>".."</c> segments by hand breaks every time that flips.
/// </summary>
internal static class TestPaths
{
    /// <summary>
    /// Path to <c>Echokraut/Resources/RemoteUrls.json</c> in the source tree.
    /// </summary>
    public static string RemoteUrlsJsonPath { get; } = ResolveRepoFile("Echokraut/Resources/RemoteUrls.json");

    private static string ResolveRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate '{relative}' by walking up from {AppContext.BaseDirectory}");
    }
}
