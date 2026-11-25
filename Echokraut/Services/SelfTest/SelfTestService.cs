using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Localization;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest;

/// <summary>
/// In-game self-test (plan <c>docs/plans/ingame-self-test.md</c>). The same checks run in Dalamud's
/// self-test window (registered by <c>Plugin</c>) and window-less via <c>/ektest</c>, which writes
/// a JSON report — the entry point for an automated run over ssh.
/// </summary>
public interface ISelfTestService
{
    /// <summary>All checks in run order: automatic first, interactive last.</summary>
    IReadOnlyList<SelfTestCheck> Checks { get; }

    bool IsRunning { get; }

    /// <summary>Where the reports land (<c>selftest-latest.json</c> + one file per run).</summary>
    string ReportDirectory { get; }

    /// <summary>
    /// Runs the automatic checks (and the interactive ones when <paramref name="includeInteractive"/>),
    /// prints the result to chat and writes the report. Null when a run is already going.
    /// </summary>
    Task<SelfTestReport?> RunAsync(bool includeInteractive, CancellationToken ct = default);

    /// <summary>Logs one finished check (also used by the Dalamud window steps).</summary>
    void LogEntry(SelfTestReportEntry entry);
}

public sealed class SelfTestService : ISelfTestService
{
    internal const string LatestFileName = "selftest-latest.json";
    internal const int KeptReports = 20;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ICommandService _commands;
    private readonly ILogService _log;
    private readonly Func<string> _language;
    private readonly Func<DateTime> _utcNow;
    private readonly ISelfTestRunGate _gate;
    internal const string GateOwner = "self-test";

    public SelfTestService(IReadOnlyList<SelfTestCheck> checks, ICommandService commands, ILogService log,
        string reportDirectory, Func<string> language, ISelfTestRunGate gate, Func<DateTime>? utcNow = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        Checks = checks.OrderBy(c => c.Interactive).ToList(); // stable: keeps the order within each group
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        ReportDirectory = reportDirectory;
        _language = language;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<SelfTestCheck> Checks { get; }
    public bool IsRunning => _gate.Owner == GateOwner;
    public string ReportDirectory { get; }

    public async Task<SelfTestReport?> RunAsync(bool includeInteractive, CancellationToken ct = default)
    {
        if (!_gate.TryEnter(GateOwner))
        {
            _commands.PrintText("", _gate.Owner == GateOwner
                ? Loc.S("The self-test is already running.")
                : Loc.S("An E2E run is going; the self-test waits until it has finished."));
            return null;
        }
        try
        {
            // Chat lines stay ASCII: the game font has no arrows or check marks.
            _commands.PrintText("", Loc.S("Self-test started."));
            var started = _utcNow();
            var entries = new List<SelfTestReportEntry>();
            foreach (var check in Checks.Where(c => includeInteractive || !c.Interactive))
            {
                if (check.Instruction is { } instruction)
                    _commands.PrintText("", $"{check.Name}: {instruction}");
                var entry = await SelfTestRunner.RunCheckAsync(check, ct).ConfigureAwait(false);
                entries.Add(entry);
                LogEntry(entry);
                if (entry.Outcome == SelfTestOutcome.Fail)
                    _commands.PrintText("", $"FAIL {entry.Name}: {entry.Detail}");
            }

            var report = new SelfTestReport(Plugin.PluginVersion, _language(), started, _utcNow(),
                includeInteractive, entries);
            var path = WriteReport(report);
            _commands.PrintText("", Summary(report) + (path != null ? $" Report: {path}" : ""));
            return report;
        }
        finally
        {
            _gate.Exit(GateOwner);
        }
    }

    public void LogEntry(SelfTestReportEntry entry)
    {
        var eventId = new EKEventId(0, TextSource.None);
        var text = $"Self-test {entry.Id}: {entry.Outcome} ({entry.DurationMs} ms) — {entry.Detail}";
        if (entry.Outcome == SelfTestOutcome.Fail) _log.Warning(nameof(SelfTestService), text, eventId);
        else _log.Info(nameof(SelfTestService), text, eventId);
    }

    /// <summary>One chat line: counts per outcome.</summary>
    internal static string Summary(SelfTestReport report) =>
        $"Self-test: {report.Passed} passed, {report.Failed} failed, {report.Skipped} skipped.";

    internal static string Serialize(SelfTestReport report) => JsonSerializer.Serialize(report, JsonOpts);

    /// <summary>Writes the run file and <see cref="LatestFileName"/>, keeps the newest <see cref="KeptReports"/>. Null on failure (logged).</summary>
    internal string? WriteReport(SelfTestReport report)
    {
        try
        {
            Directory.CreateDirectory(ReportDirectory);
            var json = Serialize(report);
            var path = Path.Combine(ReportDirectory, $"selftest-{report.StartedUtc:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, json);
            File.WriteAllText(Path.Combine(ReportDirectory, LatestFileName), json);

            foreach (var old in Directory.GetFiles(ReportDirectory, "selftest-*.json")
                         .Where(f => Path.GetFileName(f) != LatestFileName)
                         .OrderByDescending(f => f, StringComparer.Ordinal)
                         .Skip(KeptReports))
                File.Delete(old);
            return path;
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(WriteReport), $"Self-test report not written: {ex.Message}", new EKEventId(0, TextSource.None));
            return null;
        }
    }
}
