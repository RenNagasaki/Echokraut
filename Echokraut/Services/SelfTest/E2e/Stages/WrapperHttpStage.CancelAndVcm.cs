using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services.SelfTest.Checks;

namespace Echokraut.Services.SelfTest.E2e.Stages;

public sealed partial class WrapperHttpStage
{
    // ----------------------------------------------------------------- cancel

    /// <summary>
    /// Test plan R1 cancel refcount, two pairs of clients on one generation each (miss + shared). Pair one: cancelling
    /// the first ends only its stream, the second gets the clip to the end (generation <c>done</c>,
    /// <c>tts request done</c> logged). Pair two: cancelling both cancels the generation (job state, no
    /// <c>tts request done</c>, <c>/health</c> running 0, GPU load drops on cuda). A cancelled run is never stored: the
    /// same text again is a miss (that call is cancelled at once).
    /// </summary>
    private async Task<SelfTestResult> CancelAsync(E2eContext ctx, EchokrautVoice voice, string? device, CancellationToken ct)
    {
        var sample = voice.BackendVoice;
        var problems = new List<string>();
        var notes = new List<string>();

        // Pair one: one of two gone, the other runs to the end.
        // Three test sentences are ~70 s of audio with XTTS: long enough to cancel into, short enough to wait out.
        var one = await StartPairAsync(sample, Text(ctx, "cancel-one", 3), ct).ConfigureAwait(false);
        if (one.Error != null) return SelfTestResult.Fail("pair one: " + one.Error);
        using (one)
        {
            if (!await _http.CancelAsync(one.A!.JobId, ct).ConfigureAwait(false)) problems.Add("/cancel of the first client failed");
            if (!await Ended(one.ReadA!, _t.CancelWait)) problems.Add($"the cancelled first client still streams after {_t.CancelWait.TotalSeconds:0} s");
            var aBytes = one.A.BytesRead;
            if (!await Ended(one.ReadB!, _t.RunToEndWait)) problems.Add($"the remaining client did not finish within {_t.RunToEndWait.TotalSeconds:0} s");
            var state = await _http.JobStateAsync(one.Generation!, ct).ConfigureAwait(false);
            var logged = await UntilAsync(() => Task.FromResult(E2eInstallerLog.TtsDone(_log.Since(one.Mark), one.Generation!)), _t.LogSettle, ct).ConfigureAwait(false);
            notes.Add($"one of two cancelled: first {aBytes} B, second to the end {one.B!.BytesRead} B, generation {state ?? "?"}");
            if (state != "done") problems.Add($"generation {state ?? "unknown"} after one of two cancels, expected done (ran to the end)");
            if (!logged) problems.Add("no 'tts request done' for the generation the second client kept");
            if (one.B.BytesRead <= aBytes) problems.Add("the remaining client got no more audio than the cancelled one");
        }

        // Pair two: both gone, the generation itself is cancelled.
        var text = Text(ctx, "cancel-both", 10);
        var both = await StartPairAsync(sample, text, ct).ConfigureAwait(false);
        if (both.Error != null) return SelfTestResult.Fail("pair two: " + both.Error + " | " + string.Join("; ", notes));
        using (both)
        {
            var during = _gpu == null ? null : E2eGpuSampler.Start(_gpu, _gpuInterval, ct);
            await Task.Delay(_t.GpuWindow, ct).ConfigureAwait(false);
            var duringGpu = during == null ? null : await during.StopAsync().ConfigureAwait(false);
            var okA = await _http.CancelAsync(both.A!.JobId, ct).ConfigureAwait(false);
            var okB = await _http.CancelAsync(both.B!.JobId, ct).ConfigureAwait(false);
            if (!okA || !okB) problems.Add("/cancel of a client failed");
            if (!await Ended(both.ReadA!, _t.CancelWait) || !await Ended(both.ReadB!, _t.CancelWait))
                problems.Add($"a cancelled client still streams after {_t.CancelWait.TotalSeconds:0} s");
            string? finalState = null;
            await UntilAsync(async () => (finalState = await _http.JobStateAsync(both.Generation!, ct).ConfigureAwait(false)) is "cancelled" or "done" or "error",
                _t.CancelWait, ct).ConfigureAwait(false);
            notes.Add($"both cancelled: generation {finalState ?? "?"}");
            if (finalState != "cancelled") problems.Add($"generation {finalState ?? "unknown"} after both cancels, expected cancelled");
            await Task.Delay(_gpuInterval, ct).ConfigureAwait(false); // the load settles a moment after the last kernel
            var after = _gpu == null ? null : E2eGpuSampler.Start(_gpu, _gpuInterval, ct);
            await Task.Delay(_t.GpuWindow, ct).ConfigureAwait(false);
            var afterGpu = after == null ? null : await after.StopAsync().ConfigureAwait(false);
            if (JudgeGpuDrop(device, duringGpu, afterGpu, out var gpuNote) is { } gpuProblem) problems.Add(gpuProblem);
            notes.Add(gpuNote);
            if (E2eInstallerLog.TtsDone(_log.Since(both.Mark), both.Generation!)) problems.Add("the wrapper logged 'tts request done' for the cancelled generation");
        }
        var running = -1;
        await UntilAsync(async () => (running = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache?.Running ?? -1) == 0,
            _t.CancelWait, ct).ConfigureAwait(false);
        if (running != 0) problems.Add($"/health response_cache running {running} after both cancels, expected 0");

        // Never stored: a miss again. Cancelled at once, it only has to answer the header.
        using (var again = await _http.StartTtsAsync(sample, text, false, ct).ConfigureAwait(false))
        {
            if (again == null) problems.Add("/tts refused the call after the cancel");
            else
            {
                notes.Add($"again: {again.Cache}");
                if (again.Cache != "miss") problems.Add($"same text after the cancel: X-Cache {Show(again.Cache)}, expected miss");
                await _http.CancelAsync(again.JobId, ct).ConfigureAwait(false);
                await Ended(again.ReadToEndAsync(ct), _t.CancelWait);
            }
        }
        await UntilAsync(async () => (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache?.Running == 0, _t.CancelWait, ct).ConfigureAwait(false);

        return Verdict(problems, string.Join("; ", notes));

        async Task<bool> Ended(Task<string> read, TimeSpan wait) =>
            await Task.WhenAny(read, Task.Delay(wait, ct)).ConfigureAwait(false) == read;
    }

    /// <summary>Two clients on one generation, both reading in the background; <see cref="Error"/> when that did not come about.</summary>
    private sealed record Pair(long Mark, IE2eTtsCall? A, IE2eTtsCall? B, Task<string>? ReadA, Task<string>? ReadB, string? Generation, string? Error)
        : IDisposable
    {
        public void Dispose()
        {
            A?.Dispose();
            B?.Dispose();
        }
    }

    private async Task<Pair> StartPairAsync(string sample, string text, CancellationToken ct)
    {
        var mark = _log.Mark();
        var a = await _http.StartTtsAsync(sample, text, false, ct).ConfigureAwait(false);
        if (a == null) return new Pair(mark, null, null, null, null, null, "/tts refused the first client.");
        var readA = Task.Run(() => a.ReadToEndAsync(ct), ct);
        var b = await _http.StartTtsAsync(sample, text, false, ct).ConfigureAwait(false);
        if (b == null) return new Pair(mark, a, null, readA, null, null, "/tts refused the second client.");
        var readB = Task.Run(() => b.ReadToEndAsync(ct), ct);
        Pair Failed(string error) => new(mark, a, b, readA, readB, null, error);
        if (a.Cache != "miss" || b.Cache != "shared")
            return Failed($"X-Cache {Show(a.Cache)} + {Show(b.Cache)}, expected miss + shared.");
        if (!await UntilAsync(() => Task.FromResult(a.BytesRead > 0 && b.BytesRead > 0), _t.FirstBytesWait, ct).ConfigureAwait(false))
            return Failed($"No audio for both clients within {_t.FirstBytesWait.TotalSeconds:0} s.");
        string? generation = null;
        await UntilAsync(() => Task.FromResult((generation = E2eInstallerLog.GenerationOf(_log.Since(mark), b.JobId)) != null),
            _t.LogSettle, ct).ConfigureAwait(false);
        return generation == null
            ? Failed("No 'tts cache shared' line naming the generation of the second client.")
            : new Pair(mark, a, b, readA, readB, generation, null);
    }

    /// <summary>
    /// Test plan R1 "GPU drops": judged only on cuda with at least <see cref="GpuJudgeMinPercent"/> mean load while the
    /// generation ran; then the mean afterwards must be lower. <paramref name="note"/> says what was seen either way.
    /// </summary>
    internal static string? JudgeGpuDrop(string? device, E2eGpuStats? during, E2eGpuStats? after, out string note)
    {
        if (during == null || after == null)
        {
            note = "GPU not sampled";
            return null;
        }
        note = $"GPU {during.GpuUtilMean:0} % (max {during.GpuUtilMax}) while generating -> {after.GpuUtilMean:0} % after the cancel";
        if (!string.Equals(device, "cuda", StringComparison.OrdinalIgnoreCase))
        {
            note += $" (device {device ?? "?"}, not judged)";
            return null;
        }
        if (during.GpuUtilMean < GpuJudgeMinPercent)
        {
            note += " (too little load to judge)";
            return null;
        }
        return after.GpuUtilMean < during.GpuUtilMean ? null : "GPU load did not drop after both cancels";
    }

    private async Task<bool> UntilAsync(Func<Task<bool>> done, TimeSpan wait, CancellationToken ct)
    {
        var until = DateTime.UtcNow + wait;
        while (true)
        {
            if (await done().ConfigureAwait(false)) return true;
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(_t.Poll, ct).ConfigureAwait(false);
        }
    }

    // --------------------------------------------------------- vcm-regenerate

    /// <summary>
    /// Test plan R2 "regenerate twice → fresh": a throwaway clip of the self-test speaker, regenerated twice through the
    /// Voice Clip Manager service (the button's call). Each must be a new generation that bypasses the cache. Character,
    /// clip and WAV are listed in <see cref="E2eState.CreatedIds"/> first and removed at the end.
    /// </summary>
    private async Task<SelfTestResult> VcmRegenerateAsync(E2eContext ctx, EchokrautVoice voice, CancellationToken ct)
    {
        var language = _language();
        var text = Text(ctx, "vcm");
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = PipelineCheck.SpeakerName,
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
            Language = (int)language,
            ObjectKind = (int)ObjectKind.EventNpc,
            VoiceKey = voice.BackendVoice,
        });
        ctx.State.CreatedIds.Add(CleanupStage.CharacterPrefix + PipelineCheck.SpeakerName);
        VoiceClipEntity? clip = null;
        NpcMapData? speaker = null;
        try
        {
            clip = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
            {
                CharacterId = character.Id,
                Language = (int)language,
                TextSource = (int)Echotools.Logging.Enums.TextSource.VoiceTest,
                VoiceKey = voice.BackendVoice,
                OriginalText = text,
                CleanedText = text,
                BodyType = (int)BodyType.Adult,
            });
            clip.Character = character;
            ctx.State.CreatedIds.Add(CleanupStage.ClipPrefix + clip.Id);
            ctx.State.CreatedIds.Add(CleanupStage.WavPrefix + _vcm.GetAudioPath(clip));
            ctx.Save();
            speaker = _vcm.BuildVoiceMessage(clip).Speaker;

            var before = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache;
            var mark = _log.Mark();
            var first = await _vcm.GenerateForVoiceClip(clip).ConfigureAwait(false);
            var second = await _vcm.GenerateForVoiceClip(clip).ConfigureAwait(false);
            var starts = await StartsAsync(mark, 2, ct).ConfigureAwait(false);
            var after = (await _http.HealthAsync(ct).ConfigureAwait(false))?.Cache;

            var problems = new List<string>();
            if (!first || !second) problems.Add($"regenerate returned {first}/{second} (see log)");
            if (starts != 2) problems.Add($"{starts} tts request start line(s) for two regenerations, expected 2");
            if (before != null && after != null && JudgeCacheDelta(after.Minus(before), hits: 0, shared: 0, misses: 0, bypassed: 2) is { } delta)
                problems.Add(delta);
            var detail = $"clip {clip.Id}: regenerate {first}/{second}, starts {starts}, /health {Delta(before, after)}";
            return Verdict(problems, detail);
        }
        finally
        {
            RemoveVcmTraces(ctx, clip, speaker, character);
        }
    }

    private void RemoveVcmTraces(E2eContext ctx, VoiceClipEntity? clip, NpcMapData? speaker, CharacterEntity character)
    {
        if (clip != null)
        {
            var wav = _vcm.GetAudioPath(clip);
            if (_vcm.DeleteAudioForVoiceClip(clip) && !System.IO.File.Exists(wav))
                ctx.State.CreatedIds.Remove(CleanupStage.WavPrefix + wav);
            _db.DeleteVoiceClip(clip.Id);
            ctx.State.CreatedIds.Remove(CleanupStage.ClipPrefix + clip.Id);
        }
        if (speaker != null) SelfTestTraces.RemoveCharacter(_db, _npcData, speaker, PipelineCheck.SpeakerName);
        else _db.DeleteCharacter(character.Id);
        ctx.State.CreatedIds.Remove(CleanupStage.CharacterPrefix + PipelineCheck.SpeakerName);
        ctx.Save();
    }
}
