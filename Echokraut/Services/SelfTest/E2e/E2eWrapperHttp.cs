using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Backend;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>The wrapper's <c>/health</c> › <c>response_cache</c> counters (echokrautts <c>ResponseCache.snapshot</c>).</summary>
public sealed record E2eCacheStats(bool Enabled, int Entries, int Running, int Hits, int Shared, int Misses, int Bypassed)
{
    /// <summary>Counter change from <paramref name="before"/> to this snapshot.</summary>
    public E2eCacheStats Minus(E2eCacheStats before) =>
        new(Enabled, Entries - before.Entries, Running - before.Running, Hits - before.Hits,
            Shared - before.Shared, Misses - before.Misses, Bypassed - before.Bypassed);

    public override string ToString() =>
        $"hits {Hits}, shared {Shared}, misses {Misses}, bypassed {Bypassed}";
}

/// <summary>The parts of <c>/health</c> the stage <c>wrapper-http</c> reads; <see cref="Cache"/> is null before wrapper 0.0.2.0.</summary>
public sealed record E2eWrapperHealth(string? Device, string? Backend, E2eCacheStats? Cache);

/// <summary>
/// One <c>POST /tts</c> in flight: the wrapper's answer headers (<c>X-Cache</c>, <c>X-Job-Id</c>) and the body,
/// read by <see cref="ReadToEndAsync"/> (or in the background, see the cancel part of <c>wrapper-http</c>).
/// </summary>
public interface IE2eTtsCall : IDisposable
{
    /// <summary><c>X-Cache</c>: <c>miss</c>, <c>hit</c>, <c>shared</c>, <c>bypass</c> or <c>off</c>; empty when the header is missing.</summary>
    string Cache { get; }

    /// <summary><c>X-Job-Id</c>: this client's own job (cancel detaches only this client).</summary>
    string JobId { get; }

    /// <summary>Bytes read so far.</summary>
    long BytesRead { get; }

    /// <summary>Milliseconds from sending the request to the first body bytes; null before they came.</summary>
    long? FirstByteMs { get; }

    /// <summary>Reads the body to its end; returns its SHA-256 (hex) to compare two answers.</summary>
    Task<string> ReadToEndAsync(CancellationToken ct);
}

/// <summary>The plain HTTP side of the running EchokrauTTS wrapper, for the stage <c>wrapper-http</c> (plan › B4).</summary>
public interface IE2eWrapperHttp
{
    Task<E2eWrapperHealth?> HealthAsync(CancellationToken ct);

    /// <summary>Sends <c>POST /tts</c> and returns once the headers came; null when the wrapper refused it.</summary>
    Task<IE2eTtsCall?> StartTtsAsync(string sample, string text, bool noCache, CancellationToken ct);

    Task<bool> CancelAsync(string jobId, CancellationToken ct);

    /// <summary><c>GET /jobs/{id}</c> › <c>state</c> (<c>queued</c>, <c>running</c>, <c>done</c>, <c>cancelled</c>, <c>error</c>); null when unknown.</summary>
    Task<string?> JobStateAsync(string jobId, CancellationToken ct);

    /// <summary>The built-in test page (<c>GET /</c>); null when it is not served.</summary>
    Task<string?> PageAsync(CancellationToken ct);
}

/// <summary>
/// <see cref="IE2eWrapperHttp"/> against <see cref="Configuration.EchokrauTts"/> (base URL, API key): the same
/// request body as the plugin (<see cref="EchokrauTtsBackend.BuildTtsPayload"/>). The URL never goes into a
/// message (it may be a remote one).
/// </summary>
public sealed class EchokrauTtsWrapperHttp : IE2eWrapperHttp
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly Configuration _config;
    private readonly HttpClient _http;

    public EchokrauTtsWrapperHttp(Configuration config, HttpClient? http = null)
    {
        _config = config;
        // A generation may run for a minute; the stage's own timeouts bound it.
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    private string Url(string path) => _config.EchokrauTts.BaseUrl.TrimEnd('/') + path;

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, Url(path));
        var key = _config.EchokrauTts.ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        return req;
    }

    public async Task<E2eWrapperHealth?> HealthAsync(CancellationToken ct)
    {
        try
        {
            using var req = Request(HttpMethod.Get, _config.EchokrauTts.HealthPath);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            return ParseHealth(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    internal static E2eWrapperHealth? ParseHealth(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            E2eCacheStats? cache = null;
            if (root.TryGetProperty("response_cache", out var rc) && rc.ValueKind == JsonValueKind.Object)
                cache = new E2eCacheStats(
                    rc.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True,
                    Int(rc, "entries"), Int(rc, "running"), Int(rc, "hits"), Int(rc, "shared"),
                    Int(rc, "misses"), Int(rc, "bypassed"));
            return new E2eWrapperHealth(Str(root, "device"), Str(root, "backend"), cache);
        }
        catch (JsonException) { return null; }
    }

    private static int Int(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<IE2eTtsCall?> StartTtsAsync(string sample, string text, bool noCache, CancellationToken ct)
    {
        var req = Request(HttpMethod.Post, _config.EchokrauTts.TtsPath);
        req.Content = new StringContent(EchokrauTtsBackend.BuildTtsPayload(text, sample, null, noCache), Encoding.UTF8, "application/json");
        var sent = Environment.TickCount64;
        HttpResponseMessage res;
        try
        {
            res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            req.Dispose();
            return null;
        }
        if (!res.IsSuccessStatusCode)
        {
            res.Dispose();
            req.Dispose();
            return null;
        }
        var body = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new Call(req, res, body, Header(res, "X-Cache"), Header(res, "X-Job-Id"), sent);
    }

    private static string Header(HttpResponseMessage res, string name) =>
        res.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault() ?? "" : "";

    public async Task<bool> CancelAsync(string jobId, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Post, _config.EchokrauTts.CancelPath + jobId);
        try
        {
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
    }

    public async Task<string?> JobStateAsync(string jobId, CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, "/jobs/" + jobId);
        try
        {
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return Str(doc.RootElement, "state");
        }
        catch (HttpRequestException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task<string?> PageAsync(CancellationToken ct)
    {
        using var req = Request(HttpMethod.Get, "/");
        try
        {
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
        }
        catch (HttpRequestException) { return null; }
    }

    private sealed class Call : IE2eTtsCall
    {
        private readonly HttpRequestMessage _req;
        private readonly HttpResponseMessage _res;
        private readonly Stream _body;
        private readonly long _sent;
        private long _bytes;
        private long _firstByteMs = -1;

        public Call(HttpRequestMessage req, HttpResponseMessage res, Stream body, string cache, string jobId, long sent)
        {
            _req = req;
            _res = res;
            _body = body;
            Cache = cache;
            JobId = jobId;
            _sent = sent;
        }

        public string Cache { get; }
        public string JobId { get; }
        public long BytesRead => Interlocked.Read(ref _bytes);
        public long? FirstByteMs => Interlocked.Read(ref _firstByteMs) is var v && v >= 0 ? v : null;

        public async Task<string> ReadToEndAsync(CancellationToken ct)
        {
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[16 * 1024];
            try
            {
                while (true)
                {
                    var n = await _body.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n == 0) break;
                    if (Interlocked.Read(ref _bytes) == 0)
                        Interlocked.CompareExchange(ref _firstByteMs, Environment.TickCount64 - _sent, -1);
                    sha.AppendData(buffer, 0, n);
                    Interlocked.Add(ref _bytes, n);
                }
            }
            catch (IOException) { /* the wrapper closed the stream (cancelled client) */ }
            catch (HttpRequestException) { /* same */ }
            return Convert.ToHexString(sha.GetHashAndReset());
        }

        public void Dispose()
        {
            _body.Dispose();
            _res.Dispose();
            _req.Dispose();
        }
    }
}

/// <summary>New lines of the wrapper's log since a mark: the installer log relays its NDJSON (plan › B3).</summary>
public interface IE2eWrapperLog
{
    long Mark();
    IReadOnlyList<string> Since(long mark);
}

/// <summary><see cref="IE2eWrapperLog"/> over <c>EchokrautLocalInstaller.log</c> under the run's install root.</summary>
public sealed class E2eInstallerWrapperLog : IE2eWrapperLog
{
    private readonly string _path;

    public E2eInstallerWrapperLog(string installRoot) => _path = E2eInstallerLog.PathFor(installRoot);

    private string Path => _path;
    public long Mark() => E2eInstallerLog.Length(Path);
    public IReadOnlyList<string> Since(long mark) => E2eInstallerLog.ReadLines(Path, mark);
}
