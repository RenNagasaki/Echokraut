using System.Collections.Generic;
using System.Numerics;
using Echokraut.Services;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eUiChecksTests
{
    private static OwnedWindowText T(string text, float y = 0, float nodeWidth = 200, float drawWidth = 50, bool wraps = false) =>
        new(text, nodeWidth, drawWidth, wraps, 0, y);

    [Fact]
    public void StaticLabels_KeepsOnlyKnownLabelsOnce()
    {
        var labels = new HashSet<string> { "Install", "Start" };

        var found = E2eUiChecks.StaticLabels([T("Install"), T("Running: 12 s"), T("Install"), T("Start")], labels);

        Assert.Equal(["Install", "Start"], found);
    }

    [Fact]
    public void LabelDiff_NamesWhatWasLostAndGained()
    {
        var diff = E2eUiChecks.LabelDiff(new SortedSet<string> { "Install", "Start" }, new SortedSet<string> { "Install", "Test" });

        Assert.Equal("lost 'Start'; gained 'Test'", diff);
        Assert.Null(E2eUiChecks.LabelDiff(new SortedSet<string> { "a" }, new SortedSet<string> { "a" }));
    }

    [Fact]
    public void Untranslated_FlagsSkippedAndMissingTranslations_NotSameWordOrData()
    {
        var keys = new HashSet<string> { "General", "Name", "Logs", "Install" };
        var de = new Dictionary<string, string> { ["General"] = "Allgemein", ["Name"] = "Name", ["Install"] = "Installieren" };

        var found = E2eUiChecks.Untranslated(["General", "Name", "Logs", "Installieren", "Tataru Taru"], keys,
            k => de.GetValueOrDefault(k));

        Assert.Equal(["'General' (translation 'Allgemein' not used)", "'Logs' (no translation)"], found);
    }

    private static readonly string[] Engines = ["XTTS (Quality 4/5)", "F5 (Quality 3/5)", "MOSS (light)"];

    [Fact]
    public void DropdownProblem_OptionsInCatalogOrder_IsNull()
    {
        IReadOnlyList<OwnedWindowText> closed = [T("XTTS (Quality 4/5)", y: 100)];
        IReadOnlyList<OwnedWindowText> open = [T("XTTS (Quality 4/5)", y: 100), T("MOSS (light)", y: 172),
            T("XTTS (Quality 4/5)", y: 124), T("F5 (Quality 3/5)", y: 148)];

        Assert.Null(E2eUiChecks.DropdownProblem(closed, open, Engines, "XTTS (Quality 4/5)"));
    }

    [Fact]
    public void DropdownProblem_WrongOrder_CutOffText_AndMissingSelection_AreAllNamed()
    {
        IReadOnlyList<OwnedWindowText> closed = [T("something else", y: 100)];
        IReadOnlyList<OwnedWindowText> open = [T("F5 (Quality 3/5)", y: 124), T("XTTS (Quality 4/5)", y: 148, nodeWidth: 80, drawWidth: 140)];

        var problem = E2eUiChecks.DropdownProblem(closed, open, Engines, "XTTS (Quality 4/5)");

        Assert.NotNull(problem);
        Assert.Contains("does not show the selected 'XTTS (Quality 4/5)'", problem);
        Assert.Contains("options 'F5 (Quality 3/5)' | 'XTTS (Quality 4/5)' != expected 'XTTS (Quality 4/5)' | 'F5 (Quality 3/5)'", problem);
        Assert.Contains("'XTTS (Quality 4/5)' cut off (140 px text in 80 px)", problem);
    }

    [Fact]
    public void DropdownProblem_NothingOpened_IsNamed()
    {
        IReadOnlyList<OwnedWindowText> closed = [T("XTTS (Quality 4/5)")];

        Assert.Equal("no option appeared when opened", E2eUiChecks.DropdownProblem(closed, closed, Engines, "XTTS (Quality 4/5)"));
    }

    [Fact]
    public void DropdownProblem_ShorterScrollingList_ComparesTheFirstEntries()
    {
        IReadOnlyList<OwnedWindowText> closed = [T("F5 (Quality 3/5)", y: 100)];
        IReadOnlyList<OwnedWindowText> open = [T("F5 (Quality 3/5)", y: 100), T("XTTS (Quality 4/5)", y: 124), T("F5 (Quality 3/5)", y: 148)];

        Assert.Null(E2eUiChecks.DropdownProblem(closed, open, Engines, "F5 (Quality 3/5)"));
    }

    [Fact]
    public void WrappedText_NeverOverflows()
    {
        Assert.False(T("long", nodeWidth: 10, drawWidth: 500, wraps: true).Overflows);
        Assert.True(T("long", nodeWidth: 10, drawWidth: 12).Overflows);
        Assert.False(T("fits", nodeWidth: 10, drawWidth: 11).Overflows); // 1 px slack
    }

    private static OwnedWindowState W(float x, float y) =>
        new("w", true, true, new Vector2(x, y), new Vector2(400, 200), new Vector2(1920, 1080), []);

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1720, 980, true)]     // exactly half of each axis on screen
    [InlineData(1721, 0, false)]      // one pixel less than half the width
    [InlineData(-200, -100, true)]
    [InlineData(-320, 0, false)]
    [InlineData(0, 2000, false)]      // entirely below the screen
    public void ClampProblem_NeedsHalfOfEachAxisOnScreen(float x, float y, bool ok)
    {
        Assert.Equal(ok, E2eUiChecks.ClampProblem(W(x, y)) == null);
    }

    [Fact]
    public void VisibleFraction_PerAxis()
    {
        Assert.Equal(new Vector2(0.25f, 1f), W(1820, 0).VisibleFraction);
    }
}
