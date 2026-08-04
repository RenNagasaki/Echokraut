using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Unit tests for the deterministic, HTTP-free parts of <see cref="EchokrauTtsBackend"/>:
/// URL composition, health→"Ready" mapping, and response DTO parsing.
/// </summary>
public class EchokrauTtsBackendTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8765", "/tts", "http://127.0.0.1:8765/tts")]
    [InlineData("http://127.0.0.1:8765/", "/health", "http://127.0.0.1:8765/health")]
    [InlineData("http://host:8765/", "/cancel/abc", "http://host:8765/cancel/abc")]
    public void BuildUrl_CollapsesDoubleSlash(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, EchokrauTtsBackend.BuildUrl(baseUrl, path));
    }

    [Fact]
    public void HealthToReady_OkStatus_ReturnsLiteralReady()
    {
        // Must be exactly "Ready" (case-insensitive token the First-Time connection test matches).
        Assert.Equal("Ready", EchokrauTtsBackend.HealthToReady(new EchokrauTtsHealthResponse { status = "ok" }, "{}"));
        Assert.Equal("Ready", EchokrauTtsBackend.HealthToReady(new EchokrauTtsHealthResponse { status = "OK" }, "{}"));
    }

    [Fact]
    public void HealthToReady_NonOk_ReturnsNotReady()
    {
        var r = EchokrauTtsBackend.HealthToReady(new EchokrauTtsHealthResponse { status = "loading" }, "{}");
        Assert.StartsWith("Not ready", r);
        Assert.Contains("loading", r);
    }

    [Fact]
    public void HealthToReady_NullOrEmptyStatus_FallsBackToRawBody()
    {
        var r = EchokrauTtsBackend.HealthToReady(null, "service unavailable");
        Assert.StartsWith("Not ready", r);
        Assert.Contains("service unavailable", r);
    }

    [Fact]
    public void SamplesResponse_ParsesAndKeepsExtension()
    {
        var json = "{ \"samples\": [ \"Female_Hyur_Iceheart.wav\", \"Male_Elezen_Alphinaud.wav\" ] }";
        var parsed = JsonSerializer.Deserialize<EchokrauTtsSamplesResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.samples.Count);
        // Extension preserved so BackendVoice keys match AllTalk's convention.
        Assert.Contains("Female_Hyur_Iceheart.wav", parsed.samples);
    }

    [Fact]
    public async Task MaterializeAudioStream_NonStreaming_BuffersFullBodyAtPositionZero()
    {
        // Streaming OFF must fully read the response before returning, so playback only starts
        // once the whole clip is generated (the reported bug: EchokrauTTS never waited).
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var source = new MemoryStream(payload);

        var result = await EchokrauTtsBackend.MaterializeAudioStream(source, streaming: false);

        Assert.True(result.CanSeek);
        Assert.Equal(0, result.Position);
        var buf = new byte[payload.Length];
        var read = await result.ReadAsync(buf);
        Assert.Equal(payload.Length, read);
        Assert.Equal(payload, buf);
        // The source network stream is consumed + disposed once buffered.
        Assert.False(source.CanRead);
    }

    [Fact]
    public async Task MaterializeAudioStream_Streaming_ReturnsReadableStream()
    {
        var source = new MemoryStream(new byte[] { 9, 8, 7 });
        var result = await EchokrauTtsBackend.MaterializeAudioStream(source, streaming: true);
        Assert.NotNull(result);
        Assert.True(result.CanRead);
    }

    [Fact]
    public void HealthResponse_Parses()
    {
        var json = "{ \"status\": \"ok\", \"backend\": \"cuda\", \"device\": \"cuda:0\", \"language\": \"de\" }";
        var parsed = JsonSerializer.Deserialize<EchokrauTtsHealthResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(parsed);
        Assert.Equal("ok", parsed!.status);
        Assert.Equal("cuda", parsed.backend);
        Assert.Equal("de", parsed.language);
    }
}
