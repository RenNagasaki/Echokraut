using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Builds the on-disk file/folder layout for the voice starter set.
/// </summary>
public static class VoiceExtractFileNames
{
    /// <summary>
    /// Filesystem-illegal characters that can appear in localized NPC names.
    /// Replaced with <c>_</c> before use. Kept stable and ASCII-only so tests are
    /// deterministic across platforms.
    /// </summary>
    private static readonly char[] IllegalChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    /// <summary>Replace illegal FS chars with <c>_</c>; collapse runs of spaces.</summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(IllegalChars.Contains(ch) || char.IsControl(ch) ? '_' : ch);
        var trimmed = sb.ToString().Trim();
        // Avoid trailing dot (Windows hates trailing-dot directory names).
        return trimmed.TrimEnd('.', ' ');
    }

    /// <summary>Build "Gender_Race_Name" with each part sanitized.</summary>
    public static string CanonicalNamePart(string gender, string race, string localizedName)
    {
        var g = Sanitize(string.IsNullOrEmpty(gender) ? "None" : gender);
        var r = Sanitize(string.IsNullOrEmpty(race) ? "Unknown" : race);
        var n = Sanitize(localizedName);
        return $"{g}_{r}_{n}";
    }

    /// <summary>
    /// Resolve the destination path for a single sample given the run's slider count.
    /// <list type="bullet">
    /// <item><c>N == 1</c>: <c>FF14-Voices/Gender_Race_Name.wav</c></item>
    /// <item><c>N &gt; 1</c>: <c>FF14-Voices/&lt;Name&gt;/Gender_Race_Name_&lt;index&gt;.wav</c>
    ///   (1-indexed)</item>
    /// </list>
    /// </summary>
    public static string GetNamedTargetPath(string root, string gender, string race,
        string localizedName, int sampleIndex, int totalSamplesPerNpc)
    {
        var canonical = CanonicalNamePart(gender, race, localizedName);
        if (totalSamplesPerNpc <= 1)
            return Path.Combine(root, "FF14-Voices", canonical + ".wav");

        var subfolder = Sanitize(localizedName);
        return Path.Combine(root, "FF14-Voices", subfolder, $"{canonical}_{sampleIndex}.wav");
    }

    /// <summary>
    /// Random-voice NPC catalog path: <c>FF14-Voices/NPC/Gender_All_NPC&lt;ID&gt;_&lt;n&gt;.wav</c>.
    /// IDs zero-pad to 3 digits (NPC001…NPC999); auto-widens to 4 digits at 1000+.
    /// </summary>
    public static string GetCatalogTargetPath(string root, string gender, int globalId, int sampleIndex)
    {
        var g = Sanitize(string.IsNullOrEmpty(gender) ? "None" : gender);
        var idLen = globalId >= 1000 ? 4 : 3;
        var idStr = globalId.ToString($"D{idLen}");
        return Path.Combine(root, "FF14-Voices", "NPC", $"{g}_All_NPC{idStr}_{sampleIndex}.wav");
    }
}
