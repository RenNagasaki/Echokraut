using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Keys are verbatim from the live sheets (scanned 2026-08-09). The token counts quoted in the
/// comments are the measured occurrence numbers across all scripted-sheet families.
/// </summary>
public class ScriptedSheetKeyTests
{
    [Theory]
    [InlineData("FesHlx2024Transform2_00856", "TEXT_FESHLX2024TRANSFORM2_00856_TRANSFORMHLX2024_000_000", "TRANSFORMHLX2024")]
    [InlineData("RaidGuideEntrance30", "TEXT_RAIDGUIDEENTRANCE30_REDBRIX_000_000", "REDBRIX")]
    [InlineData("WarpInnGridania", "TEXT_WARPINNGRIDANIA_ANTOINAUT_000_1", "ANTOINAUT")]
    [InlineData("OpeningLimsaLominsa", "TEXT_OPENINGLIMSALOMINSA_RYSSFLOH_000_02", "RYSSFLOH")]
    [InlineData("DeepDungeon2Achievement", "TEXT_DEEPDUNGEON2ACHIEVEMENT_DD2FORMERLEADER_000_000", "DD2FORMERLEADER")]
    public void ExtractsSpeakerTokenAfterSheetName(string leaf, string key, string expected)
    {
        Assert.True(ScriptedSheetKey.TryGetSpeakerToken(leaf, key, out var token));
        Assert.Equal(expected, token);
    }

    [Fact]
    public void TokenWithoutTrailingSegment_IsStillReturned()
    {
        Assert.True(ScriptedSheetKey.TryGetSpeakerToken("CtsHnt40Kugane2_00428",
            "TEXT_CTSHNT40KUGANE2_00428_TALK", out var token));
        Assert.Equal("TALK", token);
    }

    [Theory]
    [InlineData("WarpInnGridania", "TEXT_SOMETHINGELSE_ANTOINAUT_000_1")] // key of a different sheet
    [InlineData("WarpInnGridania", "TEXT_WARPINNGRIDANIA_")]              // nothing after the prefix
    [InlineData("WarpInnGridania", "")]
    [InlineData("", "TEXT_WARPINNGRIDANIA_ANTOINAUT_000_1")]
    public void RejectsKeysThatDoNotBelong(string leaf, string key)
        => Assert.False(ScriptedSheetKey.TryGetSpeakerToken(leaf, key, out _));

    [Theory]
    [InlineData("SYSTEM")]      // 2,123 in custom/*
    [InlineData("A1")]          // 1,897 — the player's own answer
    [InlineData("A2")]
    [InlineData("Q1")]          // 607 — "What will you say?"
    [InlineData("LCUT")]        // 1,028 — scene marker, speaker lives elsewhere
    [InlineData("ENTER")]
    [InlineData("LEAVE")]
    [InlineData("TALK")]
    [InlineData("MENU")]
    [InlineData("MAINMENU")]
    [InlineData("CHALLENGEMENU")]
    [InlineData("ENTER2")]      // numbered variant of a flow marker
    [InlineData("LEAVE2")]
    [InlineData("00105")]       // pure line number — sheet has no speaker segment
    [InlineData("NARRATION")]
    public void NonSpeechTokensAreRejected(string token)
        => Assert.True(ScriptedSheetKey.IsNonSpeechToken(token));

    [Theory]
    [InlineData("REDBRIX")]
    [InlineData("ANTOINAUT")]
    [InlineData("TRANSFORMHLX2024")]
    // Deliberately treated as possible speakers so they become alias candidates for review
    // rather than being silently dropped by a keyword list.
    [InlineData("PLATOONINSTRUCTOR")]
    [InlineData("OMEGATERMINAL")]
    [InlineData("MEMBERC01548")]
    [InlineData("DD2FORMERLEADER")]
    [InlineData("KNOWLEDGE")]
    [InlineData("BOY05426")]
    public void SpeakerLikeTokensAreKept(string token)
        => Assert.False(ScriptedSheetKey.IsNonSpeechToken(token));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankTokenIsNonSpeech(string? token)
        => Assert.True(ScriptedSheetKey.IsNonSpeechToken(token));

    [Fact]
    public void DigitStrippingDoesNotSwallowRealNames()
    {
        // "BOY05426" must not reduce to something in the non-speech list.
        Assert.False(ScriptedSheetKey.IsNonSpeechToken("BOY05426"));
        // ...while "ENTER3" must.
        Assert.True(ScriptedSheetKey.IsNonSpeechToken("ENTER3"));
    }
}
