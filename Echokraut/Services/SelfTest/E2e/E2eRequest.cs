using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// What <c>tools/e2e/start.sh</c> uploads as <c>pluginConfigs\Echokraut-e2e\request.json</c>
/// (plan <c>docs/plans/ingame-e2e.md</c> › Trigger and gate). <see cref="RemoteUrl"/> comes from a
/// file on the Mac that is in no repo; it never goes into the report or the log.
/// <see cref="TestWrapper"/>: <c>first-time</c> installs the developer's local wrapper zip
/// (<c>IEchokrauTtsInstanceService.TestWrapperZipPath</c>, pushed by <c>tools/e2e/push-test-wrapper.sh</c>) instead of the
/// published release — for testing a wrapper before it is released.
/// </summary>
public sealed record E2eRequest(
    string Id,
    string InstallRoot,
    IReadOnlyList<string> Engines,
    string? RemoteUrl,
    IReadOnlyList<string>? Stages,
    bool TestWrapper = false)
{
    /// <summary>Decision D1: every EchokrauTTS sub-engine (still filtered by catalog and wrapper version at run time).</summary>
    public static readonly IReadOnlyList<string> AllEngines = ["qwen-1.7b", "qwen-0.6b", "xtts", "f5", "pocket", "moss"];

    // The id ends up in file names (e2e-<id>.json) and in every log line the watcher greps.
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// True when the stage is part of this run (<see cref="Stages"/> null = all, except
    /// <paramref name="explicitOnly"/> stages, which must be listed).
    /// </summary>
    public bool Includes(string stageId, bool explicitOnly = false) =>
        Stages == null ? !explicitOnly : Stages.Contains(stageId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses and validates the request file. Never throws.</summary>
    public static (E2eRequest? Request, string? Error) Parse(string json)
    {
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, ReadOpts);
        }
        catch (JsonException ex)
        {
            return (null, $"request.json is not valid JSON: {ex.Message}");
        }
        if (dto == null) return (null, "request.json is empty.");

        if (string.IsNullOrWhiteSpace(dto.Id) || !IdPattern.IsMatch(dto.Id))
            return (null, "\"id\" is missing or has characters other than letters, digits, '-' and '_' (max 64).");
        if (string.IsNullOrWhiteSpace(dto.InstallRoot))
            return (null, "\"installRoot\" is missing.");

        var engines = dto.Engines is { Count: > 0 }
            ? dto.Engines.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : AllEngines.ToList();

        string? remote = null;
        if (!string.IsNullOrWhiteSpace(dto.RemoteUrl))
        {
            if (!Uri.TryCreate(dto.RemoteUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return (null, "\"remoteUrl\" must be an absolute http(s) URL.");
            remote = dto.RemoteUrl.Trim();
        }

        var stages = dto.Stages?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        return (new E2eRequest(dto.Id, dto.InstallRoot.Trim(), engines, remote, stages, dto.TestWrapper), null);
    }

    /// <summary>The request as it may appear in the report: without the remote URL.</summary>
    public E2eRequest ForReport() => this with { RemoteUrl = RemoteUrl == null ? null : "<remote>" };

    private sealed class Dto
    {
        public string? Id { get; set; }
        public string? InstallRoot { get; set; }
        public List<string>? Engines { get; set; }
        public string? RemoteUrl { get; set; }
        public List<string>? Stages { get; set; }
        public bool TestWrapper { get; set; }
    }
}
