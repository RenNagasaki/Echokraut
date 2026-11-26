using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Narrator voice (test plan R5): pick a voice (what "Use as narrator voice" stores) → a line without a speaker
/// uses it → plugin reload (<see cref="E2eSelfReload"/>; the stage restarts and this scenario resumes from
/// <see cref="PhaseMark"/>) → still picked → unpicked → the shipped <c>Narrator.wav</c> → picked again but the
/// voice switched off → <c>Narrator.wav</c>. <see cref="NarratorVoicePolicy.Resolve"/> is asserted next to every
/// line. The original pick and the voice's enabled state are put back.
/// </summary>
public sealed class NarratorScenario : E2eScenario
{
    public const string PhaseMark = "narrator";
    public const string OriginalMark = "narrator-original";
    public const string PickMark = "narrator-pick";
    public const string ReloadMark = "narrator-reload";
    internal const string Picked = "picked";
    internal static readonly TimeSpan EngineReadyWait = TimeSpan.FromMinutes(10);

    private readonly Configuration _config;
    private readonly INpcDataService _npcData;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;
    private readonly E2eSelfReload? _reload;
    private readonly E2eEngineWait? _engineWait;

    public NarratorScenario(Configuration config, INpcDataService npcData, IAudioPlaybackService playback,
        E2eSelfTestSpeaker speaker, E2eSelfReload? reload, E2eEngineWait? engineWait)
    {
        _config = config;
        _npcData = npcData;
        _playback = playback;
        _speaker = speaker;
        _reload = reload;
        _engineWait = engineWait;
    }

    public override string Id => "narrator";
    public override TimeSpan Timeout => EngineReadyWait + (_reload?.StageTimeout ?? TimeSpan.Zero) + TimeSpan.FromMinutes(2);

    /// <summary>The voice to pick: enabled and not the flagged default, so every step can tell them apart.</summary>
    internal static EchokrautVoice? Candidate(IEnumerable<EchokrautVoice> voices) =>
        voices.FirstOrDefault(v => v.IsEnabled && !v.IsDefault);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        var marks = ctx.State.Marks;
        marks.Remove(ReloadMark);
        marks.Remove(E2eSelfReload.ReloadRequestedMark);
        marks.TryGetValue(PhaseMark, out var phase);
        var problems = new List<string>();
        try
        {
            if (phase == null)
            {
                var voices = _npcData.GetEchokrautVoices();
                if (Candidate(voices) is not { } pick) return SelfTestResult.Skip("No second voice to pick as narrator.");
                marks[OriginalMark] = _config.NarratorVoice;
                marks[PickMark] = pick.BackendVoice;
                SetPick(pick.BackendVoice);
                ctx.Save();
                await StepAsync("picked", pick.BackendVoice, problems).ConfigureAwait(false);
                if (problems.Count > 0) return Finish(ctx, problems);
                if (_reload == null) return await AfterRestartAsync(ctx, pick.BackendVoice, "no reload wired", problems, ct).ConfigureAwait(false);

                marks[PhaseMark] = Picked;
                ctx.Save();
                if (await _reload.StopEnginesAsync(ctx, ct).ConfigureAwait(false) is { } busy) return Finish(ctx, [busy]);
                if (await _reload.CloseWindowsAsync(ctx, ct).ConfigureAwait(false) is { } open) return Finish(ctx, [open]);
                var stagePhase = ctx.State.Phase;
                await _reload.RequestReloadAsync(ctx, stagePhase, ReloadMark, ct).ConfigureAwait(false);
                E2eSelfReload.Revert(ctx, stagePhase, ReloadMark);
                return Finish(ctx, [$"no reload within {_reload.ReloadWait.TotalSeconds:0} s after touching the DLL"]);
            }

            var picked = marks.GetValueOrDefault(PickMark) ?? "";
            if (_engineWait != null && await _engineWait.WaitForReadyAsync(ctx, "narrator-reload", EngineReadyWait, ct).ConfigureAwait(false) is { } engine)
                return Finish(ctx, [$"engine after the reload: {engine}"]);
            if (!string.Equals(_config.NarratorVoice, picked, StringComparison.OrdinalIgnoreCase))
                problems.Add($"after the reload the pick is '{_config.NarratorVoice}' instead of '{picked}'");
            return await AfterRestartAsync(ctx, picked, "after the reload", problems, ct).ConfigureAwait(false);
        }
        catch
        {
            if (!marks.ContainsKey(ReloadMark)) Restore(ctx);
            throw;
        }
    }

    private async Task<SelfTestResult> AfterRestartAsync(E2eContext ctx, string picked, string restart, List<string> problems,
        CancellationToken ct)
    {
        var flagged = _npcData.GetEchokrautVoices().FirstOrDefault(v => v.IsDefault)?.BackendVoice;
        await StepAsync(restart, picked, problems).ConfigureAwait(false);
        if (flagged == null)
        {
            // Without the shipped default there is nothing to fall back to: unpick/disable cannot be judged.
            problems.Add($"pick {(problems.Count == 0 ? "and " + restart + " ok" : "checked")}, but no flagged default narrator (Narrator.wav) among the voices - unpicked/switched off cannot fall back");
            return Finish(ctx, problems);
        }

        ct.ThrowIfCancellationRequested();
        SetPick(string.Empty);
        await StepAsync("unpicked", flagged, problems).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        SetPick(picked);
        var voice = _npcData.GetEchokrautVoices().FirstOrDefault(v => string.Equals(v.BackendVoice, picked, StringComparison.OrdinalIgnoreCase));
        if (voice == null) problems.Add($"voice '{picked}' is gone");
        else
        {
            voice.IsEnabled = false;
            _npcData.SaveVoice(voice);
            await StepAsync("picked but switched off", flagged, problems).ConfigureAwait(false);
        }
        if (problems.Count == 0)
            return Finish(ctx, problems, $"'{picked}' narrates when picked and {restart}; unpicked and switched off '{flagged}' takes over.");
        return Finish(ctx, problems);
    }

    /// <summary>One line without a speaker: the policy and the queued line must both name <paramref name="expected"/>.</summary>
    private async Task StepAsync(string step, string expected, List<string> problems)
    {
        var policy = NarratorVoicePolicy.Resolve(_npcData.GetEchokrautVoices(), _config.NarratorVoice)?.BackendVoice;
        if (!string.Equals(policy, expected, StringComparison.OrdinalIgnoreCase))
            problems.Add($"{step}: policy resolves '{policy}' instead of '{expected}'");
        var speech = await _speaker.SendAsync(TextSource.VoiceTest, _speaker.TestText, speakerName: string.Empty).ConfigureAwait(false);
        _playback.ClearQueue(TextSource.VoiceTest);
        if (speech.Queued is not { } message) problems.Add($"{step}: narrator line not queued ({speech.WhyNotQueued()})");
        else if (!string.Equals(message.Speaker?.voice, expected, StringComparison.OrdinalIgnoreCase))
            problems.Add($"{step}: narrator line uses '{message.Speaker?.voice}' instead of '{expected}'");
    }

    private void SetPick(string key)
    {
        _config.NarratorVoice = key;
        _config.Save();
    }

    private SelfTestResult Finish(E2eContext ctx, IReadOnlyList<string> problems, string? passDetail = null)
    {
        Restore(ctx);
        return problems.Count == 0 ? SelfTestResult.Pass(passDetail ?? "ok.") : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }

    /// <summary>Original pick back, the picked voice enabled again, marks gone.</summary>
    private void Restore(E2eContext ctx)
    {
        var marks = ctx.State.Marks;
        if (marks.TryGetValue(PickMark, out var picked)
            && _npcData.GetEchokrautVoices().FirstOrDefault(v => string.Equals(v.BackendVoice, picked, StringComparison.OrdinalIgnoreCase)) is { IsEnabled: false } voice)
        {
            voice.IsEnabled = true;
            _npcData.SaveVoice(voice);
        }
        if (marks.TryGetValue(OriginalMark, out var original)) SetPick(original);
        marks.Remove(PhaseMark);
        marks.Remove(OriginalMark);
        marks.Remove(PickMark);
        ctx.Save();
    }
}
