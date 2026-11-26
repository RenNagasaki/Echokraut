using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;

namespace Echokraut.Services;

/// <summary>
/// Walks the batch's matching report (<c>game_voice_unmatched_&lt;lang&gt;.json</c>, plan §11.5) one
/// token at a time for the "Assign game voices" section of the Game Data Tools: assign to an NPC,
/// mark as "no game voice", skip. Every decision lands in the LOCAL alias file at once (effective
/// immediately, no re-ranking: the index is per token) — the repo export collects them for a PR.
/// UI-free so it is testable; the window only shows <see cref="Current"/> and calls the actions.
/// </summary>
public sealed class GameVoiceAssignmentSession
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IGameVoiceMappingService _mapping;
    private readonly List<GameVoiceUnmatchedToken> _entries;
    private readonly Func<DateTime> _utcNow;

    public GameVoiceAssignmentSession(IGameVoiceMappingService mapping, GameVoiceUnmatchedReport report, Func<DateTime>? utcNow = null)
    {
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        ArgumentNullException.ThrowIfNull(report);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        // Tokens the mapping covers since the report was written (assigned earlier, or arrived
        // with the central file) are done — the batch report is a snapshot.
        var covered = new HashSet<string>(_mapping.Aliases.Select(a => VoiceExtractKey.Normalize(a.ShortName)), StringComparer.Ordinal);
        _entries = report.Unmatched.Concat(report.Ambiguous)
            .Where(e => !covered.Contains(VoiceExtractKey.Normalize(e.Token)))
            .ToList();
    }

    /// <summary>Reads a report file; null when missing or unreadable.</summary>
    public static GameVoiceUnmatchedReport? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<GameVoiceUnmatchedReport>(File.ReadAllText(path), ReadOpts) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public int Count => _entries.Count;

    /// <summary>0-based position; == <see cref="Count"/> when every entry was handled.</summary>
    public int Index { get; private set; }

    public GameVoiceUnmatchedToken? Current => Index < _entries.Count ? _entries[Index] : null;

    public int Assigned { get; private set; }
    public int Disabled { get; private set; }

    /// <summary>The best suggestion of the current entry, or empty — what the name field starts with.</summary>
    public string SuggestedName => Current?.Suggestions.FirstOrDefault()?.NpcName ?? "";

    /// <summary>Maps the current token to <paramref name="englishNpcName"/> (the English full name, as the alias file wants it).</summary>
    public bool Assign(string englishNpcName)
    {
        var entry = Current;
        var name = (englishNpcName ?? "").Trim();
        if (entry == null || name.Length == 0) return false;
        _mapping.SetLocalAlias(new GameVoiceAliasEntry
        {
            ShortName = entry.Token,
            NpcName = name,
            Comment = $"assigned {_utcNow():yyyy-MM-dd}, {entry.Clips} clip(s)",
        });
        Assigned++;
        Index++;
        return true;
    }

    /// <summary>"No game voice" for the current token (<c>Disabled</c> entry).</summary>
    public bool Disable()
    {
        var entry = Current;
        if (entry == null) return false;
        _mapping.SetLocalAlias(new GameVoiceAliasEntry
        {
            ShortName = entry.Token,
            Disabled = true,
            Comment = $"no game voice, {_utcNow():yyyy-MM-dd}, {entry.Clips} clip(s)",
        });
        Disabled++;
        Index++;
        return true;
    }

    public void Skip()
    {
        if (Index < _entries.Count) Index++;
    }

    public void Back()
    {
        if (Index > 0) Index--;
    }
}
