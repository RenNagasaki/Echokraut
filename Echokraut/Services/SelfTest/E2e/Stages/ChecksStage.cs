using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Runs every non-interactive self-test check (the same instances as <c>/ektest</c>) and reports each
/// one as a progress line. Pass when none fails.
/// </summary>
public sealed class ChecksStage : E2eStage
{
    private readonly IReadOnlyList<SelfTestCheck> _checks;
    private readonly IReadOnlyList<string> _needs;

    public ChecksStage(string id, IReadOnlyList<SelfTestCheck> checks, IReadOnlyList<string> needs)
    {
        Id = id;
        _checks = checks.Where(c => !c.Interactive).ToList();
        _needs = needs;
    }

    public override string Id { get; }
    public override IReadOnlyList<string> Needs => _needs;

    // Each check carries its own timeout; the stage only needs room for all of them.
    public override TimeSpan Timeout => _checks.Aggregate(TimeSpan.FromSeconds(30), (sum, c) => sum + c.Timeout);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var entries = new List<SelfTestReportEntry>();
        foreach (var check in _checks)
        {
            var entry = await SelfTestRunner.RunCheckAsync(check, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            entries.Add(entry);
            ctx.Progress($"{entry.Id} {E2eLogFormat.Kind(entry.Outcome)} ({entry.DurationMs} ms) {entry.Detail}");
        }

        var failed = entries.Where(e => e.Outcome == SelfTestOutcome.Fail).Select(e => e.Id).ToList();
        var summary = $"{entries.Count(e => e.Outcome == SelfTestOutcome.Pass)} passed, {failed.Count} failed, " +
                      $"{entries.Count(e => e.Outcome == SelfTestOutcome.Skipped)} skipped";
        return failed.Count == 0
            ? SelfTestResult.Pass(summary + ".")
            : SelfTestResult.Fail($"{summary}: {string.Join(", ", failed)}.");
    }
}
