using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>What a stage gets from the run: the request, the persisted state and two callbacks.</summary>
public sealed class E2eContext
{
    private readonly Action _save;
    private readonly Action<string> _progress;
    private readonly Action<bool> _suppressSaveOnDispose;
    private readonly Action<E2eStageReport> _report;

    public E2eContext(E2eState state, Action save, Action<string> progress, Action<bool>? suppressSaveOnDispose = null,
        Action<E2eStageReport>? report = null)
    {
        State = state;
        _save = save;
        _progress = progress;
        _suppressSaveOnDispose = suppressSaveOnDispose ?? (_ => { });
        _report = report ?? (r => { State.StageResults.Add(r); _save(); });
    }

    public E2eState State { get; }
    public E2eRequest Request => State.Request;

    /// <summary>Writes state.json now (after a sub-step that must survive a reload).</summary>
    public void Save() => _save();

    /// <summary>Logs <c>EKTEST &lt;id&gt; &lt;stage&gt; progress &lt;text&gt;</c>.</summary>
    public void Progress(string text) => _progress(text);

    /// <summary>
    /// While true the plugin's Dispose does not save the config (backup phase A [H6]): the file on disk
    /// is what phase B backs up, nothing a stage changed in memory may land in it.
    /// </summary>
    public void SuppressSaveOnDispose(bool value) => _suppressSaveOnDispose(value);

    /// <summary>
    /// Reports one part of a stage as a result of its own (<c>scenarios:continuation</c> …): it lands in
    /// <see cref="E2eState.StageResults"/> and the report, is saved at once and gets its own
    /// <c>EKTEST … pass|fail|skip</c> line. A restarted stage skips the parts it already reported.
    /// </summary>
    public void ReportPart(E2eStageReport part) => _report(part);
}

/// <summary>
/// One step of the E2E run (plan › Stages). Stage bodies run on the thread pool; only node/ATK work and
/// object-table reads hop to the framework thread, and blocking calls (StopInstance, RefreshBackend)
/// never run there.
/// </summary>
public abstract class E2eStage
{
    /// <summary>Stable id for log, state and report (never renamed).</summary>
    public abstract string Id { get; }

    /// <summary>Stages that must have passed; otherwise this one is skipped with "needs …".</summary>
    public virtual IReadOnlyList<string> Needs => [];

    public virtual TimeSpan Timeout => TimeSpan.FromMinutes(2);

    /// <summary>Any outcome other than Pass ends the run at once (preconditions, backup, reset).</summary>
    public virtual bool AbortsRunUnlessPass => false;

    /// <summary>Runs only when the request lists it by id; a request without a stage list leaves it out (restore).</summary>
    public virtual bool ExplicitOnly => false;

    /// <summary>Still runs after a stage ended the run (restore: a half-done run must not keep the E2E config).</summary>
    public virtual bool RunsAfterAbort => false;

    /// <summary>Part of every run, whatever the request lists (cleanup).</summary>
    public virtual bool AlwaysRuns => false;

    /// <summary>True when this stage belongs to a run of <paramref name="request"/>.</summary>
    public bool IsIn(E2eRequest request) => AlwaysRuns || request.Includes(Id, ExplicitOnly);

    /// <summary>
    /// "needs X (outcome)" when a dependency is not met, null when the stage may run. Default: every
    /// stage in <see cref="Needs"/> passed. A stage whose dependency is decided at run time (scenarios →
    /// the last passed <c>engine:*</c> part) overrides it.
    /// </summary>
    public virtual string? UnmetNeed(E2eState state) => FirstUnmet(state, Needs);

    /// <summary>"needs X (outcome)" for the first of <paramref name="needs"/> that did not pass.</summary>
    protected static string? FirstUnmet(E2eState state, IEnumerable<string> needs)
    {
        foreach (var need in needs)
        {
            var r = state.ResultOf(need);
            if (r == null) return $"needs {need} (not run)";
            if (r.Outcome != SelfTestOutcome.Pass) return $"needs {need} ({E2eLogFormat.Kind(r.Outcome)})";
        }
        return null;
    }

    /// <summary>"x passed, y failed, z skipped" over the parts a stage reported, plus the failed part ids.</summary>
    protected static (string Summary, IReadOnlyList<string> Failed, int Passed) SummarizeParts(
        IReadOnlyCollection<(string Id, SelfTestOutcome Outcome)> parts)
    {
        var failed = parts.Where(o => o.Outcome == SelfTestOutcome.Fail).Select(o => o.Id).ToList();
        var passed = parts.Count(o => o.Outcome == SelfTestOutcome.Pass);
        return ($"{passed} passed, {failed.Count} failed, {parts.Count - passed - failed.Count} skipped", failed, passed);
    }

    public abstract Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct);
}
