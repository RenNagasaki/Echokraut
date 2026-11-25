using System;
using System.Collections.Generic;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Where an E2E run stands (plan › Run state across reloads). The order matters: later phases
/// compare with <c>&gt;=</c> (e.g. cleanup touches config and DB only from <see cref="ResetDone"/> on).
/// </summary>
public enum E2ePhase
{
    Requested,
    ResetPending,
    BackupDone,
    WipeStarted,
    ResetDone,
    Stages,
    /// <summary>Restore phase A asked for the reload; the next ctor puts the backup back.</summary>
    RestorePending,
    /// <summary>The ctor began wiping the E2E config; a failed restore resumes from here on the next load.</summary>
    RestoreStarted,
    /// <summary>The real config is back (<see cref="E2eState.ConfigIsE2e"/> false).</summary>
    RestoreDone,
    Finished,
    Aborted,
}

/// <summary>Outcome of one stage, as it lands in state.json and the report.</summary>
public sealed record E2eStageReport(string Id, SelfTestOutcome Outcome, string Detail, long DurationMs);

/// <summary>
/// <c>Echokraut-e2e\state.json</c>: everything a run needs to resume after a plugin reload. Written
/// atomically after every stage and sub-step (<see cref="E2eStateStore"/>).
/// </summary>
public sealed class E2eState
{
    public string Id { get; set; } = "";
    public E2eRequest Request { get; set; } = null!;
    public E2ePhase Phase { get; set; } = E2ePhase.Requested;
    public DateTime StartedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    /// <summary>The stage that was running at the last write. A resumed run restarts it, never counts it.</summary>
    public string? RunningStage { get; set; }

    public List<E2eStageReport> StageResults { get; set; } = new();
    public string? BackupDir { get; set; }

    /// <summary>Character ids, clip row ids and WAV paths a stage created; cleanup removes exactly these.</summary>
    public List<string> CreatedIds { get; set; } = new();

    /// <summary>Old value per config option a stage changed; cleanup puts them back.</summary>
    public Dictionary<string, string> TouchedOptions { get; set; } = new();

    /// <summary>Small per-stage notes that must survive a reload (e.g. when the reload was requested).</summary>
    public Dictionary<string, string> Marks { get; set; } = new();

    /// <summary>
    /// The plugin runs on a config the E2E reset produced. Carried over into the next request so its
    /// preconditions refuse to back that config up as if it were the real one (restore.ps1 deletes
    /// state.json, which clears it).
    /// </summary>
    public bool ConfigIsE2e { get; set; }

    public bool IsOpen => Phase is not (E2ePhase.Finished or E2ePhase.Aborted);

    public E2eStageReport? ResultOf(string stageId) => StageResults.Find(r => r.Id == stageId);
}

/// <summary><c>Echokraut-e2e\e2e-&lt;id&gt;.json</c>. Never contains the remote URL or host.</summary>
public sealed record E2eReport(
    string Id,
    string PluginVersion,
    string Language,
    DateTime StartedUtc,
    DateTime FinishedUtc,
    E2ePhase Phase,
    E2eRequest Request,
    IReadOnlyList<E2eStageReport> Stages,
    string? BackupDir)
{
    public int Passed => Stages.Count(SelfTestOutcome.Pass);
    public int Failed => Stages.Count(SelfTestOutcome.Fail);
    public int Skipped => Stages.Count(SelfTestOutcome.Skipped);
}

internal static class E2eStageReportExtensions
{
    public static int Count(this IReadOnlyList<E2eStageReport> stages, SelfTestOutcome outcome)
    {
        var n = 0;
        foreach (var s in stages) if (s.Outcome == outcome) n++;
        return n;
    }
}
