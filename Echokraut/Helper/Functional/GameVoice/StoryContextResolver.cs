using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Echokraut.Helper.Functional.GameVoice;

/// <summary>Where the story patch of a line came from (plan game-voice-reference §11.4).</summary>
public enum StoryContextSource
{
    /// <summary>The line's text belongs to an accepted quest — the most precise answer.</summary>
    LineQuest,
    /// <summary>The line's text belongs to a quest that is not accepted (yet): the talk before
    /// accepting it, e.g. sequence 0 of a New Game+ chapter quest or a side quest's offer.</summary>
    LineQuestNotAccepted,
    /// <summary>The line matched no quest; the accepted main-scenario quest decides.</summary>
    AcceptedMainScenario,
    /// <summary>No quest context at all: the highest story patch the player completed.</summary>
    Progress,
}

/// <summary>
/// The story patch a line belongs to, which picks the voice-actor epoch: a replayed 6.0 chapter
/// (New Game+) or a late 5.x side quest must speak with the voice its voiced cutscenes have, not
/// with the voice of the player's furthest progress.
/// </summary>
/// <param name="PatchKey"><c>T_VER600</c> …; <c>""</c> = before the first voiced patch.</param>
/// <param name="QuestId">Quest row id the answer came from; 0 for <see cref="StoryContextSource.Progress"/>.</param>
public sealed record StoryContext(string PatchKey, StoryContextSource Source, uint QuestId = 0)
{
    public override string ToString() =>
        $"{(PatchKey.Length == 0 ? "before the first voiced patch" : PatchKey)} ({Source}{(QuestId != 0 ? $" {QuestId}" : "")})";
}

/// <summary>A quest the resolver may attribute a line to.</summary>
/// <param name="PatchKey">Story patch of the quest (<see cref="StoryProgressTable.KeyForQuest"/>), null = unknown.</param>
public readonly record struct AcceptedQuest(uint QuestId, string? PatchKey, bool IsMainScenario);

/// <summary>
/// Pure decision for <see cref="StoryContext"/>, in this order: (1) the accepted quest whose dialogue
/// text contains the line, (2) any quest whose text contains it (talk before accepting; MSQ wins a tie),
/// (3) the accepted main-scenario quest, (4) the completed progress.
/// Texts are compared by word triples ("shingles"), not literally: the sheet text carries macros
/// (player name, gender branches) the spoken line has already resolved, which breaks only the
/// triples around them; triples are specific enough to search every quest without false hits.
/// </summary>
public static class StoryContextResolver
{
    /// <summary>Share of the line's shingles the quest text must contain.</summary>
    public const double MinShingleShare = 0.6;

    /// <summary>Fewer shingles than this say too little to tell quests apart.</summary>
    public const int MinShingles = 2;

    /// <param name="questShingles">Shingles of one quest's dialogue sheet in the client language (cached by the caller).</param>
    /// <param name="mainScenario">Every quest with text and its patch, MSQ first, for lines before accepting; empty = not ready yet.</param>
    /// <param name="reachedKey">Highest completed patch; null = unknown.</param>
    public static StoryContext? Resolve(string? lineText, IReadOnlyList<AcceptedQuest> accepted,
        Func<uint, IReadOnlySet<string>> questShingles, IReadOnlyList<AcceptedQuest> mainScenario, string? reachedKey)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(questShingles);
        ArgumentNullException.ThrowIfNull(mainScenario);

        var line = Shingles(lineText);
        if (line.Count >= MinShingles)
        {
            if (BestMatch(line, accepted, questShingles) is { } hit)
                return new StoryContext(hit.PatchKey!, StoryContextSource.LineQuest, hit.QuestId);
            if (BestMatch(line, mainScenario, questShingles) is { } msq)
                return new StoryContext(msq.PatchKey!, StoryContextSource.LineQuestNotAccepted, msq.QuestId);
        }

        foreach (var quest in accepted)
            if (quest.IsMainScenario && quest.PatchKey != null)
                return new StoryContext(quest.PatchKey, StoryContextSource.AcceptedMainScenario, quest.QuestId);

        return reachedKey == null ? null : new StoryContext(reachedKey, StoryContextSource.Progress);
    }

    private static AcceptedQuest? BestMatch(HashSet<string> line, IReadOnlyList<AcceptedQuest> quests,
        Func<uint, IReadOnlySet<string>> questShingles)
    {
        AcceptedQuest? best = null;
        var bestShare = 0.0;
        foreach (var quest in quests)
        {
            if (quest.PatchKey == null) continue;
            var shingles = questShingles(quest.QuestId);
            if (shingles.Count == 0) continue;
            var share = line.Count(shingles.Contains) / (double)line.Count;
            if (share > bestShare) { bestShare = share; best = quest; }
        }
        return bestShare >= MinShingleShare ? best : null;
    }

    /// <summary>
    /// Comparable shingles of a text: triples of consecutive lower-case words (letters/digits);
    /// text without spaces (Japanese) is cut into character triples instead.
    /// </summary>
    public static HashSet<string> Shingles(string? text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text)) return result;
        var words = new List<string>();
        var run = new StringBuilder();
        foreach (var c in text + " ")
        {
            if (char.IsLetterOrDigit(c)) { run.Append(char.ToLowerInvariant(c)); continue; }
            if (run.Length > 0) words.Add(run.ToString());
            run.Clear();
        }
        foreach (var word in words.Where(w => w.Any(IsCjk)))
            for (var i = 0; i + 2 < word.Length; i++) result.Add(word.Substring(i, 3));
        var latin = words.Where(w => !w.Any(IsCjk)).ToList();
        for (var i = 0; i + 2 < latin.Count; i++) result.Add($"{latin[i]} {latin[i + 1]} {latin[i + 2]}");
        return result;
    }

    private static bool IsCjk(char c) => c >= '　';
}
