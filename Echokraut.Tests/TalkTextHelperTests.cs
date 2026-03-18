using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class TalkTextHelperTests
{
    private static readonly EKEventId EventId = new(1, TextSource.None);
    private static readonly Mock<ILogService> Log = new();

    // ── NormalizePunctuation ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello─World", "Hello - World")]
    [InlineData("Hello—World", "Hello - World")]
    [InlineData("Hello–World", "Hello-World")]
    [InlineData("Wait...", "Wait.")]
    [InlineData(null, "")]
    public void NormalizePunctuation_Transforms(string? input, string expected)
    {
        Assert.Equal(expected, TalkTextHelper.NormalizePunctuation(input));
    }

    [Fact]
    public void NormalizePunctuation_UnchangedWhenClean()
    {
        Assert.Equal("Hello, world!", TalkTextHelper.NormalizePunctuation("Hello, world!"));
    }

    // ── RemoveStutters ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("I-I can't believe it", "I can't believe it")]
    [InlineData("h-hello there", "hello there")]
    [InlineData("W-wait a moment", "Wait a moment")]   // preserves starting capitalisation
    [InlineData("", "")]
    [InlineData(null, "")]
    public void RemoveStutters_Transforms(string? input, string expected)
    {
        Assert.Equal(expected, TalkTextHelper.RemoveStutters(input));
    }

    [Fact]
    public void RemoveStutters_MultipleStutters_AllRemoved()
    {
        var result = TalkTextHelper.RemoveStutters("I-I can't b-believe it!");
        Assert.DoesNotContain("I-I", result);
        Assert.DoesNotContain("b-b", result);
    }

    [Fact]
    public void RemoveStutters_NormalHyphen_Preserved()
    {
        // "can't" has an apostrophe, not a stutter; "well-known" is not a stutter pattern
        var result = TalkTextHelper.RemoveStutters("well-known fact");
        Assert.Equal("well-known fact", result);
    }

    // ── StripAngleBracketedText ───────────────────────────────────────────────

    [Theory]
    [InlineData("<sigh> I'm tired", "I'm tired")]
    [InlineData("Hello <pause> world", "Hello  world")]
    [InlineData("No tags here", "No tags here")]
    [InlineData("<tag1><tag2>text", "text")]
    public void StripAngleBracketedText_Removes(string input, string expected)
    {
        Assert.Equal(expected, TalkTextHelper.StripAngleBracketedText(input));
    }

    // ── ReplacePhonetics ──────────────────────────────────────────────────────

    [Fact]
    public void ReplacePhonetics_ReplacesMatchCaseInsensitively()
    {
        var corrections = new List<PhoneticCorrection>
        {
            new("FFXIV", "Final Fantasy Fourteen"),
        };

        // The function lowercases the replacement but leaves surrounding text unchanged
        var result = TalkTextHelper.ReplacePhonetics("Play ffxiv daily.", corrections);

        Assert.Equal("Play final fantasy fourteen daily.", result);
    }

    [Fact]
    public void ReplacePhonetics_MultipleCorrections_AllApplied()
    {
        var corrections = new List<PhoneticCorrection>
        {
            new("Y'shtola", "Yshtola"),
            new("G'raha", "Graha"),
        };

        var result = TalkTextHelper.ReplacePhonetics("Y'shtola and G'raha speak.", corrections);

        Assert.Contains("yshtola", result);
        Assert.Contains("graha", result);
    }

    [Fact]
    public void ReplacePhonetics_EmptyList_ReturnsOriginal()
    {
        const string text = "Unchanged text.";
        Assert.Equal(text, TalkTextHelper.ReplacePhonetics(text, []));
    }

    // ── VoiceMessageToFileName ────────────────────────────────────────────────

    [Fact]
    public void VoiceMessageToFileName_StripsSpecialChars()
    {
        var result = TalkTextHelper.VoiceMessageToFileName("Hello, World! How are you?");
        Assert.DoesNotContain(",", result);
        Assert.DoesNotContain("!", result);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain(" ", result);
    }

    [Fact]
    public void VoiceMessageToFileName_LowercasesOutput()
    {
        var result = TalkTextHelper.VoiceMessageToFileName("HELLO");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void VoiceMessageToFileName_TruncatesAt120()
    {
        var longText = new string('a', 200);
        var result = TalkTextHelper.VoiceMessageToFileName(longText);
        Assert.True(result.Length <= 120);
    }

    [Fact]
    public void VoiceMessageToFileName_Exactly120Chars_NotTruncated()
    {
        var text = new string('a', 120);
        Assert.Equal(120, TalkTextHelper.VoiceMessageToFileName(text).Length);
    }

    // ── IsSpeakable ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello world", true)]
    [InlineData("123", true)]
    [InlineData("  spaces  ", true)]
    [InlineData("", false)]
    [InlineData("!!!", false)]
    [InlineData("---", false)]
    public void IsSpeakable_ReturnsExpected(string input, bool expected)
    {
        Assert.Equal(expected, TalkTextHelper.IsSpeakable(input));
    }

    // ── RemovePunctuation ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello world.", "Hello world")]
    [InlineData("Hello world!", "Hello world")]
    [InlineData("Hello world?", "Hello world")]
    [InlineData("Hello world,", "Hello world")]
    [InlineData("Hello world...", "Hello world")]
    [InlineData("Hello world", "Hello world")]           // nothing to remove
    [InlineData("Hello, world!", "Hello, world")]        // only trailing
    public void RemovePunctuation_RemovesTrailing(string input, string expected)
    {
        Assert.Equal(expected, TalkTextHelper.RemovePunctuation(input));
    }

    // ── CleanUpName ───────────────────────────────────────────────────────────

    [Fact]
    public void CleanUpName_RemovesPlaceholder()
    {
        Assert.Equal("Hello ", TalkTextHelper.CleanUpName("Hello [a]"));
    }

    [Fact]
    public void CleanUpName_RemovesNonAlphanumeric()
    {
        // Apostrophe is explicitly allowed by the regex; other symbols like ! are stripped
        var result = TalkTextHelper.CleanUpName("NPC#1!");
        Assert.DoesNotContain("#", result);
        Assert.DoesNotContain("!", result);
        Assert.Contains("NPC", result);
        Assert.Contains("1", result);
    }

    [Fact]
    public void CleanUpName_PreservesHyphenAndUmlaut()
    {
        var result = TalkTextHelper.CleanUpName("Kan-E Senna");
        Assert.Contains("-", result);
    }

    [Fact]
    public void CleanUpName_PreservesUmlaut()
    {
        var result = TalkTextHelper.CleanUpName("Müller");
        Assert.Contains("ü", result);
    }

    // ── RemovePlayerNameInText ────────────────────────────────────────────────

    [Fact]
    public void RemovePlayerNameInText_ReplacesFullName()
    {
        var result = TalkTextHelper.RemovePlayerNameInText("Hello, John Smith.", "John Smith");
        Assert.Contains("<PLAYERNAME>", result);
    }

    [Fact]
    public void RemovePlayerNameInText_ReplacesFirstName()
    {
        var result = TalkTextHelper.RemovePlayerNameInText("Hello, John.", "John Smith");
        Assert.Contains("<PLAYERFIRSTNAME>", result);
    }

    [Fact]
    public void RemovePlayerNameInText_ReplacesLastName()
    {
        var result = TalkTextHelper.RemovePlayerNameInText("Hello, Smith.", "John Smith");
        Assert.Contains("<PLAYERLASTNAME>", result);
    }

    [Fact]
    public void RemovePlayerNameInText_EmptyPlayerName_ReturnsOriginal()
    {
        const string text = "Unchanged text.";
        Assert.Equal(text, TalkTextHelper.RemovePlayerNameInText(text, ""));
    }

    // ── ExtractTokens ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractTokens_ReplacesValueWithKey()
    {
        var map = new Dictionary<string, string?> { ["<PLAYER>"] = "John Smith" };
        var result = TalkTextHelper.ExtractTokens("Hello, John Smith!", map);
        Assert.Equal("Hello, <PLAYER>!", result);
    }

    [Fact]
    public void ExtractTokens_LongestMatchFirst()
    {
        // Full name must be replaced before first name to avoid partial replacement
        var map = new Dictionary<string, string?>
        {
            ["<FULL>"]  = "John Smith",
            ["<FIRST>"] = "John",
        };
        var result = TalkTextHelper.ExtractTokens("John Smith is here", map);
        Assert.Contains("<FULL>", result);
        Assert.DoesNotContain("John", result);
    }

    [Fact]
    public void ExtractTokens_NullValueInMap_Skipped()
    {
        var map = new Dictionary<string, string?> { ["<X>"] = null };
        var result = TalkTextHelper.ExtractTokens("Hello world", map);
        Assert.Equal("Hello world", result);
    }

    // ── ReplaceSsmlTokens ─────────────────────────────────────────────────────

    [Fact]
    public void ReplaceSsmlTokens_ReplacesAmpersand()
    {
        Assert.Equal("rock and roll", TalkTextHelper.ReplaceSsmlTokens("rock & roll"));
    }

    // ── AnalyzeAndImproveText ─────────────────────────────────────────────────

    [Fact]
    public void AnalyzeAndImproveText_AddsMissingSpaceAfterPunctuation()
    {
        var result = TalkTextHelper.AnalyzeAndImproveText("Hello.World");
        Assert.Equal("Hello. World", result);
    }

    [Fact]
    public void AnalyzeAndImproveText_DoesNotChangeAlreadySpaced()
    {
        var result = TalkTextHelper.AnalyzeAndImproveText("Hello. World");
        Assert.Equal("Hello. World", result);
    }

    // ── SplitKeepLeft ─────────────────────────────────────────────────────────

    [Fact]
    public void SplitKeepLeft_SplitsAfterDelimiter()
    {
        var parts = TalkTextHelper.SplitKeepLeft("Hello, world. How are you?", ",.");
        // First part should end with comma or period
        Assert.True(parts[0].EndsWith(",") || parts[0].EndsWith("."));
    }

    [Fact]
    public void SplitKeepLeft_NoDelimiters_ReturnsSingleElement()
    {
        var parts = TalkTextHelper.SplitKeepLeft("Hello world", ",.");
        Assert.Single(parts);
        Assert.Equal("Hello world", parts[0]);
    }

    // ── ReplaceRomanNumbers ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Final Fantasy XIV", "Final Fantasy 14")]
    [InlineData("Chapter III ends here.", "Chapter 3 ends here.")]
    public void ReplaceRomanNumbers_ConvertsToArabic(string input, string expected)
    {
        var result = TalkTextHelper.ReplaceRomanNumbers(Log.Object, EventId, input);
        Assert.Equal(expected, result);
    }

    // ── ReplaceCurrency ───────────────────────────────────────────────────────

    [Fact]
    public void ReplaceCurrency_RemovesSeparatorDots()
    {
        var result = TalkTextHelper.ReplaceCurrency(Log.Object, EventId, "1.000 gil");
        Assert.Equal("1000 gil", result);
    }
}
