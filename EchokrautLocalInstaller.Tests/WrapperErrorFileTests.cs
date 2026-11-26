using Newtonsoft.Json.Linq;
using Xunit;

namespace EchokrautLocalInstaller.Tests;

public sealed class WrapperErrorFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ek-errorfile-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JObject ReadFile() => JObject.Parse(File.ReadAllText(Path.Join(_dir, "Error.EchokrauTTS.json")));

    [Fact]
    public void FileName_IsTheOneThePluginReads() => Assert.Equal("Error.EchokrauTTS.json", Constants.ECHOKRAUTTSERRORFILE);

    [Fact]
    public void WrapperErrorEvent_WritesKeyAndLocalizedMessage()
    {
        // Verbatim shape of ndjson.error(i18n.t("boot.unsupported_language", …), fatal=True).
        const string line = "{\"event\": \"error\", \"message\": \"Pocket TTS hat kein Modell für Sprache ja (verfügbar: de, en). Wähle eine andere Engine oder Sprache.\", \"fatal\": true, \"ts\": \"2026-11-26T10:24:48.123+00:00\", \"key\": \"boot.unsupported_language\"}";

        Assert.True(WrapperErrorFile.RecordIfError(_dir, line));

        var file = ReadFile();
        Assert.Equal("boot.unsupported_language", (string?)file["key"]);
        Assert.Equal("Pocket TTS hat kein Modell für Sprache ja (verfügbar: de, en). Wähle eine andere Engine oder Sprache.",
            (string?)file["message"]);
    }

    [Fact]
    public void ErrorWithoutKey_IsWrittenWithAnEmptyKey()
    {
        Assert.True(WrapperErrorFile.RecordIfError(_dir, "{\"event\": \"error\", \"message\": \"boom\", \"fatal\": true}"));

        Assert.Equal(string.Empty, (string?)ReadFile()["key"]);
        Assert.Equal("boom", (string?)ReadFile()["message"]);
    }

    [Theory]
    [InlineData("{\"event\": \"ready\", \"host\": \"127.0.0.1\", \"port\": 7851}")]
    [InlineData("{\"event\": \"log\", \"level\": \"error\", \"message\": \"failed to rebuild worker\"}")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("{not json")]
    public void OtherLines_WriteNothing(string line)
    {
        Assert.False(WrapperErrorFile.RecordIfError(_dir, line));
        Assert.False(File.Exists(Path.Join(_dir, Constants.ECHOKRAUTTSERRORFILE)));
    }

    [Fact]
    public void LaterError_ReplacesTheEarlierOne()
    {
        WrapperErrorFile.RecordIfError(_dir, "{\"event\": \"error\", \"message\": \"first\", \"key\": \"a\"}");
        WrapperErrorFile.RecordIfError(_dir, "{\"event\": \"error\", \"message\": \"second\", \"key\": \"b\"}");

        Assert.Equal("second", (string?)ReadFile()["message"]);
        Assert.Equal("b", (string?)ReadFile()["key"]);
    }

    [Fact]
    public void Clear_RemovesTheFile_AndIsQuietWithoutOne()
    {
        WrapperErrorFile.Write(_dir, "", "installer failed");

        WrapperErrorFile.Clear(_dir);
        WrapperErrorFile.Clear(_dir);

        Assert.False(File.Exists(Path.Join(_dir, Constants.ECHOKRAUTTSERRORFILE)));
    }
}
