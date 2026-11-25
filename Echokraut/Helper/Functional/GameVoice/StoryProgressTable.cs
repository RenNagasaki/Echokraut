using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>One row of the <c>ScenarioTree</c> sheet as the data source hands it over.</summary>
/// <param name="QuestId">Row id = Quest row id (65536+).</param>
/// <param name="Name">Column <c>Name</c>, e.g. <c>T_VER610_01_01</c>.</param>
/// <param name="SortKey">The sheet's running story order (<c>Unknown0</c>).</param>
public readonly record struct ScenarioTreeRow(uint QuestId, string Name, uint SortKey);

/// <summary>Start of one story patch: its first MSQ quest and the quest that unlocks it.</summary>
/// <param name="Key">Patch key, e.g. <c>T_VER610</c> (6.1) or <c>T_VER555</c> (5.55).</param>
/// <param name="GateQuest"><c>Quest.PreviousQuest[0]</c> of <paramref name="FirstQuest"/>; 0 = none.</param>
public sealed record StoryPatch(string Key, uint SortKey, uint FirstQuest, uint GateQuest);

/// <summary>
/// Story order of the voiced patches (plan game-voice-reference §11.4, research
/// <c>docs/research/quest-progress-and-clip-patch.md</c>). Built at runtime from <c>ScenarioTree</c>
/// so a new patch needs no plugin release. Pure: the game queries come in as delegates.
/// <para>Clip tokens (<c>vo_voiceman_XXYZZ_…</c>, the third segment) map onto these keys:
/// <c>XX</c> expansion, <c>Y</c> minor patch, <c>ZZ</c> part — <c>06100</c> = 6.1,
/// <c>03004</c> = 3.0 part 4, <c>06505</c> = 6.55. <c>06010</c> is NOT 6.1.</para>
/// </summary>
public sealed class StoryProgressTable
{
    private static readonly Regex KeyPattern = new(@"^T_VER(\d{3})_", RegexOptions.Compiled);

    /// <summary>
    /// 2.55 / 3.56 / 4.56 have no key of their own in <c>ScenarioTree</c> (merged into
    /// <c>T_VER250/350/450</c>); their first quests come from the research table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, uint> MergedPatchFirstQuests = new Dictionary<string, uint>
    {
        ["T_VER255"] = 65906,
        ["T_VER356"] = 67892,
        ["T_VER456"] = 68720,
    };

    /// <summary>Hops <see cref="KeyForQuest"/> follows along <c>PreviousQuest[0]</c> before giving up.</summary>
    internal const int MaxPreviousHops = 64;

    private readonly List<StoryPatch> _patches;
    private readonly Dictionary<string, int> _indexByKey;
    private readonly Dictionary<uint, string> _keyByQuest;

    private StoryProgressTable(List<StoryPatch> patches, Dictionary<uint, string>? keyByQuest = null)
    {
        _patches = patches;
        _keyByQuest = keyByQuest ?? new Dictionary<uint, string>();
        _indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < patches.Count; i++) _indexByKey[patches[i].Key] = i;

        // MSQ row ids ascending, each with the latest story patch among ids up to it (KeyByRowId).
        var byId = _keyByQuest.OrderBy(pair => pair.Key).ToList();
        _sortedMsqIds = byId.Select(pair => pair.Key).ToArray();
        _prefixMaxPatch = new int[byId.Count];
        var max = -1;
        for (var i = 0; i < byId.Count; i++)
        {
            max = Math.Max(max, _indexByKey.TryGetValue(byId[i].Value, out var p) ? p : -1);
            _prefixMaxPatch[i] = max;
        }
    }

    private readonly uint[] _sortedMsqIds;
    private readonly int[] _prefixMaxPatch;

    public static readonly StoryProgressTable Empty = new(new List<StoryPatch>());

    public IReadOnlyList<StoryPatch> Patches => _patches;

    /// <param name="rows">All <c>ScenarioTree</c> rows.</param>
    /// <param name="previousQuestOf">Quest row id → <c>PreviousQuest[0]</c> row id (0 = none).</param>
    public static StoryProgressTable Build(IEnumerable<ScenarioTreeRow> rows, Func<uint, uint> previousQuestOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(previousQuestOf);
        var list = rows.ToList();
        var first = new Dictionary<string, ScenarioTreeRow>(StringComparer.Ordinal);
        foreach (var row in list)
        {
            var m = KeyPattern.Match(row.Name ?? "");
            if (!m.Success) continue;
            var key = "T_VER" + m.Groups[1].Value;
            if (!first.TryGetValue(key, out var best) || row.SortKey < best.SortKey) first[key] = row;
        }
        foreach (var (key, questId) in MergedPatchFirstQuests)
        {
            if (first.ContainsKey(key)) continue;
            var row = list.FirstOrDefault(r => r.QuestId == questId);
            if (row.QuestId == questId && questId != 0) first[key] = row;
        }

        var patches = first
            .Select(kv => new StoryPatch(kv.Key, kv.Value.SortKey, kv.Value.QuestId, previousQuestOf(kv.Value.QuestId)))
            .OrderBy(p => p.SortKey)
            .ToList();

        // Every MSQ quest belongs to the latest patch that starts at or before it in story order
        // (so the merged 2.55/3.56/4.56 quests land in their own patch, not in x.5).
        var keyByQuest = new Dictionary<uint, string>();
        foreach (var row in list)
        {
            if (row.QuestId == 0 || !KeyPattern.IsMatch(row.Name ?? "")) continue;
            string? owner = null;
            foreach (var patch in patches)
            {
                if (patch.SortKey > row.SortKey) break;
                owner = patch.Key;
            }
            if (owner != null) keyByQuest[row.QuestId] = owner;
        }
        return new StoryProgressTable(patches, keyByQuest);
    }

    /// <summary>True when <paramref name="questId"/> is a main-scenario quest of a known patch.</summary>
    public bool IsMainScenario(uint questId) => _keyByQuest.ContainsKey(questId);

    /// <summary>Every main-scenario quest of a known patch with that patch.</summary>
    public IReadOnlyDictionary<uint, string> MainScenarioQuests => _keyByQuest;

    /// <summary>
    /// The story patch a quest belongs to: an MSQ quest directly; any other quest the story point it
    /// unlocks at — its <c>PreviousQuest[0]</c> chain to the MSQ quest it follows (a side quest added
    /// in 6.2 but playable after the 6.0 finale counts as 6.0: user call 2026-11-15, the story
    /// position decides). Only a quest that never reaches the MSQ falls back to
    /// <see cref="KeyByRowId"/>. Null when neither knows it.
    /// </summary>
    public string? KeyForQuest(uint questId, Func<uint, uint> previousQuestOf)
    {
        ArgumentNullException.ThrowIfNull(previousQuestOf);
        if (_keyByQuest.TryGetValue(questId, out var own)) return own;

        var seen = new HashSet<uint>();
        for (var q = questId; q != 0 && seen.Count <= MaxPreviousHops && seen.Add(q); q = previousQuestOf(q))
            if (_keyByQuest.TryGetValue(q, out var owner)) return owner;

        // No MSQ quest in its chain: quest row ids are handed out in creation order, so the latest
        // patch among MSQ quests with a lower id is when it came into the game.
        return KeyByRowId(questId);
    }

    /// <summary>Latest story patch among MSQ quests whose row id is at most <paramref name="questId"/>.</summary>
    internal string? KeyByRowId(uint questId)
    {
        var ids = _sortedMsqIds;
        int lo = 0, hi = ids.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (ids[mid] <= questId) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found < 0 || _prefixMaxPatch[found] < 0 ? null : _patches[_prefixMaxPatch[found]].Key;
    }

    /// <summary>
    /// Patch key of a clip token (<c>06100</c> → <c>T_VER610</c>), only if the table knows it.
    /// <c>Y = 5</c> with <c>ZZ ≥ 05</c> is the x.55/x.56 patch when the table has one.
    /// </summary>
    public bool TryKeyForToken(string? token, out string key)
    {
        key = string.Empty;
        if (token == null || token.Length != 5 || !token.All(char.IsAsciiDigit)) return false;
        var major = token[1];
        var minor = token[2];
        var part = int.Parse(token.Substring(3, 2));
        if (minor == '5' && part >= 5)
        {
            foreach (var late in new[] { $"T_VER{major}55", $"T_VER{major}56" })
                if (_indexByKey.ContainsKey(late)) { key = late; return true; }
        }
        var plain = $"T_VER{major}{minor}0";
        if (!_indexByKey.ContainsKey(plain)) return false;
        key = plain;
        return true;
    }

    /// <summary>
    /// Highest patch whose start the player reached; empty string = before the first known patch.
    /// The story is linear, so "P reached" implies every earlier patch: binary search, O(log n)
    /// queries. A patch counts as reached when its gate quest or its first quest is complete.
    /// </summary>
    public string HighestReached(Func<uint, bool> isQuestComplete)
    {
        ArgumentNullException.ThrowIfNull(isQuestComplete);
        int lo = 0, hi = _patches.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var p = _patches[mid];
            var reached = (p.GateQuest != 0 && isQuestComplete(p.GateQuest)) || isQuestComplete(p.FirstQuest);
            if (reached) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found < 0 ? string.Empty : _patches[found].Key;
    }

    /// <summary>
    /// True when <paramref name="boundaryToken"/> (a clip token) lies at or before
    /// <paramref name="reachedKey"/>. A token the table cannot place counts as NOT reached.
    /// </summary>
    public bool IsReached(string boundaryToken, string reachedKey)
    {
        if (!TryKeyForToken(boundaryToken, out var key)) return false;
        if (!_indexByKey.TryGetValue(reachedKey ?? "", out var reachedIndex)) return false;
        return _indexByKey[key] <= reachedIndex;
    }

    /// <summary>Story order of two patch keys (-1 for an unknown key).</summary>
    public int Compare(string a, string b)
    {
        var ia = _indexByKey.TryGetValue(a ?? "", out var x) ? x : -1;
        var ib = _indexByKey.TryGetValue(b ?? "", out var y) ? y : -1;
        return ia.CompareTo(ib);
    }
}
