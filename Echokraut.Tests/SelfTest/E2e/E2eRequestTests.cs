using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eRequestTests
{
    [Fact]
    public void Parse_Minimal_DefaultsToAllSixEnginesAndAllStages()
    {
        var (r, error) = E2eRequest.Parse("""{ "id": "20261122-1", "installRoot": "E:\\EkE2E" }""");

        Assert.Null(error);
        Assert.Equal("20261122-1", r!.Id);
        Assert.Equal(@"E:\EkE2E", r.InstallRoot);
        Assert.Equal(E2eRequest.AllEngines, r.Engines);
        Assert.Equal(6, r.Engines.Count);
        Assert.Null(r.Stages);
        Assert.True(r.Includes("anything"));
        Assert.False(r.Includes("restore", explicitOnly: true));
    }

    [Fact]
    public void Parse_Full_KeepsEnginesStagesAndRemote()
    {
        var (r, error) = E2eRequest.Parse("""
            { "id": "a_1", "installRoot": "E:\\EkE2E", "engines": ["xtts", "XTTS", " f5 "],
              "remoteUrl": "https://tts.example.net", "stages": ["preconditions", "checks"] }
            """);

        Assert.Null(error);
        Assert.Equal(new[] { "xtts", "f5" }, r!.Engines);
        Assert.Equal("https://tts.example.net", r.RemoteUrl);
        Assert.True(r.Includes("CHECKS"));
        Assert.False(r.Includes("reload"));
        Assert.True(r.Includes("CHECKS", explicitOnly: true));
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("null", "empty")]
    [InlineData("""{ "installRoot": "E:\\x" }""", "\"id\"")]
    [InlineData("""{ "id": "../evil", "installRoot": "E:\\x" }""", "\"id\"")]
    [InlineData("""{ "id": "ok" }""", "installRoot")]
    [InlineData("""{ "id": "ok", "installRoot": "E:\\x", "remoteUrl": "ftp://x" }""", "remoteUrl")]
    [InlineData("""{ "id": "ok", "installRoot": "E:\\x", "remoteUrl": "relative/path" }""", "remoteUrl")]
    public void Parse_Invalid_ReturnsReason(string json, string expectedInError)
    {
        var (r, error) = E2eRequest.Parse(json);

        Assert.Null(r);
        Assert.Contains(expectedInError, error);
    }

    [Fact]
    public void Parse_TestWrapper_DefaultsToFalse()
    {
        Assert.False(E2eRequest.Parse("""{ "id": "a", "installRoot": "E:\\EkE2E" }""").Request!.TestWrapper);
        Assert.True(E2eRequest.Parse("""{ "id": "a", "installRoot": "E:\\EkE2E", "testWrapper": true }""").Request!.TestWrapper);
    }

    [Fact]
    public void Parse_WrapperVersion_IsTrimmedAndAbsentByDefault()
    {
        Assert.Null(E2eRequest.Parse("""{ "id": "a", "installRoot": "E:\\EkE2E" }""").Request!.WrapperVersion);
        Assert.Equal("0.0.1.9", E2eRequest.Parse("""{ "id": "a", "installRoot": "E:\\EkE2E", "wrapperVersion": " 0.0.1.9 " }""").Request!.WrapperVersion);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("0.0.1.9/../x")]
    [InlineData("v0.0.1.9")]
    [InlineData("1")]
    public void Parse_WrapperVersion_NotATag_IsRejected(string tag)
    {
        var (r, error) = E2eRequest.Parse($$"""{ "id": "a", "installRoot": "E:\\EkE2E", "wrapperVersion": "{{tag}}" }""");

        Assert.Null(r);
        Assert.Equal("\"wrapperVersion\" must be a release tag like 0.0.1.9.", error);
    }

    [Fact]
    public void Parse_WrapperVersionWithTestWrapper_IsRejected()
    {
        var (r, error) = E2eRequest.Parse("""{ "id": "a", "installRoot": "E:\\EkE2E", "wrapperVersion": "0.0.1.9", "testWrapper": true }""");

        Assert.Null(r);
        Assert.Contains("exclude each other", error);
    }

    [Fact]
    public void ForReport_HidesTheRemoteUrl()
    {
        var (r, _) = E2eRequest.Parse("""{ "id": "x1", "installRoot": "E:\\EkE2E", "remoteUrl": "https://secret.example.net" }""");

        Assert.Equal("<remote>", r!.ForReport().RemoteUrl);
        Assert.Null((r with { RemoteUrl = null }).ForReport().RemoteUrl);
    }
}
