using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The "which engine should I pick?" block above the first-time wizard's dropdown.
/// </summary>
public class TtsEngineDescriptionsTests
{
    private static string Identity(string s) => s; // stands in for Loc.S

    private static TtsEngineOption Option(string id, string name, string description = "")
        => new(id, name, name, true, string.Empty, description);

    [Fact]
    public void KnownEngines_HaveABuiltInText()
    {
        Assert.NotNull(TtsEngineDescriptions.EnglishFor("xtts"));
        Assert.NotNull(TtsEngineDescriptions.EnglishFor("f5"));
        Assert.NotNull(TtsEngineDescriptions.EnglishFor("MOSS")); // id casing must not matter
        Assert.Null(TtsEngineDescriptions.EnglishFor("something-new"));
    }

    [Fact]
    public void Compose_OneLinePerEngine_NamedAndDescribed()
    {
        var text = TtsEngineDescriptions.Compose(
            [Option("xtts", "XTTS"), Option("moss", "MOSS")], Identity);

        var lines = text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("XTTS: ", lines[0]);
        Assert.StartsWith("MOSS: ", lines[1]);
    }

    [Fact]
    public void CatalogDescription_WinsOverTheBuiltInOne()
    {
        // That is how a wrapper release describes an engine the plugin predates - and how it can
        // correct a built-in text that has gone stale.
        var text = TtsEngineDescriptions.Compose([Option("xtts", "XTTS", "Straight from the catalog.")], Identity);

        Assert.Equal("XTTS: Straight from the catalog.", text);
    }

    [Fact]
    public void UnknownEngineWithoutADescription_IsSkipped()
    {
        // A bare name with nothing after it reads like a rendering bug, so the line is left out.
        var text = TtsEngineDescriptions.Compose(
            [Option("xtts", "XTTS"), Option("vall-e", "VALL-E")], Identity);

        Assert.Single(text.Split('\n'));
        Assert.DoesNotContain("VALL-E", text);
    }

    [Fact]
    public void Compose_RunsEveryTextThroughLocalization()
    {
        // The English text IS the localization key in this project, so nothing may bypass the lookup.
        var text = TtsEngineDescriptions.Compose([Option("xtts", "XTTS")], _ => "TRANSLATED");

        Assert.Equal("XTTS: TRANSLATED", text);
    }

    [Fact]
    public void Compose_NoEngines_IsEmpty()
    {
        Assert.Equal(string.Empty, TtsEngineDescriptions.Compose(null, Identity));
        Assert.Equal(string.Empty, TtsEngineDescriptions.Compose(new List<TtsEngineOption>(), Identity));
    }
}
