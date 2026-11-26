using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Dialog harvest (test plan section 8), small: only non-quest dialog (<see cref="QuestType.None"/>: DefaultTalk,
/// balloons; a filtered run is never offered to the community database). The event-quest filter found nothing in a
/// German client (run 20261125-220607). The harvest must finish and add voice clip rows for the client language.
/// </summary>
public sealed class HarvestScenario : E2eScenario
{
    internal const QuestType Filter = QuestType.None;

    private readonly IDialogHarvestService _harvest;
    private readonly IDatabaseService _db;
    private readonly Func<ClientLanguage> _language;

    public HarvestScenario(IDialogHarvestService harvest, IDatabaseService db, Func<ClientLanguage> language)
    {
        _harvest = harvest;
        _db = db;
        _language = language;
    }

    public override string Id => "harvest";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(15);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (_harvest.IsRunning) return SelfTestResult.Skip("A harvest is already running.");
        var language = _language();
        var before = _db.GetVoiceClipCountForLanguage((int)language);
        string? lastProgress = null;
        void OnProgress(string text) => lastProgress = text;
        _harvest.ProgressChanged += OnProgress;
        var started = DateTime.UtcNow;
        try
        {
            await _harvest.RunAsync(language, ct, (int)Filter).ConfigureAwait(false);
        }
        finally
        {
            _harvest.ProgressChanged -= OnProgress;
        }
        ct.ThrowIfCancellationRequested();
        var after = _db.GetVoiceClipCountForLanguage((int)language);
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        return after > before
            ? SelfTestResult.Pass($"Non-quest harvest ({language}) added {after - before} clip rows in {seconds:0} s.")
            : SelfTestResult.Fail($"Non-quest harvest ({language}) added no clip rows ({before} before, {after} after; last progress: {lastProgress ?? "none"}).");
    }
}
