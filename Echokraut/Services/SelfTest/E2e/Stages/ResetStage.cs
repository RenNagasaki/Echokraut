using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 2 (plan › Stages). The wipe itself is phase B (<see cref="E2eStartupReset"/>, before the
/// config was loaded); this stage checks the result on the plugin instance that loaded after it: a
/// brand-new config (<c>FirstTime</c>) and an empty DB. On pass the run moves to
/// <see cref="E2ePhase.Stages"/>.
/// </summary>
public sealed class ResetStage : E2eStage
{
    public const string StageId = "reset";

    private readonly Configuration _config;
    private readonly IDatabaseService _db;

    public ResetStage(Configuration config, IDatabaseService db)
    {
        _config = config;
        _db = db;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [BackupStage.StageId];
    public override TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public override bool AbortsRunUnlessPass => true;

    public override Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var s = ctx.State;
        if (s.Marks.TryGetValue(E2eStartupReset.ResetErrorMark, out var error))
            return Task.FromResult(SelfTestResult.Fail($"Wipe did not finish (backup in {s.BackupDir}): {error}"));
        if (s.Phase < E2ePhase.ResetDone)
            return Task.FromResult(SelfTestResult.Fail($"The startup reset did not finish (phase {s.Phase})."));

        var problems = new List<string>();
        if (!_config.FirstTime) problems.Add("config is not fresh (FirstTime is false)");
        var characters = _db.GetAllCharacters().Count;
        if (characters > 0) problems.Add($"DB has {characters} characters");
        var clips = _db.GetVoiceClipCount();
        if (clips > 0) problems.Add($"DB has {clips} voice clips");
        if (problems.Count > 0)
            return Task.FromResult(SelfTestResult.Fail(string.Join("; ", problems) + "."));

        s.Phase = E2ePhase.Stages;
        ctx.Save();
        return Task.FromResult(SelfTestResult.Pass("Fresh config (FirstTime), DB has 0 characters / 0 voice clips."));
    }
}
