using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.E2e.Scenarios;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 6 (plan › Stages): real-pipeline scenarios, each reported on its own as
/// <c>scenarios:&lt;id&gt;</c>. Never on the real config [BLK-5]: it needs <c>checks:local</c> (and through
/// it the reset) and refuses unless state.json says the config is an E2E one. With the <c>engine</c> stage in
/// the run it needs the last passed <c>engine:*</c> part, and that engine active again [M4]; without it, the
/// engine the first-time install left running (<c>checks:local</c>).
/// Auto-advance is off for the stage [M2]; every option is restored after each scenario (safety net for a
/// scenario that threw) and at the end.
/// </summary>
public sealed class ScenariosStage : E2eStage
{
    public const string StageId = "scenarios";

    private readonly IReadOnlyList<E2eScenario> _scenarios;
    private readonly Configuration _config;

    public ScenariosStage(IReadOnlyList<E2eScenario> scenarios, Configuration config)
    {
        _scenarios = scenarios;
        _config = config;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => ["checks:local"];

    public override string? UnmetNeed(E2eState state)
    {
        if (FirstUnmet(state, Needs) is { } unmet) return unmet;
        if (!state.Request.Includes(EngineStage.StageId)) return null;
        if (EngineStage.LastPassed(state) is not { } last) return $"needs a passed {EngineStage.PartPrefix}* (none passed)";
        var id = last.Substring(EngineStage.PartPrefix.Length);
        return state.Marks.GetValueOrDefault(EngineStage.ActiveEngineMark) == id
            ? null
            : $"needs {last} active again (switch back failed)";
    }
    public override TimeSpan Timeout => _scenarios.Aggregate(TimeSpan.FromSeconds(30), (sum, s) => sum + s.Timeout);

    public static string PartId(E2eScenario scenario) => $"{StageId}:{scenario.Id}";

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");

        // Left over from a run that unloaded mid-scenario: back to the values before the stage.
        RestoreOptions(ctx);
        E2eOptions.Set(ctx.State, _config, nameof(Configuration.AutoAdvanceTextAfterSpeechCompleted), false);
        ctx.Save();
        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        try
        {
            foreach (var scenario in _scenarios)
            {
                var id = PartId(scenario);
                // A restarted stage keeps the parts it already reported.
                if (ctx.State.ResultOf(id) is { } done)
                {
                    outcomes.Add((id, done.Outcome));
                    continue;
                }
                var (result, ms) = await SelfTestRunner.RunWithTimeoutAsync(
                    c => scenario.RunAsync(ctx, c), scenario.Timeout, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                // Safety net: only the stage's own auto-advance switch stays touched between scenarios.
                E2eOptions.Restore(ctx.State, _config,
                    ctx.State.TouchedOptions.Keys.Where(k => k != nameof(Configuration.AutoAdvanceTextAfterSpeechCompleted)));
                _config.Save();
                ctx.ReportPart(new E2eStageReport(id, result.Outcome, result.Detail, ms));
                outcomes.Add((id, result.Outcome));
            }
        }
        finally
        {
            if (!ct.IsCancellationRequested) RestoreOptions(ctx);
        }

        var (summary, failed, passed) = SummarizeParts(outcomes);
        if (failed.Count > 0) return SelfTestResult.Fail($"{summary}: {string.Join(", ", failed)}.");
        return passed > 0 ? SelfTestResult.Pass(summary + ".") : SelfTestResult.Skip(summary + ".");
    }

    private void RestoreOptions(E2eContext ctx)
    {
        if (E2eOptions.Restore(ctx.State, _config).Count > 0) _config.Save();
        ctx.Save();
    }
}
