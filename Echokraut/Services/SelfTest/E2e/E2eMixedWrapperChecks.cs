using System;
using System.Collections.Generic;
using System.Linq;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Judgements of the stage <c>wrapper-mixed</c> (plan › B8): which engines an older wrapper must block, whether the
/// dropdown entries say so, and which release the installer really fetched. Pure, unit-tested.
/// </summary>
public static class E2eMixedWrapperChecks
{
    /// <summary>
    /// Catalog engines whose <c>minVersion</c> is above <paramref name="installed"/>, in catalog order. Compared as
    /// numbers here (not through <c>WrapperUpdatePolicy</c>, the rule under test); an unparseable version blocks nothing.
    /// </summary>
    public static IReadOnlyList<TtsEngineInfo> Blocked(IEnumerable<TtsEngineInfo> catalog, string installed)
    {
        if (!Version.TryParse(installed, out var have)) return [];
        return catalog.Where(e => Version.TryParse(e.MinVersion, out var need) && need > have).ToList();
    }

    /// <summary>
    /// Every blocked engine's entry is unavailable and ends in the localized reason with its <c>minVersion</c>
    /// (<paramref name="unavailableFormat"/> = <c>"{0} (from wrapper version {1})"</c>, localized); every other entry is
    /// available and carries no such reason.
    /// </summary>
    public static IReadOnlyList<string> EntryProblems(IReadOnlyList<TtsEngineOption> options,
        IReadOnlyCollection<TtsEngineInfo> blocked, string unavailableFormat)
    {
        var problems = new List<string>();
        var reasonStart = ReasonStart(unavailableFormat);
        foreach (var option in options)
        {
            var engine = blocked.FirstOrDefault(b => string.Equals(b.Id, option.Id, StringComparison.OrdinalIgnoreCase));
            if (engine == null)
            {
                if (!option.Available) problems.Add($"{option.Id} blocked although the wrapper ships it");
                else if (reasonStart.Length > 0 && option.Label.Contains(reasonStart, StringComparison.Ordinal))
                    problems.Add($"{option.Id} available but labelled '{option.Label}'");
                continue;
            }
            if (option.Available) problems.Add($"{option.Id} offered although it needs wrapper {engine.MinVersion}");
            var reason = Reason(unavailableFormat, engine.MinVersion);
            if (!option.Label.EndsWith(reason, StringComparison.Ordinal))
                problems.Add($"{option.Id} label '{option.Label}' does not end in '{reason}'");
        }
        foreach (var missing in blocked.Where(b => options.All(o => !string.Equals(o.Id, b.Id, StringComparison.OrdinalIgnoreCase))))
            problems.Add($"{missing.Id} not listed (the Backend tab lists blocked engines)");
        return problems;
    }

    /// <summary>The part of the pattern after <c>{0}</c> with the version filled in, e.g. <c>" (from wrapper version 0.0.2.0)"</c>.</summary>
    public static string Reason(string unavailableFormat, string minVersion)
    {
        var at = unavailableFormat.IndexOf("{0}", StringComparison.Ordinal);
        var tail = at < 0 ? unavailableFormat : unavailableFormat[(at + 3)..];
        return tail.Replace("{1}", minVersion, StringComparison.Ordinal);
    }

    private static string ReasonStart(string unavailableFormat)
    {
        var tail = Reason(unavailableFormat, "\u0001");
        var cut = tail.IndexOf('\u0001');
        return (cut < 0 ? tail : tail[..cut]).Trim();
    }

    /// <summary>
    /// Null when the last installer <c>Mode:</c> line of <paramref name="mode"/> that downloads something names a URL of
    /// release <paramref name="tag"/> (<c>…/download/&lt;tag&gt;/…</c>), else what was found instead.
    /// </summary>
    public static string? DownloadProblem(IEnumerable<E2eInstallerLog.ModeLine> modes, string mode, string tag)
    {
        var last = modes.LastOrDefault(m => m.Mode == mode && m.Get("url") is { } u && u != "(none)" && u.Length > 0);
        if (last == null) return $"no installer Mode line '{mode}' with a download URL";
        var url = last.Get("url")!;
        return url.Contains($"/{tag}/", StringComparison.Ordinal) ? null : $"installer '{mode}' fetched {url}, not release {tag}";
    }
}
