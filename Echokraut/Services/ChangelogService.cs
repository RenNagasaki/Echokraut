using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services;

public sealed class ChangelogService : IChangelogService
{
    // Resource name pattern: Echokraut.Resources.Changelogs.v{MAJOR}.{MINOR}.{BUILD}.{REVISION}_{LANG}.txt
    // The "v" prefix anchors the regex against the surrounding dotted resource path.
    private static readonly Regex ResourceNamePattern = new(
        @"^Echokraut\.Resources\.Changelogs\.v(\d+\.\d+\.\d+\.\d+)_(EN|DE)\.txt$",
        RegexOptions.Compiled);

    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IClientState _clientState;
    private readonly Assembly _assembly;

    /// <summary>Raw current version string passed at construction so tests can override it without touching Plugin.</summary>
    private readonly string _currentVersion;

    public ChangelogService(
        ILogService log,
        Configuration config,
        IClientState clientState,
        string currentVersion,
        Assembly? assembly = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _assembly = assembly ?? typeof(ChangelogService).Assembly;
    }

    public bool HasUnseenChangelogs() => GetUnseenChangelogs().Count > 0;

    public IReadOnlyList<ChangelogEntry> GetUnseenChangelogs()
    {
        var lastSeen = TryParseVersion(_config.LastSeenChangelogVersion);
        var current = TryParseVersion(_currentVersion);
        if (lastSeen == null || current == null)
        {
            _log.Warning(nameof(GetUnseenChangelogs),
                $"Cannot compare versions — last_seen='{_config.LastSeenChangelogVersion}' " +
                $"current='{_currentVersion}'. Showing nothing.",
                new EKEventId(0, TextSource.None));
            return Array.Empty<ChangelogEntry>();
        }

        var lang = PickLanguage(_clientState.ClientLanguage);
        // Fallback to EN if the user's language has no entry for this version. Computed
        // once, applied per-entry below.
        var fallbackLang = lang == "EN" ? null : "EN";

        // Group by version: prefer lang, else fallback. Resource enumeration is unordered;
        // we sort by parsed Version so the user reads them oldest → newest in the window.
        var byVersion = new Dictionary<Version, Dictionary<string, string>>();
        foreach (var name in _assembly.GetManifestResourceNames())
        {
            var match = ResourceNamePattern.Match(name);
            if (!match.Success) continue;

            var ver = TryParseVersion(match.Groups[1].Value);
            if (ver == null) continue;
            if (ver <= lastSeen || ver > current) continue;

            if (!byVersion.TryGetValue(ver, out var perLang))
            {
                perLang = new Dictionary<string, string>();
                byVersion[ver] = perLang;
            }
            perLang[match.Groups[2].Value] = ReadResource(name);
        }

        var result = byVersion
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var content = kv.Value.GetValueOrDefault(lang)
                    ?? (fallbackLang != null ? kv.Value.GetValueOrDefault(fallbackLang) : null)
                    ?? "(no localized changelog content available)";
                return new ChangelogEntry($"v{kv.Key.ToString(4)}", content);
            })
            .ToList();

        // Diagnostic: visible in /xllog when the popup looks broken (e.g. body empty).
        // Lets the user verify content actually loaded without re-instrumenting the build.
        if (result.Count > 0)
        {
            _log.Info(nameof(GetUnseenChangelogs),
                $"Found {result.Count} unseen changelog entr{(result.Count == 1 ? "y" : "ies")}: " +
                string.Join(", ", result.Select(e => $"{e.Version}({e.Content.Length}c)")),
                new EKEventId(0, TextSource.None));
        }
        return result;
    }

    public void MarkAllSeen()
    {
        _config.LastSeenChangelogVersion = _currentVersion;
        _config.Save();
        _log.Info(nameof(MarkAllSeen),
            $"Changelog marker bumped to {_currentVersion}",
            new EKEventId(0, TextSource.None));
    }

    /// <summary>
    /// Strips the optional leading "v" and parses the rest with <see cref="System.Version"/>.
    /// Returns null on any parse failure.
    /// </summary>
    internal static Version? TryParseVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Trim outer whitespace first so a value like " v1.2.3.4 " strips both the
        // surrounding spaces AND the leading "v"; doing TrimStart('v') on a leading-space
        // string is a no-op and the subsequent Trim() can't reach back into the original.
        var trimmed = raw.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var v) ? v : null;
    }

    /// <summary>
    /// Maps Dalamud's <see cref="ClientLanguage"/> to the changelog file suffix. EN/DE have
    /// localized content; FR and JA fall back to EN until those translations are added.
    /// </summary>
    internal static string PickLanguage(ClientLanguage lang) => lang switch
    {
        ClientLanguage.German => "DE",
        _ => "EN",
    };

    private string ReadResource(string name)
    {
        try
        {
            using var stream = _assembly.GetManifestResourceStream(name);
            if (stream == null) return $"(missing resource: {name})";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ReadResource),
                $"Failed to read changelog resource '{name}': {ex.Message}",
                new EKEventId(0, TextSource.None));
            return $"(failed to read: {name})";
        }
    }
}
