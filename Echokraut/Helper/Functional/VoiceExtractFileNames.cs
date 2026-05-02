using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Maps the English <c>Race.Masculine</c> form Lumina returns onto the canonical
    /// <c>NpcRaces</c> enum-token used everywhere else in Echokraut. <c>"Hyuran"→"Hyur"</c>
    /// in particular doesn't follow the simple strip-separator rule (it's a suffix change),
    /// so an explicit map is required. Mirrors <c>DialogHarvestService.RaceNameMap</c> so
    /// voice resolution can match on the same race tokens the catalog filenames use.
    /// </summary>
    private static readonly Dictionary<string, string> RaceAliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Hyuran", "Hyur" },
        { "Miqo'te", "Miqote" },
        { "Au Ra", "AuRa" },
    };

    /// <summary>
    /// Normalize a race string into the canonical <c>NpcRaces</c> enum-token form.
    /// First tries the explicit alias map (handles <c>"Hyuran"→"Hyur"</c>), then falls back
    /// to stripping spaces / apostrophes / hyphens while preserving casing
    /// (<c>"Hrothgar"</c> stays <c>"Hrothgar"</c>). Empty input → empty output.
    /// </summary>
    public static string NormalizeRace(string race)
    {
        if (string.IsNullOrEmpty(race)) return string.Empty;
        if (RaceAliasMap.TryGetValue(race, out var mapped)) return mapped;
        var sb = new StringBuilder(race.Length);
        foreach (var ch in race)
        {
            if (ch == ' ' || ch == '\'' || ch == '-') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Build "Gender_Race_Name". The race is collapsed to the enum-style form (no spaces /
    /// apostrophes / hyphens) so AllTalk catalog filenames stay token-stable; the name keeps
    /// its display form (only filesystem-illegal chars are replaced).
    /// </summary>
    public static string CanonicalNamePart(string gender, string race, string localizedName)
    {
        var g = Sanitize(string.IsNullOrEmpty(gender) ? "None" : gender);
        var r = Sanitize(NormalizeRace(string.IsNullOrEmpty(race) ? "Unknown" : race));
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
    /// Random-voice NPC catalog path. IDs zero-pad to 3 digits (NPC001…NPC999); auto-widens
    /// to 4 digits at 1000+. Layout mirrors <see cref="GetNamedTargetPath"/>:
    /// <list type="bullet">
    /// <item><c>totalSamplesPerNpc &lt;= 1</c>: <c>FF14-Voices/Gender_All_NPC&lt;ID&gt;.wav</c>
    ///   — flat, single file per NPC alongside the named-voice files.</item>
    /// <item><c>totalSamplesPerNpc &gt; 1</c>:
    ///   <c>FF14-Voices/NPC&lt;ID&gt;/Gender_All_NPC&lt;ID&gt;_&lt;n&gt;.wav</c>
    ///   — per-NPC subfolder named after the catalog ID so AllTalk groups the variants.</item>
    /// </list>
    /// </summary>
    public static string GetCatalogTargetPath(string root, string gender, int globalId, int sampleIndex, int totalSamplesPerNpc)
    {
        var g = Sanitize(string.IsNullOrEmpty(gender) ? "None" : gender);
        var idLen = globalId >= 1000 ? 4 : 3;
        var idStr = globalId.ToString($"D{idLen}");
        if (totalSamplesPerNpc <= 1)
            return Path.Combine(root, "FF14-Voices", $"{g}_All_NPC{idStr}.wav");

        return Path.Combine(root, "FF14-Voices", $"NPC{idStr}",
            $"{g}_All_NPC{idStr}_{sampleIndex}.wav");
    }
}
