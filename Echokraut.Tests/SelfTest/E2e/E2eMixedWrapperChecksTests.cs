using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eMixedWrapperChecksTests
{
    private const string Format = "{0} (from wrapper version {1})";

    private static readonly List<TtsEngineInfo> Catalog =
    [
        new() { Id = "qwen-0.6b", Name = "Qwen 0.6B", MinVersion = "0.0.2.0" },
        new() { Id = "xtts", Name = "XTTS", MinVersion = "0.0.0.1" },
        new() { Id = "moss", Name = "MOSS", MinVersion = "0.0.1.0" },
        new() { Id = "pocket", Name = "Pocket", MinVersion = "0.0.2.0" },
    ];

    [Fact]
    public void Blocked_EnginesAboveTheInstalledTag_InCatalogOrder()
        => Assert.Equal(["qwen-0.6b", "pocket"], E2eMixedWrapperChecks.Blocked(Catalog, "0.0.1.9").Select(e => e.Id));

    [Fact]
    public void Blocked_SameTagAsMinVersion_IsNotBlocked()
    {
        Assert.Equal(["qwen-0.6b", "pocket"], E2eMixedWrapperChecks.Blocked(Catalog, "0.0.1.0").Select(e => e.Id));
        Assert.Equal(["qwen-0.6b", "moss", "pocket"], E2eMixedWrapperChecks.Blocked(Catalog, "0.0.0.9").Select(e => e.Id));
    }

    [Theory]
    [InlineData("0.0.2.0")]
    [InlineData("test (local zip)")]
    public void Blocked_NothingForTheNewestOrAnUnparseableTag(string installed)
        => Assert.Empty(E2eMixedWrapperChecks.Blocked(Catalog, installed));

    [Fact]
    public void EntryProblems_TheProductionListForAnOldWrapper_HasNone()
    {
        var options = TtsEngineAvailability.Build(Catalog, "0.0.1.9", Format);

        Assert.Empty(E2eMixedWrapperChecks.EntryProblems(options, E2eMixedWrapperChecks.Blocked(Catalog, "0.0.1.9"), Format));
    }

    [Fact]
    public void EntryProblems_ABlockedEngineOffered_IsReported()
    {
        // As if the gate had judged against the newest release instead of the installed one.
        var options = TtsEngineAvailability.Build(Catalog, "0.0.2.0", Format);

        var problems = E2eMixedWrapperChecks.EntryProblems(options, E2eMixedWrapperChecks.Blocked(Catalog, "0.0.1.9"), Format);

        Assert.Equal([
            "qwen-0.6b offered although it needs wrapper 0.0.2.0",
            "qwen-0.6b label 'Qwen 0.6B' does not end in ' (from wrapper version 0.0.2.0)'",
            "pocket offered although it needs wrapper 0.0.2.0",
            "pocket label 'Pocket' does not end in ' (from wrapper version 0.0.2.0)'",
        ], problems);
    }

    [Fact]
    public void EntryProblems_AfterTheUpdate_AReasonLeftOnAnEntry_IsReported()
    {
        var options = new List<TtsEngineOption>
        {
            new("xtts", "XTTS", "XTTS", true, "", ""),
            new("pocket", "Pocket", "Pocket (from wrapper version 0.0.2.0)", true, "", ""),
            new("moss", "MOSS", "MOSS (from wrapper version 0.0.1.0)", false, "0.0.1.0", ""),
        };

        Assert.Equal([
            "pocket available but labelled 'Pocket (from wrapper version 0.0.2.0)'",
            "moss blocked although the wrapper ships it",
        ], E2eMixedWrapperChecks.EntryProblems(options, [], Format));
    }

    [Fact]
    public void EntryProblems_ABlockedEngineMissingFromTheList_IsReported()
    {
        var options = TtsEngineAvailability.Build(Catalog, "0.0.1.9", Format, listUnavailable: false);

        Assert.Equal(["qwen-0.6b not listed (the Backend tab lists blocked engines)", "pocket not listed (the Backend tab lists blocked engines)"],
            E2eMixedWrapperChecks.EntryProblems(options, E2eMixedWrapperChecks.Blocked(Catalog, "0.0.1.9"), Format));
    }

    [Fact]
    public void Reason_IsThePatternAfterTheName()
    {
        Assert.Equal(" (from wrapper version 0.0.2.0)", E2eMixedWrapperChecks.Reason(Format, "0.0.2.0"));
        Assert.Equal(" (ab Wrapper-Version 0.0.2.0)", E2eMixedWrapperChecks.Reason("{0} (ab Wrapper-Version {1})", "0.0.2.0"));
    }

    private static E2eInstallerLog.ModeLine Mode(string line) => E2eInstallerLog.ParseMode(line)!;

    private static readonly List<E2eInstallerLog.ModeLine> Modes =
    [
        Mode("Mode: echokrautts | installRoot=E:\\EkE2E | url=https://github.com/o/w/releases/download/0.0.1.9/EchokrauTTS.zip | ttsBackend=xtts"),
        Mode("Mode: echokrautts | installRoot=E:\\EkE2E | url=(none) | ttsBackend=xtts"),
        Mode("Mode: updateechokrautts | installRoot=E:\\EkE2E | url=https://github.com/o/w/releases/download/0.0.2.0/EchokrauTTS.zip | ttsBackend=xtts"),
    ];

    [Fact]
    public void DownloadProblem_TheLastDownloadOfTheModeNamesTheTag()
    {
        Assert.Null(E2eMixedWrapperChecks.DownloadProblem(Modes, "echokrautts", "0.0.1.9"));
        Assert.Null(E2eMixedWrapperChecks.DownloadProblem(Modes, "updateechokrautts", "0.0.2.0"));
    }

    [Fact]
    public void DownloadProblem_AnotherRelease_NamesWhatWasFetched()
        => Assert.Equal("installer 'echokrautts' fetched https://github.com/o/w/releases/download/0.0.1.9/EchokrauTTS.zip, not release 0.0.2.0",
            E2eMixedWrapperChecks.DownloadProblem(Modes, "echokrautts", "0.0.2.0"));

    [Fact]
    public void DownloadProblem_NoDownloadOfThatMode_IsReported()
        => Assert.Equal("no installer Mode line 'updateechokrautts' with a download URL",
            E2eMixedWrapperChecks.DownloadProblem(Modes.Take(2), "updateechokrautts", "0.0.2.0"));
}
