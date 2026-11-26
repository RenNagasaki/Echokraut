using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Build step B3 (plan › Coverage › Build plan): the engine options a user changes in the Backend tab, set
/// through the same <see cref="IEchokrauTtsInstanceService"/> calls the UI makes, each judged by the restart,
/// the ready wait and the installer's <c>Mode:</c> line; plus the files the first-time install left behind.
/// Parts: <c>options:qwen-temperature</c>, <c>options:pocket-layers</c> (right after the engine passed, while it
/// is the running one), <c>files:voice-pack</c> (before the loop), <c>files:installer-log</c> (after it).
/// The options are nested in <see cref="Configuration.EchokrauTts"/>, which <see cref="E2eOptions"/> cannot
/// address: the original values go into a mark instead and are put back at the end of the part, or at the next
/// stage start when a reload cut the part off.
/// </summary>
public sealed partial class EngineStage
{
    public const string QwenTemperaturePart = "options:qwen-temperature";
    public const string PocketLayersPart = "options:pocket-layers";
    public const string VoicePackPart = "files:voice-pack";
    public const string InstallerLogPart = "files:installer-log";

    internal const string QwenOriginalMark = "options-qwen-original";
    internal const string PocketOriginalMark = "options-pocket-original";

    /// <summary>Option restarts on top of the engine loop: qwen 2, pocket 4, each ≤ one ready wait.</summary>
    internal TimeSpan OptionsTimeout => PerEngine * 2;

    private static string Arg(double v) => v.ToString("0.0##", CultureInfo.InvariantCulture);
    private static string Arg(int v) => v.ToString(CultureInfo.InvariantCulture);

    private void ReportOnce(E2eContext ctx, List<(string Id, SelfTestOutcome Outcome)> outcomes, string id,
        Func<SelfTestResult> check)
    {
        var result = ctx.State.ResultOf(id) is null ? check() : null;
        if (result != null) ctx.ReportPart(new E2eStageReport(id, result.Outcome, result.Detail, 0));
        outcomes.Add((id, ctx.State.ResultOf(id)!.Outcome));
    }

    private void CheckVoicePack(E2eContext ctx, List<(string Id, SelfTestOutcome Outcome)> outcomes) =>
        ReportOnce(ctx, outcomes, VoicePackPart, () =>
        {
            var (problems, detail) = E2eVoicePackFiles.Check(TtsPaths.EchokrauTtsSamples(ctx.Request.InstallRoot));
            return FromProblems(problems, detail);
        });

    private void CheckInstallerLog(E2eContext ctx, List<(string Id, SelfTestOutcome Outcome)> outcomes) =>
        ReportOnce(ctx, outcomes, InstallerLogPart, () =>
        {
            var lines = E2eInstallerLog.ReadLines(E2eInstallerLog.PathFor(ctx.Request.InstallRoot));
            if (lines.Count == 0) return SelfTestResult.Fail("installer log missing or empty");
            var passed = ctx.State.StageResults
                .Where(r => IsPassedEngine(r.Id, r.Outcome)).Select(r => r.Id[PartPrefix.Length..]);
            var (problems, detail) = E2eInstallerLog.Judge(lines, passed);
            return FromProblems(problems, detail);
        });

    /// <summary>
    /// A reload cut an option part off: its changes are put back from the mark (config only — the next switch
    /// restarts the engine with them) and the part is reported as skipped.
    /// </summary>
    private void RestoreInterruptedOptions(E2eContext ctx)
    {
        foreach (var (mark, part) in new[] { (QwenOriginalMark, QwenTemperaturePart), (PocketOriginalMark, PocketLayersPart) })
        {
            if (PutBackFromMark(ctx, mark) is not { } restored) continue;
            if (ctx.State.ResultOf(part) == null)
                ctx.ReportPart(new E2eStageReport(part, SelfTestOutcome.Skipped, $"Interrupted by a reload; {restored}.", 0));
        }
        _config.Save();
        ctx.Save();
    }

    /// <summary>Writes the original values of <paramref name="mark"/> back into the config and drops the mark;
    /// what was put back, null when there was no mark.</summary>
    private string? PutBackFromMark(E2eContext ctx, string mark)
    {
        if (!ctx.State.Marks.Remove(mark, out var value)) return null;
        var data = _config.EchokrauTts;
        if (mark == QwenOriginalMark)
        {
            data.QwenTemperature = double.Parse(value, CultureInfo.InvariantCulture);
            return $"temperature put back to {value}";
        }
        var v = value.Split('|').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        (data.PocketLayers, data.PocketDecodeSteps24, data.PocketDecodeSteps6) = (v[0], v[1], v[2]);
        return $"pocket put back to {v[0]} layers, steps {v[1]}/{v[2]}";
    }

    /// <summary>The option part for an engine that just passed and is running; null when it has none.</summary>
    private async Task<(string Id, SelfTestResult Result, long Ms)?> RunOptionsAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        string part;
        Func<CancellationToken, Task<SelfTestResult>> run;
        if (id.StartsWith("qwen-", StringComparison.Ordinal) && ctx.State.ResultOf(QwenTemperaturePart) == null)
            (part, run) = (QwenTemperaturePart, c => QwenTemperatureAsync(ctx, id, c));
        else if (id == "pocket" && ctx.State.ResultOf(PocketLayersPart) == null)
            (part, run) = (PocketLayersPart, c => PocketLayersAsync(ctx, id, c));
        else return null;

        var (result, ms) = await SelfTestRunner.RunWithTimeoutAsync(run, OptionsTimeout, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var freezes = ctx.State.Telemetry.Freezes()
            .Where(o => o.Op.StartsWith("option ", StringComparison.Ordinal) && o.Op.EndsWith(":" + id, StringComparison.Ordinal))
            .Select(o => $"{o.Op} froze the game ({o.Frames.MaxMs:0} ms frame)").ToList();
        if (freezes.Count > 0) result = SelfTestResult.Fail($"{string.Join("; ", freezes)} | {result.Detail}");
        ctx.ReportPart(new E2eStageReport(part, result.Outcome, result.Detail, ms));
        return (part, result, ms);
    }

    /// <summary>
    /// Test plan R2 "Qwen calmer emphasis": the checkbox's call (<c>SetQwenTemperature</c>) restarts the engine,
    /// the installer gets <c>qwenTemperature=0.7</c> and the engine still speaks; then back to the original.
    /// </summary>
    private async Task<SelfTestResult> QwenTemperatureAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        var data = _config.EchokrauTts;
        var original = data.QwenTemperature;
        ctx.State.Marks[QwenOriginalMark] = Arg(original);
        ctx.Save();
        var target = Math.Abs(original - EchokrauTtsData.CalmQwenTemperature) < 0.001
            ? EchokrauTtsData.DefaultQwenTemperature
            : EchokrauTtsData.CalmQwenTemperature;

        var notes = new List<string>();
        var error = await ApplyOptionAsync(ctx, id, $"temperature {Arg(target)}", () => _engine.SetQwenTemperature(target),
                        Expect(id, ("qwenTemperature", Arg(target))), generate: true, notes, ct).ConfigureAwait(false)
                    ?? await ApplyOptionAsync(ctx, id, $"temperature {Arg(original)}", () => _engine.SetQwenTemperature(original),
                        Expect(id, ("qwenTemperature", Arg(original))), generate: false, notes, ct).ConfigureAwait(false);
        return await FinishAsync(ctx, id, QwenOriginalMark, error, notes, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Test plan R1/R2 Pocket: own decode steps on 24 layers, switch to 6 layers (own steps, first bytes of the
    /// small model), back to 24 (its steps kept), then the original steps. Same calls as the two dropdowns.
    /// </summary>
    private async Task<SelfTestResult> PocketLayersAsync(E2eContext ctx, string id, CancellationToken ct)
    {
        var data = _config.EchokrauTts;
        if (data.PocketLayersEffective != EchokrauTtsData.DefaultPocketLayers)
            return SelfTestResult.Skip($"Pocket starts on {data.PocketLayersEffective} layers, the sequence needs 24.");
        var (steps24, steps6) = (data.PocketDecodeSteps24, data.PocketDecodeSteps6);
        ctx.State.Marks[PocketOriginalMark] = $"{data.PocketLayers}|{steps24}|{steps6}";
        ctx.Save();
        var own24 = data.PocketDecodeSteps == 8 ? 4 : 8;
        var own6 = Array.IndexOf(EchokrauTtsData.PocketStepOptionsFor(6), steps6) >= 0 ? steps6 : EchokrauTtsData.DefaultPocketDecodeSteps;

        var notes = new List<string>();
        var error = await ApplyOptionAsync(ctx, id, $"steps {own24}", () => _engine.SetPocketDecodeSteps(own24),
                        Expect(id, ("pocketLayers", "24"), ("pocketDecodeSteps", Arg(own24))), generate: false, notes, ct).ConfigureAwait(false)
                    ?? await ApplyOptionAsync(ctx, id, "layers 6", () => _engine.SetPocketLayers(6),
                        Expect(id, ("pocketLayers", "6"), ("pocketDecodeSteps", Arg(own6))), generate: true, notes, ct).ConfigureAwait(false)
                    ?? await ApplyOptionAsync(ctx, id, "layers 24", () => _engine.SetPocketLayers(24),
                        Expect(id, ("pocketLayers", "24"), ("pocketDecodeSteps", Arg(own24))), generate: true, notes, ct).ConfigureAwait(false)
                    ?? await ApplyOptionAsync(ctx, id, $"steps {data.PocketDecodeSteps} back to {steps24}", () => _engine.SetPocketDecodeSteps(steps24),
                        Expect(id, ("pocketLayers", "24"), ("pocketDecodeSteps", Arg(steps24))), generate: false, notes, ct).ConfigureAwait(false);
        return await FinishAsync(ctx, id, PocketOriginalMark, error, notes, ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string> Expect(string id, params (string Key, string Value)[] values)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal) { ["ttsBackend"] = id };
        foreach (var (k, v) in values) d[k] = v;
        return d;
    }

    /// <summary>
    /// Ends an option part. On success the sequence already ended on the original values; after a failed step
    /// they are written back and the engine restarted once, so the next engines (and <c>scenarios</c>) do not run
    /// on a changed option.
    /// </summary>
    private async Task<SelfTestResult> FinishAsync(E2eContext ctx, string id, string mark, string? error, List<string> notes,
        CancellationToken ct)
    {
        if (error == null) ctx.State.Marks.Remove(mark);
        else if (PutBackFromMark(ctx, mark) is { } restored)
        {
            _config.Save();
            if (_engine.InstanceRunning || _engine.InstanceStarting)
            {
                await MeasuredAsync(ctx, $"option put-back:{id}", () =>
                {
                    _engine.StopInstance(new EKEventId(0, TextSource.Backend));
                    _engine.StartInstance();
                }).ConfigureAwait(false);
                var ready = await _wait.WaitForReadyAsync(ctx, $"{id} put-back", _readyWait, ct).ConfigureAwait(false);
                restored += ready == null ? ", restarted" : $", restart failed: {ready}";
            }
            notes.Add(restored);
        }
        ctx.Save();
        var detail = string.Join("; ", notes);
        return error == null ? SelfTestResult.Pass(detail) : SelfTestResult.Fail(notes.Count == 0 ? error : $"{error} | {detail}");
    }

    /// <summary>
    /// One option change as the UI makes it: the call (persist + restart, measured as op
    /// <c>option &lt;label&gt;:&lt;id&gt;</c>), the ready wait, then the new installer Mode line must carry
    /// <paramref name="expect"/>; with <paramref name="generate"/> the generation check runs on the new args.
    /// Null on success, else the error; a note per passed step goes into <paramref name="notes"/>.
    /// </summary>
    private async Task<string?> ApplyOptionAsync(E2eContext ctx, string id, string label, Action change,
        IReadOnlyDictionary<string, string> expect, bool generate, List<string> notes, CancellationToken ct)
    {
        var log = E2eInstallerLog.PathFor(ctx.Request.InstallRoot);
        var offset = E2eInstallerLog.Length(log);
        var started = DateTime.UtcNow;
        await MeasuredAsync(ctx, $"option {label}:{id}", change).ConfigureAwait(false);
        if (await _wait.WaitForReadyAsync(ctx, $"{id} {label}", _readyWait, ct).ConfigureAwait(false) is { } error)
            return $"{label}: {error}";
        var mode = E2eInstallerLog.WrapperModeLines(E2eInstallerLog.ReadLines(log, offset)).LastOrDefault();
        if (E2eInstallerLog.Mismatch(mode, expect) is { } mismatch) return $"{label}: {mismatch}";

        var note = $"{label}: restart + ready {(DateTime.UtcNow - started).TotalSeconds:0} s, log " +
                   string.Join(" ", expect.Where(e => e.Key != "ttsBackend").Select(e => $"{e.Key}={e.Value}"));
        if (generate)
        {
            var gen = await SelfTestRunner.RunCheckAsync(_generation!, ct).ConfigureAwait(false);
            if (gen.Outcome != SelfTestOutcome.Pass)
                return $"{label}: generation {E2eLogFormat.Kind(gen.Outcome)} {gen.Detail}";
            note += $", generation {gen.DurationMs} ms ({gen.Detail})";
        }
        notes.Add(note);
        ctx.Progress($"{id}: {note}");
        return null;
    }
}
