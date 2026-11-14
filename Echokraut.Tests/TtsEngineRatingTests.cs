using System.IO;
using System.Linq;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The "how good, how demanding" suffix in the engine dropdown, so engines can be compared right in
/// the list. Built from optional <c>engines.json</c> fields.
/// </summary>
public class TtsEngineRatingTests
{
    private static string Id(string s) => s;

    [Fact]
    public void GpuEngine_ShowsQualityVramAndLoad()
    {
        var e = new TtsEngineInfo { Quality = 5, Hardware = "gpu", VramGb = 5, Load = "high" };
        Assert.Equal("(Quality 5/5, GPU ~5 GB, high load)", TtsEngineRating.Format(e, Id));
    }

    [Fact]
    public void CpuEngine_PrefersCoresOverLoad()
    {
        var pocket = new TtsEngineInfo { Quality = 3, Hardware = "cpu", Cores = 4, Load = "medium" };
        var moss = new TtsEngineInfo { Quality = 1, Hardware = "cpu", Load = "low" };
        Assert.Equal("(Quality 3/5, CPU, 4 cores)", TtsEngineRating.Format(pocket, Id));
        Assert.Equal("(Quality 1/5, CPU, light)", TtsEngineRating.Format(moss, Id));
    }

    [Fact]
    public void FractionalVram_UsesInvariantDecimalPoint()
    {
        var e = new TtsEngineInfo { Hardware = "gpu", VramGb = 2.5 };
        Assert.Equal("(GPU ~2.5 GB)", TtsEngineRating.Format(e, Id));
    }

    [Fact]
    public void NoRatingFields_NoSuffix()
    {
        // An engines.json from before the rating fields must keep the plain names.
        Assert.Equal(string.Empty, TtsEngineRating.Format(new TtsEngineInfo { Id = "xtts", Name = "XTTS" }, Id));
        Assert.Equal(string.Empty, TtsEngineRating.Format(new TtsEngineInfo { Quality = 9 }, Id));
    }

    [Fact]
    public void Suffix_IsLocalized()
    {
        var e = new TtsEngineInfo { Quality = 4, Hardware = "gpu", VramGb = 4, Load = "high" };
        var de = TtsEngineRating.Format(e, s => s switch
        {
            "Quality {0}/5" => "Qualität {0}/5",
            "high load" => "hohe Last",
            _ => s,
        });
        Assert.Equal("(Qualität 4/5, GPU ~4 GB, hohe Last)", de);
    }

    [Fact]
    public void Dropdown_LabelCarriesTheRating_AlsoWhenBlocked()
    {
        var catalog = new[]
        {
            new TtsEngineInfo { Id = "xtts", Name = "XTTS", Quality = 4, Hardware = "gpu", VramGb = 3, Load = "medium", MinVersion = "0.0.0.1" },
            new TtsEngineInfo { Id = "pocket", Name = "Pocket TTS", Quality = 3, Hardware = "cpu", Cores = 4, MinVersion = "0.0.2.0" },
        };
        var options = TtsEngineAvailability.Build(catalog, "0.0.1.9", "{0} (from wrapper version {1})",
            rating: e => TtsEngineRating.Format(e, Id));

        Assert.Equal("XTTS (Quality 4/5, GPU ~3 GB, medium load)", options[0].Label);
        Assert.Equal("XTTS", options[0].Name);
        Assert.Equal("Pocket TTS (Quality 3/5, CPU, 4 cores) (from wrapper version 0.0.2.0)", options[1].Label);
        Assert.Same(options[1], TtsEngineAvailability.FromLabel(options, options[1].Label));
    }

    [Fact]
    public void EmbeddedCatalog_RatesEveryEngine_BestFirst()
    {
        var engines = TtsEngineCatalogService.Parse(File.ReadAllText(TestPaths.EnginesJsonPath));
        Assert.NotNull(engines);
        Assert.All(engines!, e =>
        {
            Assert.InRange(e.Quality, 1, 5);
            Assert.Contains(e.Hardware, new[] { "gpu", "cpu" });
            Assert.NotEqual(string.Empty, TtsEngineRating.Format(e, Id));
        });
        var grades = engines!.Select(e => e.Quality).ToList();
        Assert.Equal(grades.OrderByDescending(q => q).ToList(), grades);
    }

    [Fact]
    public void RatingTexts_AreTranslated()
        => LocAssert.Translated(["Quality {0}/5", "GPU ~{0} GB", "CPU, {0} cores", "high load", "medium load", "light",
            "Solid quality. Needs a graphics card and takes about two seconds before the first word is heard. Pick this if your GPU has room to spare."]);
}
