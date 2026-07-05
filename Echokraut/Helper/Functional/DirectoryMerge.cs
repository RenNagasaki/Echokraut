using System.IO;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Pure file-copy helper for syncing voices between the two TTS engines on an engine switch.
/// Copies every file from the source into the destination, overwriting same-named files but
/// leaving the destination's other files intact (never deletes) — so both engine instances stay
/// usable. Recursive, so it works whether the voice folders are flat or contain subdirectories.
/// </summary>
public static class DirectoryMerge
{
    /// <summary>
    /// Merge-copy <paramref name="srcDir"/> into <paramref name="dstDir"/>. Returns the number of
    /// files copied. No-op (returns 0) when the source is missing/empty. Creates the destination
    /// (and any sub-directories) if absent. When <paramref name="overwrite"/> is false, existing
    /// destination files are kept as-is.
    /// </summary>
    public static int MergeCopy(string srcDir, string dstDir, bool overwrite = true)
    {
        if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir))
            return 0;

        Directory.CreateDirectory(dstDir);
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcDir, file);
            var target = Path.Combine(dstDir, rel);
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            if (!overwrite && File.Exists(target))
                continue;

            File.Copy(file, target, overwrite);
            copied++;
        }
        return copied;
    }
}
