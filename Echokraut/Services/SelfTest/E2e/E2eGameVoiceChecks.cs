using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using EchoScd;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Judgements of the game-voice parts (plan › B6), pure so they are unit-tested: which availability an engine's
/// <c>/health</c> implies, the repo export of the alias file, the matching report and the voice-change suggestions
/// of the batch.
/// </summary>
public static class E2eGameVoiceChecks
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Boundaries the voice-change detection must find in the German dub (measured 2026-11-15, research
    /// <c>docs/research/game-voice-actor-splits-de.md</c>): NPC (normalized) and the first patch of the new actor.
    /// </summary>
    public static readonly IReadOnlyList<(string Npc, string Patch)> ExpectedSplitsDe = [("yshtola", "06100"), ("hydaelyn", "06000")];

    /// <summary>NPCs the detection must not split (one actor throughout, the worst false gaps of the measurement).</summary>
    public static readonly IReadOnlyList<string> NoSplitDe = ["alphinaud", "thancred"];

    /// <summary>The availability a correct plugin derives from <c>/health inline_reference</c> (only an explicit true counts).</summary>
    public static GameVoiceAvailability ExpectedAvailability(bool? inlineReference) =>
        inlineReference == true ? GameVoiceAvailability.Available : GameVoiceAvailability.ServerUnsupported;

    /// <summary>
    /// The file "Export for the repo" wrote must be the resource format of <c>Resources/GameVoiceAliases.json</c>:
    /// same top-level fields, version, one entry per token sorted by token, every embedded token still in it, and
    /// <paramref name="token"/> → <paramref name="englishName"/> (the entry the part assigned).
    /// </summary>
    public static (IReadOnlyList<string> Problems, string Detail) JudgeAliasExport(string exportJson, string embeddedJson,
        string token, string englishName)
    {
        var problems = new List<string>();
        GameVoiceAliasFile? export, embedded;
        try
        {
            export = JsonSerializer.Deserialize<GameVoiceAliasFile>(exportJson, ReadOpts);
            embedded = JsonSerializer.Deserialize<GameVoiceAliasFile>(embeddedJson, ReadOpts);
        }
        catch (JsonException ex)
        {
            return (["export is not valid JSON: " + ex.Message], "");
        }
        if (export?.Aliases == null) return (["export has no aliases list"], "");
        var embeddedAliases = embedded?.Aliases ?? new List<GameVoiceAliasEntry>();

        var exportFields = TopLevelFields(exportJson);
        var embeddedFields = TopLevelFields(embeddedJson).Where(f => !f.StartsWith('_')).ToList();
        var missing = embeddedFields.Except(exportFields, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count > 0) problems.Add($"top-level field(s) missing: {string.Join(", ", missing)}");
        if (embedded != null && export.Version != embedded.Version)
            problems.Add($"version {export.Version}, resource has {embedded.Version}");

        var keys = export.Aliases.Select(a => VoiceExtractKey.Normalize(a.ShortName)).ToList();
        var duplicates = keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) problems.Add($"token(s) twice: {string.Join(", ", duplicates.Take(5))}");
        if (!keys.SequenceEqual(keys.OrderBy(k => k, StringComparer.Ordinal), StringComparer.Ordinal))
            problems.Add("entries not sorted by token");
        var lost = embeddedAliases.Select(a => VoiceExtractKey.Normalize(a.ShortName)).Except(keys, StringComparer.Ordinal).ToList();
        if (lost.Count > 0) problems.Add($"resource token(s) missing from the export: {string.Join(", ", lost.Take(5))}");

        var normalized = VoiceExtractKey.Normalize(token);
        var entry = export.Aliases.FirstOrDefault(a => VoiceExtractKey.Normalize(a.ShortName) == normalized);
        if (entry == null) problems.Add($"assigned token {normalized} not in the export");
        else if (entry.Disabled || !string.Equals(entry.NpcName?.Trim(), englishName.Trim(), StringComparison.OrdinalIgnoreCase))
            problems.Add($"{normalized} exported as {(entry.Disabled ? "disabled" : entry.NpcName)}, assigned {englishName}");

        return (problems, $"export {export.Aliases.Count} entries (resource {embeddedAliases.Count}), {normalized} -> {entry?.NpcName}");
    }

    /// <summary><c>harvest/game_voice_unmatched_&lt;lang&gt;.json</c>: readable, the right language, entries complete.</summary>
    public static (IReadOnlyList<string> Problems, string Detail) JudgeUnmatchedReport(string? json, string langCode)
    {
        if (string.IsNullOrWhiteSpace(json)) return (["matching report missing or empty"], "");
        GameVoiceUnmatchedReport? report;
        try
        {
            report = JsonSerializer.Deserialize<GameVoiceUnmatchedReport>(json, ReadOpts);
        }
        catch (JsonException ex)
        {
            return (["matching report is not valid JSON: " + ex.Message], "");
        }
        if (report == null) return (["matching report is empty"], "");
        var problems = new List<string>();
        if (report.Version < 1) problems.Add($"report version {report.Version}");
        if (!string.Equals(report.Language, langCode, StringComparison.OrdinalIgnoreCase))
            problems.Add($"report language {report.Language}, expected {langCode}");
        var entries = report.Unmatched.Concat(report.Ambiguous).ToList();
        var broken = entries.Where(e => string.IsNullOrWhiteSpace(e.Token) || e.Clips <= 0).Select(e => e.Token).ToList();
        if (broken.Count > 0) problems.Add($"entries without token or clips: {broken.Count}");
        var sorted = report.Unmatched.Select(e => e.Clips).ToList();
        if (!sorted.SequenceEqual(sorted.OrderByDescending(c => c))) problems.Add("unmatched not sorted by clip count");
        return (problems, $"report {langCode}: {report.Unmatched.Count} unmatched, {report.Ambiguous.Count} ambiguous, " +
                          $"{report.SeenWithoutToken.Count} seen without token");
    }

    /// <summary>
    /// The batch's voice-change suggestions (split format) against the measured German boundaries: each of
    /// <see cref="ExpectedSplitsDe"/> present, none for <see cref="NoSplitDe"/>.
    /// </summary>
    public static (IReadOnlyList<string> Problems, string Detail) JudgeSplitSuggestions(string? json, string langCode)
    {
        if (string.IsNullOrWhiteSpace(json)) return (["suggestions file missing or empty"], "");
        VoiceActorSplitsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<VoiceActorSplitsFile>(json, ReadOpts);
        }
        catch (JsonException ex)
        {
            return (["suggestions file is not valid JSON: " + ex.Message], "");
        }
        var entries = (file?.Splits ?? new List<VoiceActorSplitEntry>())
            .Where(e => string.Equals(e.Language, langCode, StringComparison.OrdinalIgnoreCase))
            .Select(e => (Npc: VoiceExtractKey.Normalize(e.NpcName), Patches: e.BoundaryPatches ?? new List<string>()))
            .ToList();
        var problems = new List<string>();
        if (!string.Equals(langCode, "DE", StringComparison.OrdinalIgnoreCase))
            return (problems, $"{entries.Count} suggestion(s); expectations exist only for DE");
        foreach (var (npc, patch) in ExpectedSplitsDe)
            if (!entries.Any(e => e.Npc.Contains(npc, StringComparison.Ordinal) && e.Patches.Contains(patch)))
                problems.Add($"no {npc} boundary at {patch} ({Describe(entries, npc)})");
        foreach (var npc in NoSplitDe)
            if (entries.Where(e => e.Npc.Contains(npc, StringComparison.Ordinal)).ToList() is { Count: > 0 } wrong)
                problems.Add($"false boundary for {npc} at {string.Join("/", wrong.SelectMany(e => e.Patches))}");
        return (problems, $"{entries.Count} suggestion(s): " +
                          string.Join(", ", entries.Take(8).Select(e => $"{e.Npc} {string.Join("/", e.Patches)}")));
    }

    private static string Describe(IEnumerable<(string Npc, List<string> Patches)> entries, string npc)
    {
        var hits = entries.Where(e => e.Npc.Contains(npc, StringComparison.Ordinal)).SelectMany(e => e.Patches).ToList();
        return hits.Count == 0 ? "no entry" : "found " + string.Join("/", hits);
    }

    private static List<string> TopLevelFields(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
