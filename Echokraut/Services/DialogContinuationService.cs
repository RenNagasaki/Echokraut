using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

/// <inheritdoc cref="IDialogContinuationService"/>
/// <remarks>
/// Appending is stateless (the index says which boxes end open and how their sentence ends).
/// Stripping is stateful on purpose: the next box only loses its start when the previous box really
/// got it appended. A stateless "strip whenever the previous row was open" would silence that start
/// whenever the previous box could not be matched (macro near its end, too short, ambiguous).
/// Two ways a sentence gets split: the game breaks one long sheet row over several boxes (by far the
/// most common, looked up first via <see cref="SentenceContinuationIndex.PagedRowFor"/> and then
/// followed page by page), or a row ends open and the next row ends the sentence (<see cref="SentenceContinuationIndex.HeadFor"/>).
/// </remarks>
public sealed class DialogContinuationService : IDialogContinuationService
{
    private readonly IDialogSheetSource _sheets;
    private readonly IGameObjectService _gameObjects;
    private readonly Configuration _config;
    private readonly ILogService _log;

    private readonly ConcurrentDictionary<ClientLanguage, SentenceContinuationIndex> _indexes = new();
    private readonly ConcurrentDictionary<ClientLanguage, Task> _building = new();
    private readonly object _pendingLock = new();
    private string? _pendingHead;
    // Row the last box was a page of, and how much of it (normalised) has been spoken: finds the next page.
    private string? _pendingRow;
    private int _pendingRowSpoken;

    public DialogContinuationService(IDialogSheetSource sheets, IGameObjectService gameObjects,
        Configuration config, ILogService log)
    {
        _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        _gameObjects = gameObjects ?? throw new ArgumentNullException(nameof(gameObjects));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task Prepare(ClientLanguage language)
    {
        if (_indexes.ContainsKey(language)) return Task.CompletedTask;
        return _building.GetOrAdd(language, lang => Task.Run(() => Build(lang)));
    }

    private void Build(ClientLanguage language)
    {
        var started = DateTime.UtcNow;
        try
        {
            var index = SentenceContinuationIndex.Build(_sheets.ReadDialogueSheets(language));
            _indexes[language] = index;
            _log.Info(nameof(DialogContinuationService),
                $"Sentence continuations ({language}): {index.Count} box(es) end mid-sentence, {index.PagedRowCount} row(s) long enough to be split over boxes, built in {(DateTime.UtcNow - started).TotalSeconds:0.0} s.", Event);
        }
        catch (Exception ex)
        {
            _indexes[language] = SentenceContinuationIndex.Empty;
            _log.Warning(nameof(DialogContinuationService), $"Dialogue sheets unreadable, sentences are not joined: {ex.Message}", Event);
        }
    }

    public ContinuationResult Apply(string text, TextSource source, ClientLanguage language)
    {
        if (source != TextSource.AddonTalk) return new ContinuationResult(text, null, false);
        try
        {
            return ApplyTalk(text ?? "", language);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(Apply), $"Sentence continuation failed, box spoken as is: {ex.Message}", Event);
            return new ContinuationResult(text ?? "", null, false);
        }
    }

    public int? JoinableBoxCount(ClientLanguage language) =>
        _indexes.TryGetValue(language, out var index) ? index.Count : null;

    public ContinuationPair? SamplePair(ClientLanguage language) =>
        _indexes.TryGetValue(language, out var index) ? index.Sample : null;

    private ContinuationResult ApplyTalk(string text, ClientLanguage language)
    {
        lock (_pendingLock)
        {
            var pending = _pendingHead;
            var pendingRow = _pendingRow;
            var pendingRowSpoken = _pendingRowSpoken;
            _pendingHead = null;
            _pendingRow = null;
            if (!_config.JoinSplitSentences) return new ContinuationResult(text, null, false);

            var stripped = false;
            if (pending != null && SentenceContinuation.StripHead(text, pending) is { } rest)
            {
                text = rest;
                stripped = true;
            }

            if (!_indexes.TryGetValue(language, out var index))
            {
                _ = Prepare(language);
                return new ContinuationResult(text, null, stripped);
            }
            if (text.Length == 0)
            {
                // The box was only the appended head: the row's next page follows where the head ended.
                if (pendingRow != null) Track(pendingRow, pendingRowSpoken);
                return new ContinuationResult(text, null, stripped);
            }

            var playerName = _gameObjects.LocalPlayerName ?? "";
            var tokenized = PlayerNameTokenizer.Tokenize(text, playerName);
            if (FindPage(index, tokenized, pendingRow, pendingRowSpoken) is { } page)
                return ContinuePage(text, page.Row, page.Spoken, playerName, stripped);

            if (index.HeadFor(tokenized) is not { } head)
                return new ContinuationResult(text, null, stripped);
            return Append(text, head, midWord: false, playerName, stripped) ?? new ContinuationResult(text, null, stripped);
        }
    }

    /// <summary>The row this box is a page of: the next page of the row the previous box belonged to, else a row this box starts.</summary>
    private static (string Row, int Spoken)? FindPage(SentenceContinuationIndex index, string tokenized, string? pendingRow, int pendingRowSpoken)
    {
        if (pendingRow != null
            && SentenceContinuation.EndInRow(SentenceContinuation.Normalize(pendingRow), pendingRowSpoken, tokenized) is { } end)
            return (pendingRow, end);
        if (index.PagedRowFor(tokenized) is { } row)
            return (row, SentenceContinuation.Normalize(tokenized).Length);
        return null;
    }

    private ContinuationResult ContinuePage(string text, string row, int spoken, string playerName, bool stripped)
    {
        // FindPage only yields rows longer than the box, so every result from here is a page of `row`.
        var unchanged = new ContinuationResult(text, null, stripped, row);
        if (SentenceContinuation.ContinueInRow(row, spoken) is not { } next)
        {
            // Box ends a sentence mid-row: nothing to append, but the next page is still this row.
            if (spoken < SentenceContinuation.Normalize(row).Length) Track(row, spoken);
            return unchanged;
        }
        var result = Append(text, next.Head, next.MidWord, playerName, stripped);
        Track(row, result == null ? spoken : next.SpokenChars);
        return result is { } joined ? joined with { PagedRow = row } : unchanged;
    }

    private void Track(string row, int spoken)
    {
        _pendingRow = row;
        _pendingRowSpoken = spoken;
    }

    /// <summary>Box text with the head appended, null when the head would carry an unresolved placeholder.</summary>
    private ContinuationResult? Append(string text, string head, bool midWord, string playerName, bool stripped)
    {
        var spoken = TalkTextHelper.SubstitutePlaceholders(head, playerName, _gameObjects.LocalPlayerIsMale);
        // Name not known yet: a placeholder must never reach the backend.
        if (TalkTextHelper.ContainsPlayerPlaceholder(spoken)) return null;

        _pendingHead = SentenceContinuation.Normalize(spoken);
        return new ContinuationResult(SentenceContinuation.Join(text, spoken, midWord), spoken, stripped);
    }

    private static readonly EKEventId Event = new(0, TextSource.None);
}
