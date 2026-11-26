using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Engine switch EchokrauTTS → AllTalk (test plan R5, the half without an AllTalk install): the same call as the
/// engine dropdown (<see cref="ITtsVoiceSyncService.SwitchEngine"/>) must stop the local EchokrauTTS process —
/// not running, every engine port closed — and give its VRAM back (judged only when <c>/health</c> said cuda).
/// Afterwards it switches back and starts the engine again, because the stages after this one need it. Last scenario.
/// </summary>
public sealed class AllTalkOffScenario : E2eScenario
{
    internal static TimeSpan StopWait { get; set; } = TimeSpan.FromSeconds(30);
    internal static TimeSpan VramWait { get; set; } = TimeSpan.FromSeconds(20);
    internal static TimeSpan Poll { get; set; } = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan EngineReadyWait = TimeSpan.FromMinutes(10);
    /// <summary>The smallest model (xtts) holds well over this; less means the process kept its memory.</summary>
    internal const long MinVramDropMiB = 500;

    private readonly Configuration _config;
    private readonly ITtsVoiceSyncService _voiceSync;
    private readonly IEchokrauTtsInstanceService _engine;
    private readonly E2eEngineWait _engineWait;
    private readonly Func<IReadOnlyList<int>> _ports;
    private readonly Func<int, CancellationToken, Task<bool>> _portOpen;
    private readonly IE2eGpuProbe _gpu;
    private readonly Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> _health;

    public AllTalkOffScenario(Configuration config, ITtsVoiceSyncService voiceSync, IEchokrauTtsInstanceService engine,
        E2eEngineWait engineWait, Func<IReadOnlyList<int>> ports, Func<int, CancellationToken, Task<bool>> portOpen,
        IE2eGpuProbe gpu, Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> health)
    {
        _config = config;
        _voiceSync = voiceSync;
        _engine = engine;
        _engineWait = engineWait;
        _ports = ports;
        _portOpen = portOpen;
        _gpu = gpu;
        _health = health;
    }

    public override string Id => "alltalk-off";
    public override TimeSpan Timeout => StopWait + VramWait + EngineReadyWait + TimeSpan.FromMinutes(1);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        var ek = _config.EchokrauTts;
        if (_config.BackendSelection != TTSBackends.EchokrauTTS || ek.InstanceType != AlltalkInstanceType.Local || !_engine.InstanceRunning)
            return SelfTestResult.Skip("Needs a running local EchokrauTTS engine.");

        var device = (await _health(ct).ConfigureAwait(false))?.device;
        var vramBefore = (await _gpu.SampleAsync(ct).ConfigureAwait(false))?.VramUsedMiB;
        var problems = new List<string>();
        string detail;
        try
        {
            ctx.Progress("switching to AllTalk");
            await Task.Run(() => _voiceSync.SwitchEngine(TTSBackends.Alltalk), ct).ConfigureAwait(false);
            if (_config.BackendSelection != TTSBackends.Alltalk) problems.Add($"engine is {_config.BackendSelection} after the switch");

            var open = await WaitStoppedAsync(ct).ConfigureAwait(false);
            if (_engine.InstanceRunning) problems.Add($"EchokrauTTS still running {StopWait.TotalSeconds:0} s after the switch");
            if (open.Count > 0) problems.Add($"port {string.Join(", ", open)} still open");

            var vram = await VramAfterAsync(device, vramBefore, ct).ConfigureAwait(false);
            if (vram.Problem != null) problems.Add(vram.Problem);
            detail = $"EchokrauTTS stopped, ports closed, {vram.Detail}";
        }
        finally
        {
            if (!ct.IsCancellationRequested && await BackAsync(ctx, ct).ConfigureAwait(false) is { } back) problems.Add(back);
        }
        return problems.Count == 0
            ? SelfTestResult.Pass($"{detail}; back on EchokrauTTS and ready.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }

    private async Task<List<int>> WaitStoppedAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + StopWait;
        while (true)
        {
            var open = new List<int>();
            foreach (var port in _ports())
                if (await _portOpen(port, ct).ConfigureAwait(false)) open.Add(port);
            if ((open.Count == 0 && !_engine.InstanceRunning) || DateTime.UtcNow >= deadline) return open;
            await Task.Delay(Poll, ct).ConfigureAwait(false);
        }
    }

    private async Task<(string Detail, string? Problem)> VramAfterAsync(string? device, long? before, CancellationToken ct)
    {
        if (device == null || !device.StartsWith("cuda", StringComparison.OrdinalIgnoreCase))
            return ($"VRAM not judged (device {device ?? "?"})", null);
        if (before is not { } used) return ("VRAM not judged (no nvidia-smi sample)", null);
        var deadline = DateTime.UtcNow + VramWait;
        long? after = null;
        while (DateTime.UtcNow < deadline)
        {
            after = (await _gpu.SampleAsync(ct).ConfigureAwait(false))?.VramUsedMiB;
            if (after is { } a && used - a >= MinVramDropMiB) return ($"VRAM {used} -> {a} MiB", null);
            await Task.Delay(Poll, ct).ConfigureAwait(false);
        }
        return ($"VRAM {used} -> {after?.ToString() ?? "?"} MiB",
            $"VRAM dropped by less than {MinVramDropMiB} MiB ({used} -> {after?.ToString() ?? "?"} MiB)");
    }

    /// <summary>Back to EchokrauTTS, started and ready; null when it is, else why not.</summary>
    private async Task<string?> BackAsync(E2eContext ctx, CancellationToken ct)
    {
        ctx.Progress("switching back to EchokrauTTS");
        if (_config.BackendSelection != TTSBackends.EchokrauTTS)
            await Task.Run(() => _voiceSync.SwitchEngine(TTSBackends.EchokrauTTS), ct).ConfigureAwait(false);
        if (!_engine.InstanceRunning && !_engine.InstanceStarting)
            await Task.Run(_engine.StartInstance, ct).ConfigureAwait(false);
        var error = await _engineWait.WaitForReadyAsync(ctx, "alltalk-off:back", EngineReadyWait, ct).ConfigureAwait(false);
        return error == null ? null : $"EchokrauTTS not ready again: {error}";
    }
}
