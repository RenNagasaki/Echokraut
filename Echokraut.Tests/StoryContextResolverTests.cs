using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Story patch of a line (§11.4): accepted quest, any MSQ quest, accepted MSQ, progress.</summary>
public sealed class StoryContextResolverTests
{
    private static readonly AcceptedQuest Msq700 = new(70396, "T_VER700", true);
    private static readonly AcceptedQuest Side600 = new(90002, "T_VER600", false);
    private static readonly AcceptedQuest Msq600 = new(69893, "T_VER600", true);

    private static readonly Dictionary<uint, IReadOnlySet<string>> Texts = new()
    {
        [70396] = StoryContextResolver.Shingles("The new continent awaits, and Tural is calling us onward to its golden cities"),
        [90002] = StoryContextResolver.Shingles("I have studied the aetherial currents of this land for many years, friend"),
        // sheet text of the 6.0 quest: the player-name macro is gone after ExtractText
        [69893] = StoryContextResolver.Shingles("If they were indeed made thralls, it seems safe to assume that these entities are of a kind with the Ascians, ."),
    };

    private static StoryContext? Resolve(string? line, AcceptedQuest[] accepted, params AcceptedQuest[] mainScenario) =>
        StoryContextResolver.Resolve(line, accepted, q => Texts.TryGetValue(q, out var w) ? w : new HashSet<string>(),
            mainScenario, "T_VER700");

    [Fact]
    public void LineOfAnAcceptedSideQuest_TakesThatQuestsPatch()
    {
        // the resolved macro (", Alisaie") breaks only the triples around it
        var ctx = Resolve("I have studied the aetherial currents of this land for many years, Alisaie.", new[] { Msq700, Side600 });
        Assert.Equal(new StoryContext("T_VER600", StoryContextSource.LineQuest, 90002), ctx);
    }

    [Fact]
    public void LineBeforeAcceptingAnMsqQuest_IsFoundInTheMainScenario()
    {
        // New Game+ chapter start: the quest is not accepted yet (sequence 0), only side quests are
        var ctx = Resolve("If they were indeed made thralls, it seems safe to assume that these entities are of a kind with the Ascians, Ren.",
            new[] { Side600 with { PatchKey = "T_VER700" } }, Msq700, Msq600);
        Assert.Equal(new StoryContext("T_VER600", StoryContextSource.LineQuestNotAccepted, 69893), ctx);
    }

    [Fact]
    public void AcceptedQuest_WinsOverTheMainScenarioSearch() =>
        Assert.Equal(StoryContextSource.LineQuest,
            Resolve("I have studied the aetherial currents of this land for many years", new[] { Side600 }, Msq700, Msq600)!.Source);

    [Fact]
    public void UnmatchedLine_TakesTheAcceptedMainScenarioQuest() =>
        Assert.Equal(new StoryContext("T_VER700", StoryContextSource.AcceptedMainScenario, 70396),
            Resolve("Something nobody wrote in any quest at all", new[] { Side600, Msq700 }, Msq600));

    [Fact]
    public void SharedCommonWords_DoNotMatch() =>
        // every single word occurs in the 6.0 text, but no triple of them does
        Assert.Equal(StoryContextSource.Progress,
            Resolve("the Ascians are safe, it seems, if they made a kind of thralls", Array.Empty<AcceptedQuest>(), Msq600)!.Source);

    [Fact]
    public void NewGamePlus_AcceptedReplayedQuest_IsLowerThanProgress() =>
        Assert.Equal("T_VER600", Resolve(null, new[] { Msq600 })!.PatchKey);

    [Fact]
    public void NoQuest_FallsBackToProgress_OrUnknown()
    {
        Assert.Equal(new StoryContext("T_VER700", StoryContextSource.Progress), Resolve("Hello there my friend", Array.Empty<AcceptedQuest>()));
        Assert.Null(StoryContextResolver.Resolve("x", Array.Empty<AcceptedQuest>(), _ => new HashSet<string>(), Array.Empty<AcceptedQuest>(), null));
    }

    [Fact]
    public void ShortLine_DoesNotGuessAQuest() =>
        Assert.Equal(StoryContextSource.AcceptedMainScenario, Resolve("many years, friend", new[] { Side600, Msq700 })!.Source);

    [Fact]
    public void Shingles_WordTriples_AndJapaneseCharacterTriples()
    {
        Assert.Equal(new[] { "the land of", "land of old" }.ToHashSet(), StoryContextResolver.Shingles("The land, of old!"));
        Assert.Equal(new[] { "こんに", "んにち", "にちは" }.ToHashSet(), StoryContextResolver.Shingles("こんにちは"));
        Assert.Empty(StoryContextResolver.Shingles("two words"));
    }
}
