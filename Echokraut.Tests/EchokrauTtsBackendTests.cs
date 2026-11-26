using System;
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

    // ── Inline game-voice reference (plan game-voice-reference §2.4) ──

    [Fact]
    public void HealthResponse_ParsesInlineReferenceFlags()
    {
        var json = "{ \"status\": \"ok\", \"inline_reference\": true, \"asr_check\": true }";
        var parsed = JsonSerializer.Deserialize<EchokrauTtsHealthResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.True(parsed!.inline_reference);
        Assert.True(parsed.asr_check);
    }

    [Fact]
    public void HealthResponse_OlderWrapper_LeavesInlineReferenceNull()
    {
        var parsed = JsonSerializer.Deserialize<EchokrauTtsHealthResponse>("{ \"status\": \"ok\" }",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Null(parsed!.inline_reference);
    }

    [Fact]
    public void EncodePcm16Base64_IsLittleEndian()
    {
        var b64 = EchokrauTtsBackend.EncodePcm16Base64(new short[] { 1, -2, 0x1234 });
        Assert.Equal(new byte[] { 0x01, 0x00, 0xFE, 0xFF, 0x34, 0x12 }, Convert.FromBase64String(b64));
    }

    [Fact]
    public void BuildTtsPayload_WithoutReference_SendsSampleOnly()
    {
        using var doc = JsonDocument.Parse(EchokrauTtsBackend.BuildTtsPayload("Hello.", "Male_All_NPC001.wav", null));
        Assert.Equal("Male_All_NPC001.wav", doc.RootElement.GetProperty("sample").GetString());
        Assert.Equal("Hello.", doc.RootElement.GetProperty("text").GetString());
        Assert.False(doc.RootElement.TryGetProperty("ref_audio", out _));
    }

    [Fact]
    public void BuildTtsPayload_WithReference_ReplacesSample()
    {
        var reference = new InlineVoiceReference(new short[] { 5, 6 }, "Game text.", new[] { "vo_voiceman_06006_000010" });
        using var doc = JsonDocument.Parse(EchokrauTtsBackend.BuildTtsPayload("Hello.", "Male_All_NPC001.wav", reference));
        var root = doc.RootElement;
        // Exactly one of sample/ref_audio — the wrapper answers 400 otherwise.
        Assert.False(root.TryGetProperty("sample", out _));
        Assert.Equal("Hello.", root.GetProperty("text").GetString());
        Assert.Equal("Game text.", root.GetProperty("ref_text").GetString());
        Assert.Equal(new byte[] { 5, 0, 6, 0 }, Convert.FromBase64String(root.GetProperty("ref_audio").GetString()!));
        Assert.Equal("vo_voiceman_06006_000010", root.GetProperty("ref_ids")[0].GetString());
    }

    [Fact]
    public void BuildTtsPayload_NoCacheOnlyWhenAsked()
    {
        using var plain = JsonDocument.Parse(EchokrauTtsBackend.BuildTtsPayload("Hello.", "Male_All_NPC001.wav", null));
        Assert.False(plain.RootElement.TryGetProperty("no_cache", out _));

        using var fresh = JsonDocument.Parse(EchokrauTtsBackend.BuildTtsPayload("Hello.", "Male_All_NPC001.wav", null, noCache: true));
        Assert.True(fresh.RootElement.GetProperty("no_cache").GetBoolean());
        Assert.Equal("Male_All_NPC001.wav", fresh.RootElement.GetProperty("sample").GetString());

        var reference = new InlineVoiceReference(new short[] { 1 }, "Game text.", new[] { "a" });
        using var inline = JsonDocument.Parse(EchokrauTtsBackend.BuildTtsPayload("Hello.", "x.wav", reference, noCache: true));
        Assert.True(inline.RootElement.GetProperty("no_cache").GetBoolean());
        Assert.False(inline.RootElement.TryGetProperty("sample", out _));
    }

    [Fact]
    public void InlineVoiceReference_ToString_HidesTextAndAudio()
    {
        var reference = new InlineVoiceReference(new short[24000], "secret line", new[] { "a" });
        Assert.DoesNotContain("secret", reference.ToString());
        Assert.Equal(1.0, reference.Seconds, 3);
    }
}
