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
    /// Player-race tokens (<see cref="Echokraut.Enums.NpcRaces"/> values 1..8). Catalog
    /// filenames collapse these eight to <c>All</c> so a single random-voice file covers all
    /// player-race NPCs. Non-player-race NPCs (beast tribes, Sylph, Goblin, …) keep their
    /// specific race token because their voices are distinctive enough that mixing them into
    /// a generic pool would produce wrong-feeling assignments.
    /// </summary>
    private static readonly HashSet<string> PlayerRaceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hyur", "Elezen", "Miqote", "Roegadyn", "Lalafell", "Viera", "AuRa", "Hrothgar",
    };

    /// <summary>
    /// True if the (normalized) race token names one of the eight player races. Input is
    /// expected to already be in the canonical form returned by <see cref="NormalizeRace"/>
    /// (e.g. <c>"AuRa"</c>, not <c>"Au Ra"</c>); we still uppercase-fold for safety so
    /// callers can pass the raw sheet value if convenient.
    /// </summary>
    public static bool IsPlayerRace(string raceToken) =>
        !string.IsNullOrEmpty(raceToken) && PlayerRaceTokens.Contains(NormalizeRace(raceToken));

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
    /// Translate a body-type token into the filename suffix that goes after the race segment.
    /// <c>Adult</c> (and any unrecognized value) → empty: Adult is the implicit default and
    /// has no token. <c>Child</c>/<c>Elder</c> → <c>"-Child"</c>/<c>"-Elder"</c>, matching
    /// the convention parsed in <c>EchokrautVoice.VoiceName</c> getter and
    /// <c>NpcDataService.ReSetVoiceRaces</c> (split by <c>_</c> first, then by <c>-</c>).
    /// </summary>
    private static string BodyTypeSuffix(string bodyType)
    {
        if (string.Equals(bodyType, "Child", StringComparison.OrdinalIgnoreCase)) return "-Child";
        if (string.Equals(bodyType, "Elder", StringComparison.OrdinalIgnoreCase)) return "-Elder";
        return string.Empty;
    }

    /// <summary>
    /// Build "Gender_Race[-BodyType]_Name". The race is collapsed to the enum-style form (no
    /// spaces / apostrophes / hyphens) so AllTalk catalog filenames stay token-stable; the
    /// name keeps its display form (only filesystem-illegal chars are replaced). Body type
    /// is appended to the race segment with a <c>-</c> separator (e.g.
    /// <c>"Female_Hyur-Child_Tataru"</c>); Adult NPCs get no body suffix.
    /// </summary>
    public static string CanonicalNamePart(string gender, string race, string bodyType, string localizedName)
    {
        var g = Sanitize(string.IsNullOrEmpty(gender) ? "None" : gender);
        var r = Sanitize(NormalizeRace(string.IsNullOrEmpty(race) ? "Unknown" : race));
        var n = Sanitize(localizedName);
        return $"{g}_{r}{BodyTypeSuffix(bodyType)}_{n}";
    }

    /// <summary>
    /// Resolve the destination path for a single sample given the run's slider count.
    /// <list type="bullet">
    /// <item><c>N == 1</c>: <c>&lt;outputSubfolder&gt;/Gender_Race[-BodyType]_Name.wav</c></item>
    /// <item><c>N &gt; 1</c>: <c>&lt;outputSubfolder&gt;/&lt;Name&gt;/Gender_Race[-BodyType]_Name_&lt;index&gt;.wav</c>
    ///   (1-indexed)</item>
    /// </list>
    /// <c>bodyType</c> is one of <c>"Adult"</c>/<c>"Child"</c>/<c>"Elder"</c>; Adult
    /// produces no suffix. <c>outputSubfolder</c> defaults to <c>"FF14-Voices"</c> for the
    /// regular Game-Data-Tools run; the First-Time install flow overrides it to <c>"voices"</c>
    /// so files land directly inside AllTalk's expected voices folder.
    /// <para><c>epochName</c> (from <c>VoiceActorSplits</c>) tags a voice-actor epoch onto the
    /// canonical name when non-empty: <c>Female_Hyur_Iceheart_Pre06010.wav</c>. Empty keeps the
    /// current single-voice filename unchanged. The per-character subfolder (multi-sample case)
    /// stays keyed on the name alone so both epochs group under the same character folder.</para>
    /// </summary>
    public static string GetNamedTargetPath(string root, string gender, string race, string bodyType,
        string localizedName, int sampleIndex, int totalSamplesPerNpc, string outputSubfolder = "FF14-Voices",
        string epochName = "")
    {
        var canonical = CanonicalNamePart(gender, race, bodyType, localizedName);
        if (!string.IsNullOrEmpty(epochName))
            canonical = $"{canonical}_{Sanitize(epochName)}";
        if (totalSamplesPerNpc <= 1)
            return Path.Combine(root, outputSubfolder, canonical + ".wav");

        var subfolder = Sanitize(localizedName);
        return Path.Combine(root, outputSubfolder, subfolder, $"{canonical}_{sampleIndex}.wav");
    }

    /// <summary>
    /// Random-voice NPC catalog path. IDs zero-pad to 3 digits (NPC001…NPC999); auto-widens
    /// to 4 digits at 1000+. Race + body type drive the filename:
    /// <list type="bullet">
    /// <item>Player races (Hyur, Elezen, Miqote, Roegadyn, Lalafell, Viera, AuRa, Hrothgar)
    ///   collapse to <c>All</c> so a single catalog file serves as a generic random voice
    ///   across all eight.</item>
    /// <item>Non-player races (beast tribes, Sylph, Goblin, …) keep their specific race
    ///   token — their voice characteristics are distinctive enough that pooling them under
    ///   <c>All</c> would produce wrong-feeling assignments.</item>
    /// <item>Body type appends <c>-Child</c>/<c>-Elder</c> after the race segment; Adult
    ///   gets no suffix.</item>
    /// </list>
    /// Layout mirrors <see cref="GetNamedTargetPath"/>:
    /// <list type="bullet">
    /// <item><c>totalSamplesPerNpc &lt;= 1</c>:
    ///   <c>&lt;outputSubfolder&gt;/Gender_&lt;Race&gt;[-BodyType]_NPC&lt;ID&gt;.wav</c>
    ///   — flat, single file per NPC alongside the named-voice files.</item>
    /// <item><c>totalSamplesPerNpc &gt; 1</c>:
    ///   <c>&lt;outputSubfolder&gt;/NPC&lt;ID&gt;/Gender_&lt;Race&gt;[-BodyType]_NPC&lt;ID&gt;_&lt;n&gt;.wav</c>
    ///   — per-NPC subfolder named after the catalog ID so AllTalk groups the variants.</item>
    /// </list>
    /// <c>outputSubfolder</c> defaults to <c>"FF14-Voices"</c> for the regular Game-Data-Tools
    /// run; the First-Time install flow overrides it to <c>"voices"</c>.
    /// </summary>
    public static string GetCatalogTargetPath(string root, string gender, string race, string bodyType,
        int globalId, int sampleIndex, int totalSamplesPerNpc, string outputSubfolder = "FF14-Voices")
    {
        var g = Sanitize(string.IsNullOrEmpty(gender) ? "None" : gender);
        var idLen = globalId >= 1000 ? 4 : 3;
        var idStr = globalId.ToString($"D{idLen}");
        // Player races AND Unknown collapse to "All". Unknown is treated as "no race info, fall
        // back to the generic player-race pool" — pinning it as "Unknown" in the filename would
        // produce a dead bucket no voice ever fits, so the pragmatic fix is to merge it with All.
        var normalized = NormalizeRace(string.IsNullOrEmpty(race) ? "Unknown" : race);
        var isUnknown = string.Equals(normalized, "Unknown", StringComparison.OrdinalIgnoreCase);
        var raceToken = (IsPlayerRace(race) || isUnknown)
            ? "All"
            : Sanitize(normalized);
        var bodySuffix = BodyTypeSuffix(bodyType);

        if (totalSamplesPerNpc <= 1)
            return Path.Combine(root, outputSubfolder, $"{g}_{raceToken}{bodySuffix}_NPC{idStr}.wav");

        return Path.Combine(root, outputSubfolder, $"NPC{idStr}",
            $"{g}_{raceToken}{bodySuffix}_NPC{idStr}_{sampleIndex}.wav");
    }
}
