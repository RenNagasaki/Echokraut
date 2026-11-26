using System;
using System.IO;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eInstallerLogTests
{
    private const string Mode =
        "[2026-11-25 10:00:00.000] Mode: echokrautts | installRoot=E:\\EkE2E | url=(none) | port=7851 | language=de | parentPid=1 " +
        "| ttsBackend=qwen-0.6b | xttsFp16=true | qwenTemperature=0.7 | pocketDecodeSteps=4 | pocketLayers=24 " +
        "| responseCacheMinutes= | preserveUserData=False | isWindows=True";

    private const string Keyed =
        "[2026-11-25 10:00:01.000] {\"event\":\"progress\",\"step\":\"deps\",\"message\":\"Abhängigkeiten\",\"key\":\"step.deps_installed\"}";

    [Fact]
    public void ParseMode_ReadsModeAndFields()
    {
        var m = E2eInstallerLog.ParseMode(Mode)!;
        Assert.Equal("echokrautts", m.Mode);
        Assert.Equal("qwen-0.6b", m.Get("ttsBackend"));
        Assert.Equal("0.7", m.Get("qwenTemperature"));
        Assert.Equal("", m.Get("responseCacheMinutes"));
        Assert.Null(m.Get("qwenXvecOnly"));
        Assert.Null(E2eInstallerLog.ParseMode("[x] EchokrauTTS is ready"));
    }

    [Fact]
    public void WrapperModeLines_SkipAllTalkModes()
    {
        var lines = new[] { "[x] Mode: start | installFolder=E:\\a | isWindows=True", Mode };
        Assert.Single(E2eInstallerLog.WrapperModeLines(lines));
    }

    [Fact]
    public void Mismatch_NamesTheFirstWrongKey()
    {
        var m = E2eInstallerLog.ParseMode(Mode);
        Assert.Null(E2eInstallerLog.Mismatch(m, new System.Collections.Generic.Dictionary<string, string> { ["qwenTemperature"] = "0.7" }));
        Assert.Equal("installer log pocketLayers=24, expected 6",
            E2eInstallerLog.Mismatch(m, new System.Collections.Generic.Dictionary<string, string> { ["pocketLayers"] = "6" }));
        Assert.Contains("no installer Mode line", E2eInstallerLog.Mismatch(null, new System.Collections.Generic.Dictionary<string, string>()));
    }

    [Fact]
    public void Mojibake_FindsLatin1DecodedUtf8AndReplacementChars()
    {
        var bad = E2eInstallerLog.Mojibake(["Abhängigkeiten", "AbhÃ¤ngigkeiten", "â€¦", "x\uFFFDy"]);
        Assert.Equal(3, bad.Count);
    }

    [Fact]
    public void Ndjson_CountsEventsKeysAndGermanText()
    {
        var nd = E2eInstallerLog.Ndjson([Keyed, "[x] {\"event\":\"ready\",\"port\":7851}", "[x] {not json}", "[x] plain"]);
        Assert.Equal(2, nd.Events);
        Assert.Equal(1, nd.Keyed);
        Assert.Equal(1, nd.KeyedWithUmlaut);
        Assert.Equal("step.deps_installed", nd.SampleKey);
    }

    [Fact]
    public void Judge_PassesAGoodLog()
    {
        var (problems, detail) = E2eInstallerLog.Judge([Mode, Keyed], ["qwen-0.6b"]);
        Assert.Empty(problems);
        Assert.Contains("language de, 1 German message(s)", detail);
    }

    [Fact]
    public void Judge_FlagsXvecMissingEngineNoKeyAndEnglishText()
    {
        var english = Keyed.Replace("Abhängigkeiten", "Dependencies");
        var (problems, _) = E2eInstallerLog.Judge([Mode + " | qwenXvecOnly=false", english], ["qwen-0.6b", "pocket"]);
        Assert.Contains(problems, p => p.Contains("qwenXvecOnly"));
        Assert.Contains(problems, p => p.Contains("ttsBackend=pocket"));
        Assert.Contains(problems, p => p.Contains("no keyed NDJSON message has a German letter"));

        var (noKey, _) = E2eInstallerLog.Judge([Mode, "[x] {\"event\":\"ready\"}"], []);
        Assert.Contains(noKey, p => p.Contains("none with a key field"));
    }

    [Fact]
    public void ReadLines_FromAnOffset_ReturnsOnlyNewLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "ek-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            Assert.Equal(0, E2eInstallerLog.Length(path));
            Assert.Empty(E2eInstallerLog.ReadLines(path));
            File.WriteAllText(path, "old\n");
            var offset = E2eInstallerLog.Length(path);
            File.AppendAllText(path, "Abhängig\n");
            Assert.Equal(["Abhängig"], E2eInstallerLog.ReadLines(path, offset));
        }
        finally { File.Delete(path); }
    }

    // Wrapper lines as the installer relays them (plan › B4).
    private const string Start1 = "[2026-11-25 10:00:01.000] {\"event\": \"log\", \"level\": \"info\", \"message\": \"tts request start: job=aa-1 sample=Narrator.wav file=Narrator.wav lang=de chars=40 chunks=1\"}";
    private const string Shared = "[2026-11-25 10:00:01.100] {\"event\": \"log\", \"level\": \"info\", \"message\": \"tts cache shared: job=bb-2 generation=aa-1\"}";
    private const string Done1 = "[2026-11-25 10:00:03.000] {\"event\": \"log\", \"level\": \"info\", \"message\": \"tts request done: job=aa-1 generated=2.00s audio=4.00s rtf=0.50 first=0.40s parts=9\"}";

    [Fact]
    public void TtsStarts_CountsOnlyGenerationStarts()
    {
        Assert.Equal(1, E2eInstallerLog.TtsStarts([Start1, Shared, Done1, "tts request starting soon"]));
        Assert.Equal(0, E2eInstallerLog.TtsStarts([]));
    }

    [Fact]
    public void GenerationOf_FindsTheJoinedGeneration_OfExactlyThatClient()
    {
        Assert.Equal("aa-1", E2eInstallerLog.GenerationOf([Start1, Shared], "bb-2"));
        Assert.Null(E2eInstallerLog.GenerationOf([Start1, Shared], "bb"));   // prefix of another client id
        Assert.Null(E2eInstallerLog.GenerationOf([Start1], "aa-1"));         // the miss logs no join line
    }

    [Fact]
    public void TtsDone_MatchesTheWholeJobId()
    {
        Assert.True(E2eInstallerLog.TtsDone([Done1], "aa-1"));
        Assert.False(E2eInstallerLog.TtsDone([Done1], "aa"));
        Assert.False(E2eInstallerLog.TtsDone([Start1], "aa-1"));
    }
}
