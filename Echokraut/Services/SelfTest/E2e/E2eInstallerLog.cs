using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Echokraut.Helper.Functional;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Reads <c>EchokrautLocalInstaller.log</c> (next to the installer exe under the install root, appended by
/// every installer start) for the E2E engine stage (plan › B3): the <c>Mode:</c> line each start writes
/// (<c>ttsBackend=… | xttsFp16=… | qwenTemperature=… | pocketDecodeSteps=… | pocketLayers=…</c>, from
/// <c>EchokrauTtsServeArgs.Describe</c>), the wrapper's NDJSON lines it relays verbatim, and mojibake.
/// Pure apart from the file read; nothing here goes into the report except the values it is asked about.
/// </summary>
public static class E2eInstallerLog
{
    public const string FileName = "EchokrautLocalInstaller.log";

    /// <summary>What a wrong decode leaves: UTF-8 read as CP1252/Latin-1 (<c>Ã¤</c>, <c>â€¦</c>) or U+FFFD.</summary>
    private static readonly string[] MojibakeMarks = ["Ã", "â€", "�"];

    public static string PathFor(string installRoot) =>
        Path.Combine(installRoot, LocalInstallerProvisioner.InstallerFolderName, FileName);

    /// <summary>File length in bytes, 0 when it does not exist (yet).</summary>
    public static long Length(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch (IOException) { return 0; }
    }

    /// <summary>Lines from byte <paramref name="offset"/> on; the installer may be appending meanwhile.</summary>
    public static IReadOnlyList<string> ReadLines(string path, long offset = 0)
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset > 0 && offset <= fs.Length) fs.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, new UTF8Encoding(false));
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (IOException) { return []; }
    }

    /// <summary>One <c>Mode: &lt;mode&gt; | key=value | …</c> line.</summary>
    public sealed record ModeLine(string Mode, IReadOnlyDictionary<string, string> Fields)
    {
        public string? Get(string key) => Fields.TryGetValue(key, out var v) ? v : null;
    }

    public static ModeLine? ParseMode(string line)
    {
        var at = line.IndexOf("Mode: ", StringComparison.Ordinal);
        if (at < 0) return null;
        var parts = line[(at + 6)..].Split(" | ");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in parts.Skip(1))
        {
            var eq = part.IndexOf('=');
            if (eq > 0) fields[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return new ModeLine(parts[0].Trim(), fields);
    }

    /// <summary>The Mode lines of the wrapper modes (start, install, update, custom-data restart).</summary>
    public static IReadOnlyList<ModeLine> WrapperModeLines(IEnumerable<string> lines) =>
        lines.Select(ParseMode).OfType<ModeLine>().Where(m => m.Fields.ContainsKey("ttsBackend")).ToList();

    /// <summary>First differing key as "key=actual, expected value", null when every expected value is there.</summary>
    public static string? Mismatch(ModeLine? mode, IReadOnlyDictionary<string, string> expect)
    {
        if (mode == null) return "no installer Mode line after the restart";
        foreach (var (key, value) in expect)
        {
            var actual = mode.Get(key);
            if (!string.Equals(actual, value, StringComparison.Ordinal))
                return $"installer log {key}={actual ?? "(missing)"}, expected {value}";
        }
        return null;
    }

    public static IReadOnlyList<string> Mojibake(IEnumerable<string> lines) =>
        lines.Where(l => MojibakeMarks.Any(m => l.Contains(m, StringComparison.Ordinal))).ToList();

    /// <summary>
    /// NDJSON events the installer relayed: how many, how many carry the i18n <c>key</c> field, and how many of
    /// those have a German letter in their message (the client language de must reach the wrapper texts).
    /// </summary>
    public sealed record NdjsonSummary(int Events, int Keyed, int KeyedWithUmlaut, string? SampleKey);

    public static NdjsonSummary Ndjson(IEnumerable<string> lines)
    {
        int events = 0, keyed = 0, umlaut = 0;
        string? sample = null;
        foreach (var line in lines)
        {
            var brace = line.IndexOf('{');
            if (brace < 0 || !line.TrimEnd().EndsWith('}')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line[brace..]);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out _)) continue;
                events++;
                if (!root.TryGetProperty("key", out var key) || key.ValueKind != JsonValueKind.String) continue;
                keyed++;
                sample ??= key.GetString();
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
                    && msg.GetString()!.IndexOfAny(['ä', 'ö', 'ü', 'ß', 'Ä', 'Ö', 'Ü']) >= 0)
                    umlaut++;
            }
            catch (JsonException) { /* a non-JSON line with braces */ }
        }
        return new NdjsonSummary(events, keyed, umlaut, sample);
    }

    /// <summary>
    /// The whole-log judgement of the run (part <c>files:installer-log</c>): no mojibake, no
    /// <c>qwenXvecOnly</c> (removed option), a wrapper Mode line per passed engine, NDJSON with <c>key</c>
    /// and, for a German client, German text. Returns the problems and a detail line.
    /// </summary>
    public static (IReadOnlyList<string> Problems, string Detail) Judge(IReadOnlyList<string> lines, IEnumerable<string> passedEngines)
    {
        var problems = new List<string>();
        var modes = WrapperModeLines(lines);
        var bad = Mojibake(lines);
        if (bad.Count > 0) problems.Add($"{bad.Count} mojibake line(s), first: {Clip(bad[0])}");
        if (modes.Any(m => m.Fields.ContainsKey("qwenXvecOnly")))
            problems.Add("a Mode line still carries qwenXvecOnly");
        var backends = modes.Select(m => m.Get("ttsBackend")).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var missing = passedEngines.Where(e => !backends.Contains(e, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0) problems.Add($"no Mode line with ttsBackend={string.Join("/", missing)}");

        var nd = Ndjson(lines);
        var language = modes.Select(m => m.Get("language")).FirstOrDefault(l => !string.IsNullOrEmpty(l));
        if (nd.Events == 0) problems.Add("no NDJSON event in the log");
        else if (nd.Keyed == 0) problems.Add($"{nd.Events} NDJSON events, none with a key field");
        if (language == "de" && nd.Keyed > 0 && nd.KeyedWithUmlaut == 0)
            problems.Add("language de, but no keyed NDJSON message has a German letter");

        var detail = $"{lines.Count} lines, {modes.Count} Mode lines (backends {string.Join("/", backends)}), " +
                     $"NDJSON {nd.Events} events / {nd.Keyed} with key (e.g. {nd.SampleKey ?? "-"}), language {language ?? "?"}" +
                     (language == "de" ? $", {nd.KeyedWithUmlaut} German message(s)" : ", text language not judged") +
                     $", mojibake {bad.Count}";
        return (problems, detail);
    }

    /// <summary>How many generations the wrapper started (<c>tts request start</c>, one per engine run, plan › B4).</summary>
    public static int TtsStarts(IEnumerable<string> lines) =>
        lines.Count(l => l.Contains("tts request start: job=", StringComparison.Ordinal));

    /// <summary>True when the wrapper logged <c>tts request done</c> for the generation job <paramref name="generationJob"/>.</summary>
    public static bool TtsDone(IEnumerable<string> lines, string generationJob) =>
        lines.Any(l => l.Contains($"tts request done: job={generationJob} ", StringComparison.Ordinal));

    /// <summary>
    /// The generation a client joined: <c>tts cache shared: job=&lt;client&gt; generation=&lt;id&gt;</c> (the wrapper logs
    /// it for <c>hit</c> and <c>shared</c>); null when the line is not there (yet).
    /// </summary>
    public static string? GenerationOf(IEnumerable<string> lines, string clientJob)
    {
        foreach (var line in lines)
        {
            var at = line.IndexOf($": job={clientJob} generation=", StringComparison.Ordinal);
            if (at < 0 || !line.Contains("tts cache ", StringComparison.Ordinal)) continue;
            var start = at + $": job={clientJob} generation=".Length;
            var end = start;
            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '-')) end++;
            if (end > start) return line[start..end];
        }
        return null;
    }

    private static string Clip(string line) => line.Length <= 160 ? line : line[..160] + "…";
}
