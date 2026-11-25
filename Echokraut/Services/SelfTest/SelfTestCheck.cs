using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest;

/// <summary>How one self-test check ended.</summary>
public enum SelfTestOutcome
{
    Pass,
    Fail,
    /// <summary>Not applicable in this setup (feature off, None mode, opt-in missing). Never a failure.</summary>
    Skipped,
}

/// <summary>Result of one check: outcome plus a diagnostic detail (English, goes to log and report).</summary>
public sealed record SelfTestResult(SelfTestOutcome Outcome, string Detail)
{
    public static SelfTestResult Pass(string detail) => new(SelfTestOutcome.Pass, detail);
    public static SelfTestResult Fail(string detail) => new(SelfTestOutcome.Fail, detail);
    public static SelfTestResult Skip(string detail) => new(SelfTestOutcome.Skipped, detail);
}

/// <summary>
/// One in-game check of the plugin's self-test (plan <c>docs/plans/ingame-self-test.md</c>).
/// Checks are plain async code without any Dalamud UI dependency, so the same instance runs in
/// Dalamud's self-test window (<see cref="DalamudSelfTestStep"/>) and in the window-less
/// <c>/ektest</c> run that writes a JSON report.
/// </summary>
public abstract class SelfTestCheck
{
    /// <summary>Stable id for the report (never localized, never renamed).</summary>
    public abstract string Id { get; }

    /// <summary>Localized display name.</summary>
    public abstract string Name { get; }

    /// <summary>Needs the player to do something (talk to an NPC, write in /say). Not part of the automatic run.</summary>
    public virtual bool Interactive => false;

    /// <summary>Localized instruction for interactive checks, shown in the window and printed to chat.</summary>
    public virtual string? Instruction => null;

    /// <summary>The check fails when it has not finished after this long.</summary>
    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(30);

    /// <summary>Runs the check. Must clean up anything it created, also when cancelled.</summary>
    public abstract Task<SelfTestResult> RunAsync(CancellationToken ct);
}

/// <summary>One line of the report.</summary>
public sealed record SelfTestReportEntry(string Id, string Name, SelfTestOutcome Outcome, string Detail, long DurationMs);

/// <summary>Everything one <c>/ektest</c> run found.</summary>
public sealed record SelfTestReport(
    string PluginVersion,
    string Language,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    bool IncludedInteractive,
    IReadOnlyList<SelfTestReportEntry> Entries)
{
    public int Passed => Count(SelfTestOutcome.Pass);
    public int Failed => Count(SelfTestOutcome.Fail);
    public int Skipped => Count(SelfTestOutcome.Skipped);

    private int Count(SelfTestOutcome outcome)
    {
        var n = 0;
        foreach (var e in Entries) if (e.Outcome == outcome) n++;
        return n;
    }
}

/// <summary>Runs one check with its timeout; turns exceptions and timeouts into a failed entry.</summary>
public static class SelfTestRunner
{
    public static async Task<SelfTestReportEntry> RunCheckAsync(SelfTestCheck check, CancellationToken ct)
    {
        var (result, durationMs) = await RunWithTimeoutAsync(check.RunAsync, check.Timeout, ct).ConfigureAwait(false);
        return new SelfTestReportEntry(check.Id, check.Name, result.Outcome, result.Detail, durationMs);
    }

    /// <summary>
    /// Runs <paramref name="run"/> with a timeout. Exceptions, the timeout and cancellation through
    /// <paramref name="ct"/> become a failed result — it never throws. Shared by checks and E2E stages.
    /// </summary>
    public static async Task<(SelfTestResult Result, long DurationMs)> RunWithTimeoutAsync(
        Func<CancellationToken, Task<SelfTestResult>> run, TimeSpan timeout, CancellationToken ct)
    {
        var started = Environment.TickCount64;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        SelfTestResult result;
        try
        {
            result = await run(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result = SelfTestResult.Fail($"Timed out after {timeout.TotalSeconds:0} s.");
        }
        catch (OperationCanceledException)
        {
            result = SelfTestResult.Fail("Cancelled.");
        }
        catch (Exception ex)
        {
            result = SelfTestResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
        return (result, Environment.TickCount64 - started);
    }
}
