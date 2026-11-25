using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Echokraut.Services;

/// <inheritdoc cref="IStoryProgressService"/>
/// <remarks>
/// Dalamud adapter around the pure <see cref="StoryProgressTable"/> and
/// <see cref="StoryContextResolver"/> (research <c>docs/research/quest-progress-and-clip-patch.md</c>):
/// <c>ScenarioTree</c> → story patch of every MSQ quest, <c>IUnlockState.IsQuestCompleted</c> →
/// highest completed patch (polled every <see cref="PollInterval"/> and on
/// <see cref="IUnlockState.Unlock"/>), <c>QuestManager.NormalQuests</c> → accepted quests (every
/// <see cref="AcceptedPollInterval"/>). Nothing is stored: a replayed chapter (New Game+) is the
/// accepted quest's patch, so its lines speak with the voice its cutscenes have.
/// </remarks>
public sealed class StoryProgressService : IStoryProgressService
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan AcceptedPollInterval = TimeSpan.FromSeconds(1);

    private readonly IFramework _framework;
    private readonly IUnlockState _unlock;
    private readonly IPlayerState _player;
    private readonly IDataManager _data;
    private readonly ILogService _log;

    private StoryProgressTable? _table;
    private bool _tableFailed;
    private volatile string? _reached;
    private volatile AcceptedQuest[] _accepted = Array.Empty<AcceptedQuest>();
    private readonly ConcurrentDictionary<(uint Quest, ClientLanguage Language), IReadOnlySet<string>> _questShingles = new();
    // Every quest with text per language once its shingles are loaded (Prepare); missing = not ready yet.
    private readonly ConcurrentDictionary<ClientLanguage, AcceptedQuest[]> _allQuests = new();
    private readonly ConcurrentDictionary<ClientLanguage, byte> _preparing = new();
    private readonly object _tableLock = new();
    private DateTime _nextPoll = DateTime.MinValue;
    private DateTime _nextAcceptedPoll = DateTime.MinValue;
    private bool _disposed;

    public event System.Action? ProgressChanged;

    public StoryProgressService(IFramework framework, IUnlockState unlock, IPlayerState player,
        IDataManager data, ILogService log)
    {
        _framework = framework ?? throw new ArgumentNullException(nameof(framework));
        _unlock = unlock ?? throw new ArgumentNullException(nameof(unlock));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _framework.Update += OnUpdate;
        _unlock.Unlock += OnUnlock;
    }

    public string? ReachedPatchKey => _reached;

    public StoryContext? ContextFor(string? lineText, ClientLanguage language) =>
        StoryContextResolver.Resolve(lineText, _accepted, q => QuestShingles(q, language),
            _allQuests.TryGetValue(language, out var msq) ? msq : Array.Empty<AcceptedQuest>(), _reached);

    public void Prepare(ClientLanguage language)
    {
        if (_disposed || _allQuests.ContainsKey(language) || !_preparing.TryAdd(language, 0)) return;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var started = DateTime.UtcNow;
                if (Table() is not { } table) return;
                // Every quest with dialogue text, MSQ first (a tie goes to the main scenario).
                var quests = new List<AcceptedQuest>();
                foreach (var row in _data.GetExcelSheet<Quest>())
                {
                    if (_disposed) return;
                    if (row.RowId < 0x10000 || string.IsNullOrEmpty(row.Id.ExtractText())) continue;
                    if (QuestShingles(row.RowId, language).Count == 0) continue;
                    var patch = table.KeyForQuest(row.RowId, PreviousQuestOf);
                    if (patch != null) quests.Add(new AcceptedQuest(row.RowId, patch, table.IsMainScenario(row.RowId)));
                }
                var ordered = quests.OrderByDescending(q => q.IsMainScenario).ToArray();
                _allQuests[language] = ordered;
                _log.Info(nameof(Prepare), $"Quest texts ({language}): {ordered.Length} quest(s), {ordered.Count(q => q.IsMainScenario)} MSQ, in {(DateTime.UtcNow - started).TotalSeconds:0.0} s.", Event);
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(Prepare), $"Quest texts unreadable: {ex.Message}", Event);
            }
            finally { _preparing.TryRemove(language, out _); }
        });
    }

    public bool IsReached(string boundaryToken, string patchKey) =>
        _table is { } table && table.IsReached(boundaryToken, patchKey);

    private void OnUnlock(RowRef rowRef)
    {
        _nextPoll = DateTime.MinValue;
        _nextAcceptedPoll = DateTime.MinValue;
    }

    private void OnUpdate(IFramework _)
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;
        if (now >= _nextPoll)
        {
            _nextPoll = now + PollInterval;
            try { Poll(); }
            catch (Exception ex)
            {
                _log.Warning(nameof(StoryProgressService), $"Story progress unreadable: {ex.Message}", Event);
                Publish(null);
            }
        }
        if (now >= _nextAcceptedPoll)
        {
            _nextAcceptedPoll = now + AcceptedPollInterval;
            try { PollAccepted(); }
            catch (Exception ex)
            {
                _log.Warning(nameof(StoryProgressService), $"Accepted quests unreadable: {ex.Message}", Event);
                _accepted = Array.Empty<AcceptedQuest>();
            }
        }
    }

    private void Poll()
    {
        if (!_player.IsLoaded || _player.ContentId == 0) { Publish(null); return; }
        var table = Table();
        if (table == null || table.Patches.Count == 0) { Publish(null); return; }

        var quests = _data.GetExcelSheet<Quest>();
        Publish(table.HighestReached(id => quests.GetRowOrDefault(id) is { } q && _unlock.IsQuestCompleted(q)));
    }

    /// <summary>Framework thread: snapshot of the accepted quests with their story patch.</summary>
    private unsafe void PollAccepted()
    {
        var table = Table();
        var manager = QuestManager.Instance();
        if (!_player.IsLoaded || table == null || manager == null)
        {
            _accepted = Array.Empty<AcceptedQuest>();
            return;
        }

        var ids = new List<uint>();
        foreach (var work in manager->NormalQuests)
            if (work.QuestId != 0) ids.Add(work.QuestId + 0x10000u); // ushort id → Quest row id

        var current = _accepted;
        if (current.Length == ids.Count && current.Select(a => a.QuestId).SequenceEqual(ids)) return;
        var accepted = ids.Select(id => new AcceptedQuest(id, table.KeyForQuest(id, PreviousQuestOf), table.IsMainScenario(id))).ToArray();
        _accepted = accepted;
        _log.Info(nameof(StoryProgressService),
            $"Accepted quests: {(accepted.Length == 0 ? "none" : string.Join(", ", accepted.Select(a => $"{a.QuestId} {a.PatchKey ?? "?"}{(a.IsMainScenario ? " MSQ" : "")}")))}", Event);
    }

    private uint PreviousQuestOf(uint questId) =>
        _data.GetExcelSheet<Quest>().GetRowOrDefault(questId) is { } q && q.PreviousQuest.Count > 0 ? q.PreviousQuest[0].RowId : 0u;

    /// <summary>Shingles of a quest's dialogue sheet (<c>quest/&lt;nnn&gt;/&lt;Id&gt;</c>), cached per language.</summary>
    private IReadOnlySet<string> QuestShingles(uint questId, ClientLanguage language) =>
        _questShingles.GetOrAdd((questId, language), key =>
        {
            var words = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (_data.GetExcelSheet<Quest>().GetRowOrDefault(key.Quest) is not { } quest) return words;
                var id = quest.Id.ExtractText();
                if (string.IsNullOrEmpty(id)) return words;
                var sheetPath = $"quest/{(key.Quest - 0x10000u) / 100:D3}/{id}";
                var sheet = _data.GetExcelSheet<RawRow>(key.Language, sheetPath);
                foreach (var row in sheet)
                {
                    try { words.UnionWith(StoryContextResolver.Shingles(row.ReadStringColumn(1).ExtractText())); }
                    catch { /* row without a text column */ }
                }
            }
            catch (Exception ex)
            {
                _log.Debug(nameof(QuestShingles), $"Quest {key.Quest} text unreadable: {ex.Message}", Event);
            }
            return words;
        });

    private StoryProgressTable? Table()
    {
        if (_table != null || _tableFailed) return _table;
        lock (_tableLock) return BuildTable();
    }

    private StoryProgressTable? BuildTable()
    {
        if (_table != null || _tableFailed) return _table;
        try
        {
            var tree = _data.GetExcelSheet<ScenarioTree>();
            var rows = tree.Select(r => new ScenarioTreeRow(r.RowId, r.Name.ExtractText(), r.Unknown0));
            _table = StoryProgressTable.Build(rows, PreviousQuestOf);
            _log.Info(nameof(StoryProgressService), $"Story table: {_table.Patches.Count} patch(es).", Event);
        }
        catch (Exception ex)
        {
            _tableFailed = true;
            _log.Warning(nameof(StoryProgressService), $"ScenarioTree unreadable, story progress unknown: {ex.Message}", Event);
        }
        return _table;
    }

    private void Publish(string? value)
    {
        if (_reached == value) return;
        _reached = value;
        _log.Info(nameof(StoryProgressService), $"Story progress: {(value == null ? "unknown" : value.Length == 0 ? "before the first voiced patch" : value)}", Event);
        ProgressChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _framework.Update -= OnUpdate;
        _unlock.Unlock -= OnUnlock;
    }

    private static readonly EKEventId Event = new(0, TextSource.None);
}
