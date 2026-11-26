using Echokraut.Services;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eGameVoiceChecksTests
{
    private const string Embedded = """
        { "version": 1, "_comment": "x", "aliases": [
          { "ShortName": "vritra", "NpcName": "Vrtra", "Comment": "" },
          { "ShortName": "mysteryvoice", "NpcName": "G'raha Tia", "Comment": "" } ] }
        """;

    private static string Export(string aliases, int version = 1) => $$"""{ "version": {{version}}, "aliases": [ {{aliases}} ] }""";

    [Theory]
    [InlineData(true, GameVoiceAvailability.Available)]
    [InlineData(false, GameVoiceAvailability.ServerUnsupported)]
    [InlineData(null, GameVoiceAvailability.ServerUnsupported)]
    public void ExpectedAvailability_OnlyAnExplicitTrueIsAvailable(bool? inline, GameVoiceAvailability expected) =>
        Assert.Equal(expected, E2eGameVoiceChecks.ExpectedAvailability(inline));

    [Fact]
    public void AliasExport_InTheResourceFormat_Passes()
    {
        var json = Export("""
            { "ShortName": "mysteryvoice", "NpcName": "G'raha Tia" },
            { "ShortName": "thancred", "NpcName": "Thancred Waters" },
            { "ShortName": "vritra", "NpcName": "Vrtra" }
            """);
        var (problems, detail) = E2eGameVoiceChecks.JudgeAliasExport(json, Embedded, "THANCRED", "Thancred Waters");
        Assert.Empty(problems);
        Assert.Equal("export 3 entries (resource 2), thancred -> Thancred Waters", detail);
    }

    [Fact]
    public void AliasExport_Unsorted_Duplicated_LosingAResourceToken_AndWrongVersion_ListsEach()
    {
        var json = Export("""
            { "ShortName": "thancred", "NpcName": "Thancred Waters" },
            { "ShortName": "thancred", "NpcName": "Thancred Waters" },
            { "ShortName": "mysteryvoice", "NpcName": "G'raha Tia" }
            """, version: 2);
        var (problems, _) = E2eGameVoiceChecks.JudgeAliasExport(json, Embedded, "thancred", "Thancred Waters");
        Assert.Equal(new[]
        {
            "version 2, resource has 1",
            "token(s) twice: thancred",
            "entries not sorted by token",
            "resource token(s) missing from the export: vritra",
        }, problems);
    }

    [Fact]
    public void AliasExport_AssignedTokenDisabledOrMissing_Fails()
    {
        var disabled = Export("""
            { "ShortName": "mysteryvoice", "NpcName": "G'raha Tia" },
            { "ShortName": "thancred", "Disabled": true },
            { "ShortName": "vritra", "NpcName": "Vrtra" }
            """);
        Assert.Equal(new[] { "thancred exported as disabled, assigned Thancred Waters" },
            E2eGameVoiceChecks.JudgeAliasExport(disabled, Embedded, "thancred", "Thancred Waters").Problems);

        var missing = Export("""{ "ShortName": "mysteryvoice" }, { "ShortName": "vritra" }""");
        Assert.Contains("assigned token thancred not in the export",
            E2eGameVoiceChecks.JudgeAliasExport(missing, Embedded, "thancred", "Thancred Waters").Problems);
    }

    [Fact]
    public void AliasExport_MissingTopLevelField_AndBrokenJson_Fail()
    {
        Assert.Contains("top-level field(s) missing: version",
            E2eGameVoiceChecks.JudgeAliasExport("""{ "aliases": [] }""", Embedded, "x", "X").Problems);
        Assert.StartsWith("export is not valid JSON",
            Assert.Single(E2eGameVoiceChecks.JudgeAliasExport("{", Embedded, "x", "X").Problems));
    }

    [Fact]
    public void UnmatchedReport_Complete_Passes()
    {
        var json = """
            { "Version": 1, "Language": "DE", "Unmatched": [ { "Token": "a", "Clips": 9 }, { "Token": "b", "Clips": 2 } ],
              "Ambiguous": [ { "Token": "c", "Clips": 1 } ], "SeenWithoutToken": [ "Bob" ] }
            """;
        var (problems, detail) = E2eGameVoiceChecks.JudgeUnmatchedReport(json, "DE");
        Assert.Empty(problems);
        Assert.Equal("report DE: 2 unmatched, 1 ambiguous, 1 seen without token", detail);
    }

    [Fact]
    public void UnmatchedReport_WrongLanguage_Unsorted_EmptyEntries_OrMissing_Fail()
    {
        var json = """{ "Version": 1, "Language": "EN", "Unmatched": [ { "Token": "a", "Clips": 1 }, { "Token": "", "Clips": 3 } ] }""";
        Assert.Equal(new[] { "report language EN, expected DE", "entries without token or clips: 1", "unmatched not sorted by clip count" },
            E2eGameVoiceChecks.JudgeUnmatchedReport(json, "DE").Problems);
        Assert.Equal(new[] { "matching report missing or empty" }, E2eGameVoiceChecks.JudgeUnmatchedReport(null, "DE").Problems);
    }

    [Fact]
    public void SplitSuggestions_WithTheMeasuredBoundaries_Pass()
    {
        var json = """
            { "Version": 2, "Splits": [
              { "NpcName": "Y'shtola", "Language": "DE", "BoundaryPatches": [ "06100" ] },
              { "NpcName": "Hydaelyn", "Language": "DE", "BoundaryPatches": [ "06000" ] },
              { "NpcName": "Thancred", "Language": "EN", "BoundaryPatches": [ "05000" ] } ] }
            """;
        var (problems, detail) = E2eGameVoiceChecks.JudgeSplitSuggestions(json, "DE");
        Assert.Empty(problems);
        Assert.Equal("2 suggestion(s): yshtola 06100, hydaelyn 06000", detail);
    }

    [Fact]
    public void SplitSuggestions_MissingOrWrongPatch_AndAFalseBoundary_Fail()
    {
        var json = """
            { "Version": 2, "Splits": [
              { "NpcName": "Y'shtola", "Language": "DE", "BoundaryPatches": [ "06010" ] },
              { "NpcName": "Alphinaud", "Language": "DE", "BoundaryPatches": [ "04000" ] } ] }
            """;
        Assert.Equal(new[]
        {
            "no yshtola boundary at 06100 (found 06010)",
            "no hydaelyn boundary at 06000 (no entry)",
            "false boundary for alphinaud at 04000",
        }, E2eGameVoiceChecks.JudgeSplitSuggestions(json, "DE").Problems);
    }

    [Fact]
    public void SplitSuggestions_OtherLanguage_HasNoExpectation() =>
        Assert.Empty(E2eGameVoiceChecks.JudgeSplitSuggestions("""{ "Splits": [] }""", "EN").Problems);
}
