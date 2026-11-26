using System.Linq;
using System.Collections.Generic;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Rules behind the engine dropdown: every published engine is listed, but only the ones the
/// installed wrapper can actually run may be picked.
/// </summary>
public class TtsEngineAvailabilityTests
{
    private const string Format = "{0} (from wrapper version {1})";

    private static List<TtsEngineInfo> Catalog() =>
    [
        new() { Id = "xtts", Name = "XTTS", MinVersion = "0.0.0.1" },
        new() { Id = "f5",   Name = "F5",   MinVersion = "0.0.0.1" },
        new() { Id = "moss", Name = "MOSS", MinVersion = "0.0.1.0" }, // as published for the real MOSS release
    ];

    [Theory]
    // Installed: the installed wrapper gates, whatever the latest release is.
    [InlineData(true, "0.0.0.2", "0.0.1.0", false)]
    // Not installed: the release a fresh install would fetch gates.
    [InlineData(false, "0.0.0.2", "0.0.1.0", true)]
    public void ForConfig_GatesByInstalledWrapperOnceInstalled(bool installed, string installedVersion,
        string latest, bool mossAvailable)
    {
        var ek = new EchokrauTtsData { LocalInstall = installed, InstalledWrapperVersion = installedVersion, TtsEngine = "f5" };

        var options = TtsEngineAvailability.ForConfig(Catalog(), ek, latest, "de", listUnavailable: true, s => "<" + s + ">");

        Assert.Equal(mossAvailable, options[2].Available);
        Assert.Equal(mossAvailable ? "MOSS" : "<MOSS (from wrapper version 0.0.1.0)>", options[2].Label);
    }

    [Fact]
    public void TooOldWrapper_ListsTheEngineButBlocksIt()
    {
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.0.2", Format);

        Assert.Equal(3, options.Count);
        Assert.True(options[0].Available);
        Assert.True(options[1].Available);
        var moss = options[2];
        Assert.False(moss.Available);
        Assert.Equal("MOSS (from wrapper version 0.0.1.0)", moss.Label);
        Assert.Equal("0.0.1.0", moss.MinVersion);
    }

    [Fact]
    public void NewEnoughWrapper_MakesTheEngineSelectableAndDropsTheSuffix()
    {
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.1.0", Format);

        var moss = options[2];
        Assert.True(moss.Available);
        Assert.Equal("MOSS", moss.Label);
    }

    [Fact]
    public void WithoutALocalInstall_TheReleaseAFreshInstallWouldGetDecides()
    {
        // Regression (first in-game test, 2026-09-10): the wizard offered MOSS with no "from version"
        // hint while installing a wrapper that does not contain it, because "no local install" used
        // to mean "offer everything".
        var options = TtsEngineAvailability.Build(Catalog(), TtsEngineAvailability.GatingVersion(false, "", "0.0.1.0"), Format);

        Assert.All(options, o => Assert.True(o.Available));
    }

    [Fact]
    public void WithoutALocalInstall_AnEngineTheComingReleaseLacks_IsStillBlocked()
    {
        var options = TtsEngineAvailability.Build(
            Catalog(), TtsEngineAvailability.GatingVersion(false, "", "0.0.0.7"), Format);

        Assert.False(options[2].Available);
        Assert.Equal("MOSS (from wrapper version 0.0.1.0)", options[2].Label);
    }

    [Theory]
    [InlineData(true, "0.0.0.7", "0.0.1.0", "0.0.0.7")]  // installed wins: that wrapper is what runs
    [InlineData(false, "0.0.0.7", "0.0.1.0", "0.0.1.0")] // no install: what would be fetched
    [InlineData(true, "", "0.0.1.0", "0.0.1.0")]         // flagged as installed but no tag recorded
    public void GatingVersion_PicksTheWrapperThatWouldRunTheEngine(
        bool localInstall, string installed, string latest, string expected)
    {
        Assert.Equal(expected, TtsEngineAvailability.GatingVersion(localInstall, installed, latest));
    }

    [Fact]
    public void Signature_ChangesWithAvailability()
    {
        // What the per-frame refresh compares: a finished wrapper update must be visible here, or the
        // dropdown keeps the stale labels until the plugin is reloaded.
        var before = TtsEngineAvailability.Signature(TtsEngineAvailability.Build(Catalog(), "0.0.0.7", Format));
        var after = TtsEngineAvailability.Signature(TtsEngineAvailability.Build(Catalog(), "0.0.1.0", Format));

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ConfiguredEngineMissingFromTheCatalog_IsStillListed()
    {
        // The dropdown has to be able to show what is actually configured, even after an engine is
        // dropped from the catalog — otherwise it would silently display someone else's engine.
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.0.9", Format, "vall-e");

        Assert.Contains(options, o => o.Id == "vall-e" && o.Available);
    }

    [Fact]
    public void EmptyCatalog_YieldsOnlyTheConfiguredEngine()
    {
        var options = TtsEngineAvailability.Build([], "0.0.0.2", Format, "xtts");

        Assert.Single(options);
        Assert.Equal("xtts", options[0].Id);
    }

    [Fact]
    public void BlankIdsAndDuplicates_AreIgnored()
    {
        List<TtsEngineInfo> catalog =
        [
            new() { Id = "", Name = "ghost", MinVersion = "0.0.0.1" },
            new() { Id = "xtts", Name = "XTTS", MinVersion = "0.0.0.1" },
            new() { Id = "xtts", Name = "XTTS (dupe)", MinVersion = "9.9.9.9" },
        ];

        var options = TtsEngineAvailability.Build(catalog, "0.0.0.2", Format);

        Assert.Single(options);
        Assert.Equal("XTTS", options[0].Label); // first entry wins
    }

    [Fact]
    public void MissingName_FallsBackToTheId()
    {
        List<TtsEngineInfo> catalog = [new() { Id = "moss", Name = "", MinVersion = "0.0.0.1" }];

        var options = TtsEngineAvailability.Build(catalog, "0.0.0.2", Format);

        Assert.Equal("moss", options[0].Label);
    }

    [Fact]
    public void LabelLookup_RoundTripsBothWays()
    {
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.0.2", Format);

        var label = TtsEngineAvailability.LabelFor(options, "f5");
        Assert.Equal("F5", label);
        Assert.Equal("f5", TtsEngineAvailability.FromLabel(options, label)?.Id);
        Assert.Null(TtsEngineAvailability.FromLabel(options, "no such label"));
    }

    [Fact]
    public void LabelFor_UnknownId_FallsBackToTheIdItself()
    {
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.0.2", Format);

        Assert.Equal("whisper", TtsEngineAvailability.LabelFor(options, "whisper"));
    }

    [Fact]
    public void ListUnavailableFalse_HidesTheBlockedEngineEntirely()
    {
        // First-time wizard: the release about to be installed is fixed, so nothing in that window
        // could unlock a newer engine — listing it blocked would only be noise.
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.0.7", Format, "xtts",
            listUnavailable: false);

        Assert.Equal(2, options.Count);
        Assert.DoesNotContain(options, o => o.Id == "moss");
        Assert.All(options, o => Assert.True(o.Available));
    }

    [Fact]
    public void ListUnavailableFalse_StillKeepsTheConfiguredEngine()
    {
        // Someone who already picked MOSS and then reinstalls an older wrapper must still see what
        // their config says — and see it BLOCKED. Merely dropping it would hand it to the
        // self-preservation branch, which re-adds an unlisted id as available: hiding would make it
        // selectable, the exact opposite of what hiding is for.
        var options = TtsEngineAvailability.Build(Catalog(), "0.0.0.7", Format, "moss",
            listUnavailable: false);

        var moss = Assert.Single(options, o => o.Id == "moss");
        Assert.False(moss.Available);
        Assert.Equal("MOSS (from wrapper version 0.0.1.0)", moss.Label);
    }
    private static IReadOnlyList<TtsEngineInfo> ShippedCatalog() =>
        Echokraut.Services.TtsEngineCatalogService.Parse(System.IO.File.ReadAllText(TestPaths.EnginesJsonPath))!;

    [Fact]
    public void ShippedWrapperVersion_OffersEveryShippedEngine_OnAFreshInstall()
    {
        // A fresh install fetches echokrauTtsVersion; every engine in the shipped catalog must be
        // runnable by that release, otherwise the wizard offers nothing it could install.
        var latest = Echokraut.Services.RemoteUrlService.LoadEmbeddedFallback().EchokrauTtsVersion;
        var engines = ShippedCatalog();

        var options = TtsEngineAvailability.Build(engines,
            TtsEngineAvailability.GatingVersion(false, null, latest), Format, listUnavailable: false);

        Assert.Equal(engines.Count, options.Count);
        Assert.All(options, o => Assert.True(o.Available, $"{o.Id} needs a newer wrapper than {latest}"));
    }

    [Fact]
    public void LocalWrapper0019_BlocksQwenAndPocket_UntilUpdated()
    {
        var engines = ShippedCatalog();

        var options = TtsEngineAvailability.Build(engines,
            TtsEngineAvailability.GatingVersion(true, "0.0.1.9", "0.0.2.0"), Format);

        foreach (var id in new[] { "qwen-0.6b", "qwen-1.7b", "pocket" })
        {
            var option = Assert.Single(options, o => o.Id == id);
            Assert.False(option.Available);
            Assert.EndsWith("(from wrapper version 0.0.2.0)", option.Label);
        }
        Assert.True(Assert.Single(options, o => o.Id == "xtts").Available);
    }

    // ── Game language (TtsEngineInfo.Languages) ──

    private const string LanguageFormat = "{0} (not available in this game language)";

    private static List<TtsEngineInfo> PocketCatalog() =>
    [
        new() { Id = "xtts", Name = "XTTS", MinVersion = "0.0.0.1" },
        new() { Id = "pocket", Name = "Pocket TTS", MinVersion = "0.0.2.0", Languages = ["de", "en", "es", "fr", "it", "pt"] },
    ];

    [Theory]
    [InlineData("ja", false)]
    [InlineData("JA", false)]
    [InlineData("de", true)]
    [InlineData("fr", true)]
    [InlineData(null, true)] // language unknown: nothing to judge by, never block
    [InlineData("", true)]
    public void Language_BlocksPocketOnlyWithoutAModel(string? language, bool pocketAvailable)
    {
        var options = TtsEngineAvailability.Build(PocketCatalog(), "0.0.2.0", Format, language: language);

        var pocket = Assert.Single(options, o => o.Id == "pocket");
        Assert.Equal(pocketAvailable, pocket.Available);
        Assert.Equal(pocketAvailable ? "Pocket TTS" : "Pocket TTS (not available in this game language)", pocket.Label);
        Assert.Equal(string.Empty, pocket.MinVersion);
        Assert.True(Assert.Single(options, o => o.Id == "xtts").Available); // no list = every language
    }

    [Fact]
    public void Language_WinsOverTheVersionNote_BecauseNoUpdateBringsTheModel()
    {
        var options = TtsEngineAvailability.Build(PocketCatalog(), "0.0.1.9", Format, language: "ja");

        var pocket = Assert.Single(options, o => o.Id == "pocket");
        Assert.False(pocket.Available);
        Assert.Equal("Pocket TTS (not available in this game language)", pocket.Label);
        Assert.Equal(string.Empty, pocket.MinVersion);
    }

    [Fact]
    public void Language_WizardHidesTheEntry_ButKeepsItBlockedWhenItIsConfigured()
    {
        var hidden = TtsEngineAvailability.Build(PocketCatalog(), "0.0.2.0", Format, "xtts",
            listUnavailable: false, language: "ja");
        var configured = TtsEngineAvailability.Build(PocketCatalog(), "0.0.2.0", Format, "pocket",
            listUnavailable: false, language: "ja");

        Assert.Equal(["xtts"], hidden.Select(o => o.Id));
        var pocket = Assert.Single(configured, o => o.Id == "pocket");
        Assert.False(pocket.Available); // not re-added as available by the "keep what is configured" branch
    }

    [Fact]
    public void ForConfig_UsesTheLocalizedLanguageLabel()
    {
        var ek = new EchokrauTtsData { LocalInstall = true, InstalledWrapperVersion = "0.0.2.0", TtsEngine = "xtts" };

        var options = TtsEngineAvailability.ForConfig(PocketCatalog(), ek, "0.0.2.0", "ja", listUnavailable: true, s => "<" + s + ">");

        Assert.Equal("<Pocket TTS (not available in this game language)>", Assert.Single(options, o => o.Id == "pocket").Label);
    }

    [Theory]
    [InlineData("pocket", "ja", true)]
    [InlineData("POCKET", "ja", true)]
    [InlineData("pocket", "de", false)]
    [InlineData("xtts", "ja", false)]
    [InlineData("vall-e", "ja", false)] // not in the catalog: nothing to judge by
    public void IsBlockedByLanguage(string engineId, string language, bool expected)
    {
        Assert.Equal(expected, TtsEngineAvailability.IsBlockedByLanguage(PocketCatalog(), engineId, language));
    }

    [Fact]
    public void ShippedCatalog_PocketHasNoJapanese_EveryOtherEngineSpeaksAllGameLanguages()
    {
        var engines = ShippedCatalog();

        foreach (var lang in new[] { "de", "en", "fr", "ja" })
        foreach (var engine in engines)
            Assert.Equal(!(engine.Id == "pocket" && lang == "ja"), TtsEngineAvailability.SupportsLanguage(engine, lang));
    }
}
