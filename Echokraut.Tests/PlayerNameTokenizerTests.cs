using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// <see cref="PlayerNameTokenizer"/> is the write-side counterpart of
/// <see cref="TalkTextHelper.SubstitutePlaceholders"/>. Everything persisted into
/// <c>voice_clips</c> passes through it, so the round trip between the two must hold exactly —
/// otherwise the live path's row stops matching the harvested one and the duplicate-row bug
/// this was written for comes back.
/// </summary>
public class PlayerNameTokenizerTests
{
    private const string PlayerName = "Jon Doe";

    [Fact]
    public void Tokenize_FullName_PrefersTheFullNameToken()
    {
        var result = PlayerNameTokenizer.Tokenize("Well met, Jon Doe!", PlayerName);

        // Longest value first — "Jon Doe" must not decay into two separate tokens.
        Assert.Equal("Well met, -PlayerName-!", result);
    }

    [Fact]
    public void Tokenize_FirstNameOnly_UsesTheFirstNameToken()
    {
        var result = PlayerNameTokenizer.Tokenize("Well met, Jon!", PlayerName);

        Assert.Equal("Well met, -PlayerFirstName-!", result);
    }

    [Fact]
    public void Tokenize_LastNameOnly_UsesTheLastNameToken()
    {
        var result = PlayerNameTokenizer.Tokenize("A pleasure, Master Doe.", PlayerName);

        Assert.Equal("A pleasure, Master -PlayerLastName-.", result);
    }

    [Fact]
    public void Tokenize_EmptyPlayerName_LeavesTextUntouched()
    {
        // The cold LocalPlayerName cache. Writing a half-tokenised row would be worse than
        // writing none, so the text must survive verbatim.
        const string text = "Well met, Jon Doe!";

        Assert.Equal(text, PlayerNameTokenizer.Tokenize(text, ""));
        Assert.Equal(text, PlayerNameTokenizer.Tokenize(text, "   "));
    }

    [Fact]
    public void Tokenize_AlreadyTokenizedText_IsUnchanged()
    {
        // Harvested rows come in already tokenised; running them through again must be a no-op
        // so the migration and the live path are both idempotent.
        const string text = "Well met, -PlayerFirstName-!";

        Assert.Equal(text, PlayerNameTokenizer.Tokenize(text, PlayerName));
    }

    [Fact]
    public void Tokenize_NameWithoutSurname_DoesNotMapTheLastNameToken()
    {
        // A single-word name must not leave -PlayerLastName- mapped to the empty string, which
        // would match at every position in the text.
        var map = PlayerNameTokenizer.BuildTokenMap("Jon");

        Assert.False(map.ContainsKey(TalkTextHelper.PlaceholderLastName));
        Assert.Equal("Hello -PlayerName-", PlayerNameTokenizer.Tokenize("Hello Jon", "Jon"));
    }

    [Fact]
    public void SubstitutingWithAColdNameCache_LeavesThePlaceholderInPlace()
    {
        // This is exactly why VoiceClipManagerService refuses to generate such a line: the
        // substitution fails silently, and the TTS would read "-PlayerFirstName-" out loud into
        // a WAV that the live path then adopts forever.
        var spoken = TalkTextHelper.SubstitutePlaceholders("Well met, -PlayerFirstName-!", "");

        Assert.True(TalkTextHelper.ContainsPlayerPlaceholder(spoken));
    }

    [Theory]
    [InlineData("Well met, Jon Doe!")]
    [InlineData("Jon, hold the line!")]
    [InlineData("The Doe family sends word.")]
    [InlineData("No name in this line at all.")]
    public void TokenizeThenSubstitute_RoundTripsToTheOriginalText(string original)
    {
        var tokenized = PlayerNameTokenizer.Tokenize(original, PlayerName);
        var restored = TalkTextHelper.SubstitutePlaceholders(tokenized, PlayerName);

        Assert.Equal(original, restored);
    }
}
