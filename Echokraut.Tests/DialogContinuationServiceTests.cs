using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class DialogContinuationServiceTests
{
    private const string BoxA = "If we hurry, we might reach the camp";
    private const string BoxB = "before nightfall. Then we rest.";

    private readonly Configuration _config = new() { JoinSplitSentences = true };
    private readonly Mock<IGameObjectService> _gameObjects = new();
    private readonly Mock<IDialogSheetSource> _sheets = new();

    public DialogContinuationServiceTests()
    {
        _gameObjects.SetupGet(g => g.LocalPlayerName).Returns("Jon Doe");
        _gameObjects.SetupGet(g => g.LocalPlayerIsMale).Returns(true);
        Sheet(new SheetLine("", BoxA), new SheetLine("", BoxB));
    }

    private void Sheet(params SheetLine[] rows) =>
        _sheets.Setup(s => s.ReadDialogueSheets(ClientLanguage.English))
            .Returns(new List<IReadOnlyList<SheetLine>> { rows });

    private async Task<DialogContinuationService> Ready()
    {
        var service = new DialogContinuationService(_sheets.Object, _gameObjects.Object, _config, new Mock<ILogService>().Object);
        await service.Prepare(ClientLanguage.English);
        return service;
    }

    [Fact]
    public async Task SamplePair_NullUntilBuilt_ThenTheIndexSample()
    {
        var service = new DialogContinuationService(_sheets.Object, _gameObjects.Object, _config, new Mock<ILogService>().Object);
        Assert.Null(service.SamplePair(ClientLanguage.English));

        await service.Prepare(ClientLanguage.English);

        Assert.Equal(new ContinuationPair(BoxA, BoxB, "before nightfall."), service.SamplePair(ClientLanguage.English));
    }

    [Fact]
    public async Task JoinableBoxCount_NullUntilBuilt_ThenTheIndexSize()
    {
        var service = new DialogContinuationService(_sheets.Object, _gameObjects.Object, _config, new Mock<ILogService>().Object);
        Assert.Null(service.JoinableBoxCount(ClientLanguage.English));

        await service.Prepare(ClientLanguage.English);

        Assert.Equal(1, service.JoinableBoxCount(ClientLanguage.English));
    }

    [Fact]
    public async Task OpenBox_GetsTheRest_AndTheNextBoxLosesIt()
    {
        var service = await Ready();

        var a = service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);
        var b = service.Apply(BoxB, TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal("If we hurry, we might reach the camp before nightfall.", a.Text);
        Assert.Equal("before nightfall.", a.AppendedHead);
        Assert.Equal("Then we rest.", b.Text);
        Assert.True(b.StrippedHead);
    }

    [Fact]
    public async Task NextBoxIsSomethingElse_StaysWhole()
    {
        var service = await Ready();

        service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);
        var other = service.Apply("A completely different box.", TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal("A completely different box.", other.Text);
        Assert.False(other.Changed);
    }

    [Fact]
    public async Task OnlyOneBoxIsStripped()
    {
        var service = await Ready();

        service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);
        service.Apply("Unrelated box.", TextSource.AddonTalk, ClientLanguage.English);
        var later = service.Apply(BoxB, TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal(BoxB, later.Text);
    }

    [Fact]
    public async Task Disabled_PassesThroughAndForgetsThePendingStrip()
    {
        var service = await Ready();
        service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);
        _config.JoinSplitSentences = false;

        var b = service.Apply(BoxB, TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal(BoxB, b.Text);
        Assert.False(b.Changed);
    }

    [Fact]
    public async Task OtherSources_PassThrough()
    {
        var service = await Ready();

        var r = service.Apply(BoxA, TextSource.AddonBubble, ClientLanguage.English);

        Assert.Equal(BoxA, r.Text);
        Assert.False(r.Changed);
    }

    [Fact]
    public void IndexNotBuiltYet_PassesThrough()
    {
        var service = new DialogContinuationService(_sheets.Object, _gameObjects.Object, _config, new Mock<ILogService>().Object);
        // Block the background build so the first box surely arrives before it.
        var gate = new TaskCompletionSource();
        _sheets.Setup(s => s.ReadDialogueSheets(ClientLanguage.German)).Returns(() => { gate.Task.Wait(); return new List<IReadOnlyList<SheetLine>>(); });

        var r = service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.German);
        gate.SetResult();

        Assert.Equal(BoxA, r.Text);
    }

    [Fact]
    public async Task PlayerNamePlaceholders_MatchAndAreSubstituted()
    {
        Sheet(new SheetLine("", "Listen well, " + TalkTextHelper.PlaceholderFirstName + ", for I must tell you"),
              new SheetLine("", "about " + TalkTextHelper.PlaceholderFullName + "'s fate. More."));
        var service = await Ready();

        var a = service.Apply("Listen well, Jon, for I must tell you", TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal("Listen well, Jon, for I must tell you about Jon Doe's fate.", a.Text);
    }

    [Fact]
    public async Task PlayerNameUnknown_DoesNotAppendAPlaceholder()
    {
        Sheet(new SheetLine("", BoxA), new SheetLine("", "with " + TalkTextHelper.PlaceholderFullName + ". Go."));
        _gameObjects.SetupGet(g => g.LocalPlayerName).Returns("");
        var service = await Ready();

        var a = service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal(BoxA, a.Text);
        Assert.Null(a.AppendedHead);
    }

    [Fact]
    public async Task WholeNextBoxIsTheRest_BecomesEmpty()
    {
        Sheet(new SheetLine("", BoxA), new SheetLine("", "before nightfall."));
        var service = await Ready();

        service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);
        var b = service.Apply("before nightfall.", TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal("", b.Text);
        Assert.True(b.StrippedHead);
    }

    [Fact]
    public async Task SheetSourceThrows_PassesThrough()
    {
        _sheets.Setup(s => s.ReadDialogueSheets(ClientLanguage.English)).Throws(new System.InvalidOperationException("boom"));
        var service = await Ready();

        Assert.Equal(BoxA, service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English).Text);
    }

    private const string GrahaRow =
        "Wenn jemand Informationen zum Weltuntergang hat, dann jene, die ihr ganzes Leben dem Sammeln von Wissen widmen. Und für meinen Geschmack hat der ehrenwerte Fourchenault unser Hilfsgesuch etwas zu heftig abgeschmettert. Genauer betrachtet wirkt die Reaktion Sharlayans schlicht überzogen. Es blieb der Eindruck, man habe etwas zu verbergen.";

    private const string GrahaPage1 =
        "Wenn jemand Informationen zum Weltuntergang hat, dann jene, die ihr ganzes Leben dem Sammeln von Wissen widmen. Und für meinen Geschmack hat der ehrenwerte Fourchenault unser Hilfsgesuch etwas zu heftig abgeschmettert. Genauer betrachtet wirkt die Reaktion Sharlayans schlicht";

    [Fact]
    public async Task RowSplitByTheGame_FirstPageGetsTheSentenceEnd_SecondPageLosesIt()
    {
        Sheet(new SheetLine("TEXT_AKTKMA101_04357_GRAHATIA_000_019", GrahaRow));
        var service = await Ready();

        var a = service.Apply(GrahaPage1, TextSource.AddonTalk, ClientLanguage.English);
        var b = service.Apply("überzogen. Es blieb der Eindruck, man habe etwas zu verbergen.", TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal(GrahaPage1 + " überzogen.", a.Text);
        Assert.Equal("überzogen.", a.AppendedHead);
        Assert.Equal("Es blieb der Eindruck, man habe etwas zu verbergen.", b.Text);
        Assert.True(b.StrippedHead);
        // Both boxes are pages of the row: reported to Echolines as the whole row, flagged paged.
        Assert.Equal(GrahaRow, a.PagedRow);
        Assert.Equal(GrahaRow, b.PagedRow);
    }

    [Fact]
    public async Task RowSplitByTheGame_LivePunctuationNormalised_StillJoinsThePause()
    {
        const string row = "Als wir vorsichtig durch die zerstörten Gebäude schlichen, sahen wir im Herzen der Hauptstadt Heerscharen von Garlearn, die stumm am Umbau des Kaiserpalastes arbeiteten. Welchem Zweck diese Arbeiten dienten, vermochten wir nicht zu sagen, aber der Anblick war ... verstörend.";
        const string page1 = "Als wir vorsichtig durch die zerstörten Gebäude schlichen, sahen wir im Herzen der Hauptstadt Heerscharen von Garlearn, die stumm am Umbau des Kaiserpalastes arbeiteten. Welchem Zweck diese Arbeiten dienten, vermochten wir nicht zu sagen, aber der Anblick";
        Sheet(new SheetLine("", row));
        var service = await Ready();

        var a = service.Apply(page1, TextSource.AddonTalk, ClientLanguage.English);
        // AddonTalkHelper turns "..." into "." before the continuation sees the box.
        var b = service.Apply("war . verstörend.", TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal("war ... verstörend.", a.AppendedHead);
        Assert.Equal("", b.Text);
        Assert.True(b.StrippedHead);
    }

    [Fact]
    public async Task RowOverThreePages_SecondPageIsFoundThroughTheFirst()
    {
        var p1 = "This is the first part of a very long line that the game cannot fit into a single box, so it goes on";
        var p2 = "into a second box. And then the speaker keeps talking for quite a while longer, until the box is full and";
        var p3 = "the third box ends the sentence. Done.";
        Sheet(new SheetLine("", p1 + " " + p2 + " " + p3));
        var service = await Ready();

        var a = service.Apply(p1, TextSource.AddonTalk, ClientLanguage.English);
        var b = service.Apply(p2, TextSource.AddonTalk, ClientLanguage.English);
        var c = service.Apply(p3, TextSource.AddonTalk, ClientLanguage.English);

        Assert.Equal("into a second box.", a.AppendedHead);
        Assert.Equal("And then the speaker keeps talking for quite a while longer, until the box is full and the third box ends the sentence.", b.Text);
        Assert.Equal("the third box ends the sentence.", b.AppendedHead);
        Assert.Equal("Done.", c.Text);
        Assert.Equal(p1 + " " + p2 + " " + p3, c.PagedRow);
    }

    [Fact]
    public async Task RowSplitAtASentenceEnd_NothingAppended_NextPageStillFollowed()
    {
        var p1 = "This is the first part of a very long line that the game cannot fit into a single box, because the speaker has a lot to say today, and it ends here.";
        var p2 = "The second box starts a new sentence and stops in the middle of";
        var p3 = "it. Then it is over.";
        Sheet(new SheetLine("", p1 + " " + p2 + " " + p3));
        var service = await Ready();

        var a = service.Apply(p1, TextSource.AddonTalk, ClientLanguage.English);
        var b = service.Apply(p2, TextSource.AddonTalk, ClientLanguage.English);
        var c = service.Apply(p3, TextSource.AddonTalk, ClientLanguage.English);

        Assert.False(a.Changed);
        // Nothing appended, but the box is still a page of the row.
        Assert.Equal(p1 + " " + p2 + " " + p3, a.PagedRow);
        Assert.Equal("it.", b.AppendedHead);
        Assert.Equal("Then it is over.", c.Text);
    }

    [Fact]
    public async Task BoxThatIsTheWholeRow_IsNoPage()
    {
        Sheet(new SheetLine("", GrahaRow));
        var service = await Ready();

        Assert.Null(service.Apply(GrahaRow, TextSource.AddonTalk, ClientLanguage.English).PagedRow);
    }

    [Fact]
    public async Task RowPairJoin_IsNoPage()
    {
        // BoxA/BoxB are two rows (a row ends open, the next ends the sentence) — no row was split.
        var service = await Ready();

        var a = service.Apply(BoxA, TextSource.AddonTalk, ClientLanguage.English);

        Assert.NotNull(a.AppendedHead);
        Assert.Null(a.PagedRow);
    }
}
