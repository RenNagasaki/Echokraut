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
}
