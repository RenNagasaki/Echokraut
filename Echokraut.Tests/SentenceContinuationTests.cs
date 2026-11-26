using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

public class SentenceContinuationTests
{
    [Theory]
    [InlineData("We should leave now.", SentenceEnding.Complete)]
    [InlineData("Are you sure?", SentenceEnding.Complete)]
    [InlineData("\"Get out!\"", SentenceEnding.Complete)]
    [InlineData("行きましょう。", SentenceEnding.Complete)]
    [InlineData("If we hurry, we might reach the camp", SentenceEnding.Open)]
    [InlineData("Listen to me,", SentenceEnding.Open)]
    [InlineData("There is one thing:", SentenceEnding.Open)]
    [InlineData("私は、", SentenceEnding.Open)]
    [InlineData("I thought...", SentenceEnding.Ellipsis)]
    [InlineData("I thought…", SentenceEnding.Ellipsis)]
    [InlineData("", SentenceEnding.Complete)]
    public void EndingOf_ClassifiesTheBoxEnd(string text, SentenceEnding expected) =>
        Assert.Equal(expected, SentenceContinuation.EndingOf(text));

    [Fact]
    public void SplitHead_TakesTextUpToTheFirstSentenceEnd()
    {
        var head = SentenceContinuation.SplitHead("before nightfall. Then we rest.");

        Assert.Equal("before nightfall.", head.Head);
        Assert.Equal("Then we rest.", head.Rest);
        Assert.True(head.Terminated);
        Assert.False(head.LeadingEllipsis);
    }

    [Fact]
    public void SplitHead_DropsALeadingEllipsis()
    {
        var head = SentenceContinuation.SplitHead("...that you knew all along!? Unbelievable.");

        Assert.Equal("that you knew all along!?", head.Head);
        Assert.True(head.LeadingEllipsis);
    }

    [Fact]
    public void SplitHead_IgnoresADotInsideANumber()
    {
        Assert.Equal("costs 3.5 gil.", SentenceContinuation.SplitHead("costs 3.5 gil. Cheap!").Head);
    }

    [Fact]
    public void SplitHead_KeepsAClosingQuoteWithTheHead()
    {
        Assert.Equal("the end.\"", SentenceContinuation.SplitHead("the end.\" She left.").Head);
    }

    [Fact]
    public void SplitHead_NoTerminator_IsNotTerminated()
    {
        var head = SentenceContinuation.SplitHead("and still going");

        Assert.False(head.Terminated);
        Assert.Equal("and still going", head.Head);
    }

    [Fact]
    public void SplitHead_Japanese_NeedsNoSpace()
    {
        Assert.Equal("行きましょう。", SentenceContinuation.SplitHead("行きましょう。次へ。").Head);
    }

    [Theory]
    [InlineData(SentenceEnding.Open, "Then we rest.", true)]
    [InlineData(SentenceEnding.Ellipsis, "...that you knew.", true)]
    [InlineData(SentenceEnding.Ellipsis, "that you knew.", true)]
    [InlineData(SentenceEnding.Ellipsis, "Never mind.", false)]
    [InlineData(SentenceEnding.Complete, "and more.", false)]
    public void Continues_DecidesByEndingAndNextBox(SentenceEnding ending, string next, bool expected) =>
        Assert.Equal(expected, SentenceContinuation.Continues(ending, SentenceContinuation.SplitHead(next)));

    [Theory]
    [InlineData("If we hurry,", "we make it.", "If we hurry, we make it.")]
    [InlineData("I thought...", "that you knew.", "I thought that you knew.")]
    [InlineData("私は、", "行きます。", "私は、行きます。")]
    public void Join_ConnectsTheBoxes(string current, string head, string expected) =>
        Assert.Equal(expected, SentenceContinuation.Join(current, head));

    [Fact]
    public void StripHead_RemovesTheMatchingStartIgnoringPunctuationAndCase()
    {
        var normalized = SentenceContinuation.Normalize("that you knew.");

        Assert.Equal("Unbelievable.", SentenceContinuation.StripHead("...That you knew! Unbelievable.", normalized));
    }

    [Fact]
    public void StripHead_WholeBox_ReturnsEmpty()
    {
        Assert.Equal("", SentenceContinuation.StripHead("before nightfall.", SentenceContinuation.Normalize("before nightfall.")));
    }

    [Fact]
    public void StripHead_KeepsAnOpeningQuoteOfTheRest()
    {
        Assert.Equal("\"Run!\"", SentenceContinuation.StripHead("he said. \"Run!\"", SentenceContinuation.Normalize("he said.")));
    }

    [Theory]
    [InlineData("Something else entirely.")]
    [InlineData("before nightfalls come.")] // word goes on past the head
    public void StripHead_DifferentStart_ReturnsNull(string text) =>
        Assert.Null(SentenceContinuation.StripHead(text, SentenceContinuation.Normalize("before nightfall")));

    [Theory]
    [InlineData("war ... verstörend. Dann gingen wir.", "war ... verstörend.")]
    [InlineData("it was… odd. We left.", "it was… odd.")]
    [InlineData("it was... Odd. We left.", "it was...")]
    public void SplitHead_EllipsisBeforeALowerCaseWord_IsAPauseNotTheEnd(string text, string head) =>
        Assert.Equal(head, SentenceContinuation.SplitHead(text).Head);

    [Theory]
    [InlineData("Ab, c-d.", 0, 0)]
    [InlineData("Ab, c-d.", 2, 2)]
    [InlineData("Ab, c-d.", 3, 5)]
    [InlineData("Ab, c-d.", 4, 7)]
    [InlineData("Ab, c-d.", 5, -1)]
    public void PositionAfter_MapsNormalizedCountsBackOntoTheText(string text, int normalized, int expected) =>
        Assert.Equal(expected, SentenceContinuation.PositionAfter(text, normalized));

    [Fact]
    public void EndInRow_MatchesOnlyThatPartOfTheRow()
    {
        var row = SentenceContinuation.Normalize("One two. Three four five.");

        Assert.Equal(6, SentenceContinuation.EndInRow(row, 0, "One, two!"));
        Assert.Equal(15, SentenceContinuation.EndInRow(row, 6, "three four"));
        Assert.Null(SentenceContinuation.EndInRow(row, 6, "four five"));
        Assert.Null(SentenceContinuation.EndInRow(row, 15, "five and more"));
    }

    private const string UriangerRow = "Welchem Zweck diese Arbeiten dienten, vermochten wir nicht zu sagen, aber der Anblick war ... verstörend.";

    [Fact]
    public void ContinueInRow_OpenBox_GetsTheRestOfTheSentenceIncludingAPause()
    {
        var box = "Welchem Zweck diese Arbeiten dienten, vermochten wir nicht zu sagen, aber der Anblick";

        var next = SentenceContinuation.ContinueInRow(UriangerRow, SentenceContinuation.Normalize(box).Length);

        Assert.NotNull(next);
        Assert.Equal("war ... verstörend.", next!.Head);
        Assert.False(next.MidWord);
        Assert.Equal(SentenceContinuation.Normalize(UriangerRow).Length, next.SpokenChars);
    }

    [Fact]
    public void ContinueInRow_BoxEndsASentence_IsNull() =>
        Assert.Null(SentenceContinuation.ContinueInRow("We rest here. Tomorrow we ride.", SentenceContinuation.Normalize("We rest here").Length));

    [Fact]
    public void ContinueInRow_RowEndsThere_IsNull() =>
        Assert.Null(SentenceContinuation.ContinueInRow("We rest here", SentenceContinuation.Normalize("We rest here").Length));

    [Fact]
    public void ContinueInRow_EllipsisBeforeACapital_IsNull() =>
        Assert.Null(SentenceContinuation.ContinueInRow("Sorry, friends ... I have news. Good news.", SentenceContinuation.Normalize("Sorry, friends").Length));

    [Fact]
    public void ContinueInRow_EllipsisBeforeLowerCase_GoesOn()
    {
        var next = SentenceContinuation.ContinueInRow("I thought ... that you knew. Never mind.", SentenceContinuation.Normalize("I thought").Length);

        Assert.Equal("that you knew.", next!.Head);
    }

    [Fact]
    public void ContinueInRow_BreakInsideAWord_JoinsWithoutSpace()
    {
        var row = "Sie sagt, dass alle Vorbereitungen abgeschlossen seien! Wir brechen auf.";

        var next = SentenceContinuation.ContinueInRow(row, SentenceContinuation.Normalize("Sie sagt, dass alle Vor").Length);

        Assert.True(next!.MidWord);
        Assert.Equal("bereitungen abgeschlossen seien!", next.Head);
        Assert.Equal("Sie sagt, dass alle Vorbereitungen abgeschlossen seien!", SentenceContinuation.Join("Sie sagt, dass alle Vor-", next.Head, midWord: true));
    }

    [Fact]
    public void TailKey_ShortBox_IsNull() =>
        Assert.Null(SentenceContinuation.TailKey("Yes,"));

    [Fact]
    public void TailKey_IsTheNormalizedEnd()
    {
        var key = SentenceContinuation.TailKey(new string('a', 100) + " The END,");

        Assert.Equal(SentenceContinuation.TailChars, key!.Length);
        Assert.EndsWith("theend", key);
    }
}

public class SentenceContinuationIndexTests
{
    private const string OpenBox = "If we hurry, we might reach the camp";

    private static SentenceContinuationIndex Build(params SheetLine[][] sheets) => SentenceContinuationIndex.Build(sheets);

    [Fact]
    public void OpenBox_MapsToTheHeadOfTheNextRow()
    {
        var index = Build([new SheetLine("TEXT_Q_00001_THANCRED_000_10", OpenBox), new SheetLine("TEXT_Q_00001_THANCRED_000_11", "before nightfall. Then we rest.")]);

        Assert.Equal("before nightfall.", index.HeadFor(OpenBox));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void LiveTextWithOtherPunctuationAndSpacing_StillMatches()
    {
        var index = Build([new SheetLine("", OpenBox), new SheetLine("", "before nightfall.")]);

        Assert.Equal("before nightfall.", index.HeadFor("If we hurry —  we might reach the CAMP"));
    }

    [Fact]
    public void CompleteBox_IsNotStored()
    {
        var index = Build([new SheetLine("", "We should leave for the camp now."), new SheetLine("", "before nightfall.")]);

        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void DifferentSpeakers_AreNotJoined()
    {
        var index = Build([new SheetLine("TEXT_Q_00001_THANCRED_000_10", OpenBox), new SheetLine("TEXT_Q_00001_YSHTOLA_000_11", "before nightfall.")]);

        Assert.Null(index.HeadFor(OpenBox));
    }

    [Fact]
    public void NextRowWithoutSentenceEnd_IsNotJoined()
    {
        var index = Build([new SheetLine("", OpenBox), new SheetLine("", "before nightfall and then")]);

        Assert.Null(index.HeadFor(OpenBox));
    }

    [Fact]
    public void SameTailWithDifferentContinuations_IsDropped()
    {
        var index = Build(
            [new SheetLine("", OpenBox), new SheetLine("", "before nightfall.")],
            [new SheetLine("", OpenBox), new SheetLine("", "by tomorrow.")]);

        Assert.Null(index.HeadFor(OpenBox));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void SameTailWithTheSameContinuation_IsKept()
    {
        var index = Build(
            [new SheetLine("", OpenBox), new SheetLine("", "before nightfall.")],
            [new SheetLine("", OpenBox), new SheetLine("", "before nightfall. Other text.")]);

        Assert.Equal("before nightfall.", index.HeadFor(OpenBox));
    }

    [Fact]
    public void GenderBranchNearTheEnd_IsSkipped()
    {
        var box = "We trust you, my " + TalkTextHelper.MakeGenderPlaceholder("lord", "lady");
        var index = Build([new SheetLine("", box), new SheetLine("", "with our lives.")]);

        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void PairsNeverCrossSheets()
    {
        var index = Build([new SheetLine("", OpenBox)], [new SheetLine("", "before nightfall.")]);

        Assert.Equal(0, index.Count);
    }

    [Theory]
    [InlineData("TEXT_CLSGLA001_00177_THANCRED_000_10", "TEXT_CLSGLA001_THANCRED")]
    [InlineData("TEXT_VOICEMAN_02000_000010_ALPHINAUD", "TEXT_VOICEMAN_ALPHINAUD")]
    [InlineData("", "")]
    public void SpeakerGroup_DropsNumericSegments(string key, string expected) =>
        Assert.Equal(expected, SentenceContinuationIndex.SpeakerGroup(key));

    [Fact]
    public void Sample_IsTheFirstJoinablePairWithoutPlaceholders()
    {
        var withName = OpenBox + " " + TalkTextHelper.PlaceholderFirstName;
        var index = Build(
            [new SheetLine("", withName), new SheetLine("", "before nightfall. Then we rest.")],
            [new SheetLine("", OpenBox), new SheetLine("", "before nightfall. Then we rest.")]);

        Assert.Equal(new ContinuationPair(OpenBox, "before nightfall. Then we rest.", "before nightfall."), index.Sample);
    }

    [Fact]
    public void Sample_SkipsAPairWhoseSecondBoxIsOnlyTheHead()
    {
        var index = Build([new SheetLine("", OpenBox), new SheetLine("", "before nightfall.")]);
        Assert.Equal(1, index.Count);
        Assert.Null(index.Sample);
    }

    [Fact]
    public void Sample_SkipsATailThatTurnedAmbiguous()
    {
        var index = Build(
            [new SheetLine("", OpenBox), new SheetLine("", "before nightfall. Then we rest.")],
            [new SheetLine("", OpenBox), new SheetLine("", "by tomorrow. Then we rest.")]);
        Assert.Null(index.Sample);
    }

    [Fact]
    public void Sample_SkipsAGenderBranch()
    {
        var gendered = TalkTextHelper.MakeGenderPlaceholder("he", "she") + " said: before nightfall. Then we rest.";
        var index = Build([new SheetLine("", OpenBox), new SheetLine("", gendered)]);
        Assert.Null(index.Sample);
    }

    private const string LongRow =
        "Als wir vorsichtig durch die zerstörten Gebäude schlichen, sahen wir im Herzen der Hauptstadt Heerscharen von Garlearn, die, wie von Geisterhand gesteuert, stumm am Umbau des Kaiserpalastes arbeiteten. Welchem Zweck diese Arbeiten dienten, vermochten wir nicht zu sagen, aber der Anblick war ... verstörend.";

    private const string LongRowFirstPage =
        "Als wir vorsichtig durch die zerstörten Gebäude schlichen, sahen wir im Herzen der Hauptstadt Heerscharen von Garlearn, die, wie von Geisterhand gesteuert, stumm am Umbau des Kaiserpalastes arbeiteten. Welchem Zweck diese Arbeiten dienten, vermochten wir nicht zu sagen, aber der Anblick";

    [Fact]
    public void PagedRowFor_FirstPageOfALongRow_IsThatRow()
    {
        var index = Build([new SheetLine("TEXT_A_URIANGER_000_014", LongRow)]);

        Assert.Equal(LongRow, index.PagedRowFor(LongRowFirstPage));
        Assert.Equal(1, index.PagedRowCount);
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void PagedRowFor_WholeRowOrOtherText_IsNull()
    {
        var index = Build([new SheetLine("", LongRow)]);

        Assert.Null(index.PagedRowFor(LongRow));
        Assert.Null(index.PagedRowFor("Als wir vorsichtig durch die zerstörten Gebäude gingen, war alles still."));
        Assert.Null(index.PagedRowFor("Als wir"));
    }

    [Fact]
    public void PagedRowFor_ShortRows_AreNotStored()
    {
        var index = Build([new SheetLine("", OpenBox + " before nightfall. Then we rest.")]);

        Assert.Equal(0, index.PagedRowCount);
    }

    [Fact]
    public void PagedRowFor_SameRowInTwoSheets_IsStoredOnce()
    {
        var index = Build([new SheetLine("", LongRow)], [new SheetLine("", LongRow)]);

        Assert.Equal(1, index.PagedRowCount);
        Assert.Equal(LongRow, index.PagedRowFor(LongRowFirstPage));
    }

    [Fact]
    public void PagedRowFor_TwoRowsGoingOnDifferently_IsNull()
    {
        var other = LongRowFirstPage + " der Mauern war beeindruckend.";
        var index = Build([new SheetLine("", LongRow), new SheetLine("", other)]);

        Assert.Null(index.PagedRowFor(LongRowFirstPage));
    }
}
