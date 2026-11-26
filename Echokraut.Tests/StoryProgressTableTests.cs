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

/// <summary>Story progress table (§11.4): ScenarioTree rows → patches, tokens → patches, reached.</summary>
public sealed class StoryProgressTableTests
{
    // A slice of the real ScenarioTree (2026-11-15): T_VER key, first quest, sort key.
    private static readonly ScenarioTreeRow[] Rows =
    {
        new(65575, "T_VER200_01_Gri_01", 10),
        new(65564, "T_VER200_01_Gri_03", 30),
        new(65588, "T_VER240_01_01", 7010),
        new(65899, "T_VER250_01_01", 8010),
        new(65906, "T_VER250_01_09", 8090),     // first quest of 2.55 (merged into T_VER250)
        new(69893, "T_VER600_01_01", 36710),
        new(70062, "T_VER610_01_01", 37900),
        new(70063, "T_VER610_01_02", 37910),
        new(70280, "T_VER650_01_01", 38300),
        new(70287, "T_VER655_01_01", 38370),
        new(70396, "T_VER700_01_01", 38400),
    };

    private static uint Previous(uint q) => q == 70062 ? 70000u : q - 1;

    private static StoryProgressTable Table() => StoryProgressTable.Build(Rows, Previous);

    [Fact]
    public void Build_TakesTheFirstQuestPerPatch_InStoryOrder()
    {
        var t = Table();
        Assert.Equal(new[] { "T_VER200", "T_VER240", "T_VER250", "T_VER255", "T_VER600", "T_VER610", "T_VER650", "T_VER655", "T_VER700" },
            t.Patches.Select(p => p.Key));
        var p610 = t.Patches.Single(p => p.Key == "T_VER610");
        Assert.Equal(70062u, p610.FirstQuest);
        Assert.Equal(70000u, p610.GateQuest);
    }

    [Theory]
    [InlineData("06100", "T_VER610")]   // 6.1 MSQ
    [InlineData("06101", "T_VER610")]   // 6.1 side content
    [InlineData("06006", "T_VER600")]   // 6.0 part 6
    [InlineData("06505", "T_VER655")]   // 6.55
    [InlineData("06500", "T_VER650")]
    [InlineData("02505", "T_VER255")]   // 2.55 from the merged-patch table
    [InlineData("07000", "T_VER700")]
    [InlineData("06010", "T_VER600")]   // the plan's old "6.1" example: it is 6.0 (part 10), the trap the research found
    public void TokenMapsToItsPatch(string token, string key)
    {
        Assert.True(Table().TryKeyForToken(token, out var k));
        Assert.Equal(key, k);
    }

    [Theory]
    [InlineData("08000")] // future patch
    [InlineData("abc")]
    [InlineData(null)]
    public void UnknownTokens_AreNotPlaced(string? token) => Assert.False(Table().TryKeyForToken(token, out _));

    [Fact]
    public void HighestReached_BinarySearch_GateOrFirstQuest()
    {
        var t = Table();
        var done = new HashSet<uint> { 65575, 65588, 65899, 65905, 69892, 69893, 70000 }; // gate of 6.1 done
        Assert.Equal("T_VER610", t.HighestReached(done.Contains));
        Assert.Equal("", t.HighestReached(_ => false));
        Assert.Equal("T_VER700", t.HighestReached(_ => true));
    }

    [Fact]
    public void IsReached_ComparesStoryOrder()
    {
        var t = Table();
        Assert.True(t.IsReached("06100", "T_VER610"));
        Assert.True(t.IsReached("06006", "T_VER610"));
        Assert.False(t.IsReached("06505", "T_VER610"));
        Assert.False(t.IsReached("06100", ""));          // before the first voiced patch
        Assert.False(t.IsReached("08000", "T_VER700"));  // unknown boundary = not reached
        Assert.True(t.Compare("T_VER700", "T_VER610") > 0);
    }

    [Theory]
    [InlineData(70062u, "T_VER610")]  // first quest of 6.1
    [InlineData(70063u, "T_VER610")]  // later 6.1 MSQ quest
    [InlineData(65906u, "T_VER255")]  // merged 2.55 quest lands in 2.55, not 2.5
    [InlineData(69893u, "T_VER600")]
    public void KeyForQuest_MainScenario(uint quest, string key)
    {
        var t = Table();
        Assert.True(t.IsMainScenario(quest));
        Assert.Equal(key, t.KeyForQuest(quest, _ => 0));
    }

    [Fact]
    public void KeyForQuest_SideQuest_FollowsItsPreviousQuests()
    {
        // side quest 69901 → 69900 → 69893 (6.0 MSQ); its id lies before 6.1 too → 6.0, whenever it is played
        var previous = new Dictionary<uint, uint> { [69901] = 69900, [69900] = 69893 };
        var t = Table();
        Assert.False(t.IsMainScenario(69901));
        Assert.Equal("T_VER600", t.KeyForQuest(69901, q => previous.GetValueOrDefault(q)));
        Assert.Equal("T_VER600", t.KeyForQuest(69950, q => q == 69950 ? 69951u : 69950u)); // cycle ends, id decides
    }

    [Fact]
    public void KeyForQuest_SideQuestAddedLater_CountsWhereItUnlocks_IdOnlyWithoutChain()
    {
        // created after the first 6.1 quests (id > 70063) but playable after the 6.0 MSQ quest → 6.0
        var t = Table();
        Assert.Equal("T_VER600", t.KeyForQuest(70100, q => q == 70100 ? 69893u : 0u));
        Assert.Equal("T_VER610", t.KeyForQuest(70100, _ => 0));  // no chain: the id decides
        Assert.Equal("T_VER200", t.KeyForQuest(65570, _ => 0));  // lowest ids: ARR
        Assert.Null(t.KeyForQuest(65000, _ => 0));               // below every MSQ id, no chain
    }
}
