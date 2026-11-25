using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eLogFormatTests
{
    private const string Remote = "https://Tts.Example.net/";

    [Fact]
    public void Stage_HasTheGreppedShape()
    {
        Assert.Equal("EKTEST r1 checks start", E2eLogFormat.Stage("r1", "checks", "start", "", null));
        Assert.Equal("EKTEST r1 checks progress backend pass", E2eLogFormat.Stage("r1", "checks", "progress", "backend pass", null));
    }

    [Theory]
    [InlineData(SelfTestOutcome.Pass, "pass")]
    [InlineData(SelfTestOutcome.Fail, "fail")]
    [InlineData(SelfTestOutcome.Skipped, "skip")]
    public void Outcome_UsesLowerCaseKinds(SelfTestOutcome outcome, string kind)
    {
        Assert.Equal($"EKTEST r1 s {kind} d", E2eLogFormat.Outcome("r1", "s", outcome, "d", null));
    }

    [Fact]
    public void Done_CountsAndPath()
    {
        Assert.Equal(@"EKTEST r1 done 3/1/2 C:\x\e2e-r1.json", E2eLogFormat.Done("r1", 3, 1, 2, @"C:\x\e2e-r1.json"));
    }

    [Fact]
    public void Stage_IsOneLine()
    {
        Assert.DoesNotContain("\n", E2eLogFormat.Stage("r1", "s", "fail", "a\r\nb\nc", null));
    }

    [Fact]
    public void Scrub_ReplacesUrlAndHostCaseInsensitively()
    {
        var text = "POST https://tts.example.net/health failed; host TTS.EXAMPLE.NET not reachable";

        var scrubbed = E2eLogFormat.Scrub(text, Remote);

        Assert.DoesNotContain("example", scrubbed, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<remote>/health", scrubbed);
    }

    [Fact]
    public void Stage_ScrubsTheDetail()
    {
        Assert.Equal("EKTEST r1 remote fail <remote> down", E2eLogFormat.Stage("r1", "remote", "fail", "tts.example.net down", Remote));
    }

    [Fact]
    public void Scrub_WithoutRemote_LeavesTextAlone()
    {
        Assert.Equal("tts.example.net", E2eLogFormat.Scrub("tts.example.net", null));
    }
}
