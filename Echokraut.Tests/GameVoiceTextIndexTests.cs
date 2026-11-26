using System.Linq;
using Echokraut.Helper.Functional.GameVoice;
using Xunit;

namespace Echokraut.Tests;

public class GameVoiceTextIndexTests
{
    private static CutsceneTextRow Row(string key, string text, bool macro = false) => new(key, text, macro);

    [Fact]
    public void Build_GroupsLinesByNormalizedSpeakerToken()
    {
        var index = GameVoiceTextIndex.Build(new[]
        {
            Row("TEXT_VOICEMAN_06006_000010_YSHTOLA", "First line."),
            Row("TEXT_VOICEMAN_06006_000020_YSHTOLA", "Second line."),
            Row("TEXT_VOICEMAN_06006_000030_THANCRED", "Other speaker."),
        });

        Assert.Equal(3, index.LineCount);
        var lines = index.LinesFor("YSHTOLA");
        Assert.Equal(new[] { "vo_voiceman_06006_000010", "vo_voiceman_06006_000020" }, lines.Select(l => l.AudioBase));
        Assert.Equal("yshtola", lines[0].SpeakerToken);
    }

    [Fact]
    public void Build_DropsUnvoicedMacroEmptyAndMalformedRows()
    {
        var index = GameVoiceTextIndex.Build(new[]
        {
            Row("TEXT_VOICEMAN_06101_000040_ENDSINGER_NONE_VOICE", "Not voiced."),
            Row("TEXT_VOICEMAN_06006_000010_YSHTOLA", "Hello, !", macro: true),
            Row("TEXT_VOICEMAN_06006_000020_YSHTOLA", "   "),
            Row("TEXT_VOICEMAN_06006_SYSTEM", "Malformed."),
            Row("", "No key."),
        });

        Assert.Equal(0, index.LineCount);
    }

    [Fact]
    public void Build_FirstRowPerAudioBaseWins()
    {
        var index = GameVoiceTextIndex.Build(new[]
        {
            Row("TEXT_VOICEMAN_06006_000010_YSHTOLA", "Kept."),
            Row("TEXT_VOICEMAN_06006_000010_YSHTOLA", "Duplicate."),
        });

        Assert.Equal("Kept.", Assert.Single(index.LinesFor("yshtola")).Text);
    }

    [Fact]
    public void CleanText_CollapsesLineBreaks()
    {
        Assert.Equal("One two three", GameVoiceTextIndex.CleanText(" One\ntwo \r\n three "));
    }

    [Fact]
    public void LinesFor_UnknownToken_IsEmpty()
    {
        Assert.Empty(GameVoiceTextIndex.Empty.LinesFor("nobody"));
    }
}
