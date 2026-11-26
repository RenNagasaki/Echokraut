using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Stage 7 (plan › Stages): the user's private remote EchokrauTTS. Fixed order [BLK-3]: stop the local engine
/// (thread pool) and wait until its port is closed and no process is tracked, only then switch to Remote with
/// the request's <c>remoteUrl</c> through <see cref="IBackendService.SwitchInstanceTypeAsync"/> (the Backend
/// tab's switch) and refresh the voices [H5]. Then backend, generation and pipeline check, and the game voice
/// (decision D2: starting the run is the opt-in): <c>GameVoiceReferenceEnabled</c> + current consent version in
/// <c>touchedOptions</c>, <see cref="Checks.GameVoiceCheck"/>, one line for a voiced NPC with its own game voice
/// (<see cref="E2eGameVoiceGeneration"/>); the opt-in is switched off again at the end of the stage. Each part
/// is reported as <c>remote:&lt;part&gt;</c>. The remote URL never leaves the request (scrubbed to <c>&lt;remote&gt;</c>).
/// </summary>
public sealed class RemoteStage : E2eStage
{
    public const string StageId = "remote";
    public const string PartPrefix = StageId + ":";
    public const string SwitchPart = PartPrefix + "switch";
    public const string GameVoicePart = PartPrefix + "game-voice";
    public const string GameVoiceGenerationPart = PartPrefix + "game-voice-generation";

    internal static readonly TimeSpan VoicesWait = TimeSpan.FromMinutes(2);

    private readonly IBackendService _backend;
    private readonly IEchokrauTtsInstanceService _engine;
    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly Configuration _config;
    private readonly Func<E2eContext, CancellationToken, Task<string?>> _stopLocal;
    private readonly E2eEngineWait _wait;
    private readonly IReadOnlyList<SelfTestCheck?> _checks;
    private readonly SelfTestCheck? _gameVoiceCheck;
    private readonly Func<E2eContext, CancellationToken, Task<SelfTestResult>> _gameVoiceGeneration;
    private readonly TimeSpan _voicesWait;

    /// <param name="stopLocal">Stops the local engines and waits until their ports are closed; null = free
    /// (<see cref="E2eSelfReload.StopEnginesAsync"/>).</param>
    /// <param name="checks">Backend, generation and pipeline check, in that order (null = missing from the catalog).</param>
    public RemoteStage(IBackendService backend, IEchokrauTtsInstanceService engine, IGameVoiceReferenceService gameVoice,
        Configuration config, Func<E2eContext, CancellationToken, Task<string?>> stopLocal, E2eEngineWait wait,
        IReadOnlyList<SelfTestCheck?> checks, SelfTestCheck? gameVoiceCheck,
        Func<E2eContext, CancellationToken, Task<SelfTestResult>> gameVoiceGeneration, TimeSpan? voicesWait = null)
    {
        _backend = backend;
        _engine = engine;
        _gameVoice = gameVoice;
        _config = config;
        _stopLocal = stopLocal;
        _wait = wait;
        _checks = checks;
        _gameVoiceCheck = gameVoiceCheck;
        _gameVoiceGeneration = gameVoiceGeneration;
        _voicesWait = voicesWait ?? VoicesWait;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [ResetStage.StageId];

    public override TimeSpan Timeout
    {
        get
        {
            var t = TimeSpan.FromMinutes(2) + _voicesWait + E2eGameVoiceGeneration.Timeout + (_gameVoiceCheck?.Timeout ?? TimeSpan.Zero);
            foreach (var c in _checks) t += c?.Timeout ?? TimeSpan.Zero;
            return t;
        }
    }

    public static string PartId(string checkId) => PartPrefix + checkId;

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");
        if (string.IsNullOrWhiteSpace(ctx.Request.RemoteUrl))
            return SelfTestResult.Skip("The request has no remoteUrl (start.sh --no-remote?).");
        if (_config.BackendSelection != TTSBackends.EchokrauTTS)
            return SelfTestResult.Fail($"Backend is {_config.BackendSelection}, the remote stage needs EchokrauTTS.");

        // Left over from a run that unloaded mid-stage.
        RestoreGameVoice(ctx);
        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        try
        {
            var switched = await RunPartAsync(ctx, outcomes, SwitchPart, null, c => SwitchAsync(ctx, c), ct).ConfigureAwait(false);
            var blocker = switched == SelfTestOutcome.Pass ? null : $"needs {SwitchPart} ({E2eLogFormat.Kind(switched)})";
            foreach (var check in _checks)
            {
                if (check == null) continue;
                var outcome = await RunPartAsync(ctx, outcomes, PartId(check.Id), blocker, c => RunCheckAsync(check, c), ct).ConfigureAwait(false);
                if (check.Id == "backend" && outcome != SelfTestOutcome.Pass)
                    blocker ??= $"needs {PartId(check.Id)} ({E2eLogFormat.Kind(outcome)})";
            }

            var gvBlocker = blocker ?? (_gameVoiceCheck == null ? "The game-voice check is missing from the self-test catalog." : null);
            var gv = await RunPartAsync(ctx, outcomes, GameVoicePart, gvBlocker, c =>
            {
                EnableGameVoice(ctx);
                return RunCheckAsync(_gameVoiceCheck!, c);
            }, ct).ConfigureAwait(false);
            var genBlocker = gvBlocker ?? (gv == SelfTestOutcome.Pass ? null : $"needs {GameVoicePart} ({E2eLogFormat.Kind(gv)})");
            await RunPartAsync(ctx, outcomes, GameVoiceGenerationPart, genBlocker, c =>
            {
                EnableGameVoice(ctx);
                return _gameVoiceGeneration(ctx, c);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            if (!ct.IsCancellationRequested) RestoreGameVoice(ctx);
        }

        return FromParts(outcomes);
    }

    private static async Task<SelfTestResult> RunCheckAsync(SelfTestCheck check, CancellationToken ct)
    {
        var entry = await SelfTestRunner.RunCheckAsync(check, ct).ConfigureAwait(false);
        return new SelfTestResult(entry.Outcome, $"{entry.Detail} ({entry.DurationMs} ms)");
    }

    /// <summary>Stop local → port closed, nothing tracked → switch to Remote → voices [BLK-3, H5].</summary>
    private async Task<SelfTestResult> SwitchAsync(E2eContext ctx, CancellationToken ct)
    {
        if (await _stopLocal(ctx, ct).ConfigureAwait(false) is { } busy) return SelfTestResult.Fail(busy);
        if (_engine.InstanceRunning || _engine.InstanceStarting || _engine.Installing)
            return SelfTestResult.Fail("The local engine is still tracked as running after the stop - not switched.");

        ctx.Progress("switching to the remote instance");
        await _backend.SwitchInstanceTypeAsync(AlltalkInstanceType.Remote, ctx.Request.RemoteUrl, new EKEventId(0, TextSource.Backend))
            .ConfigureAwait(false);
        if (_config.EchokrauTts.InstanceType != AlltalkInstanceType.Remote)
            return SelfTestResult.Fail($"Instance type is {_config.EchokrauTts.InstanceType} after the switch.");

        var voices = await _wait.WaitForVoicesAsync(_voicesWait, ct).ConfigureAwait(false);
        return voices == 0
            ? SelfTestResult.Fail($"No enabled voice within {_voicesWait.TotalSeconds:0} s after RefreshBackend on the remote.")
            : SelfTestResult.Pass($"Local engine stopped, port free; remote instance with {voices} enabled voice(s).");
    }

    private void EnableGameVoice(E2eContext ctx) => E2eGameVoiceGeneration.EnableOptIn(ctx, _config);

    private void RestoreGameVoice(E2eContext ctx) => E2eGameVoiceGeneration.RestoreOptIn(ctx, _config, _gameVoice);
}
