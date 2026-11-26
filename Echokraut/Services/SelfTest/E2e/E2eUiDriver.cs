using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// The E2E stage <c>ui</c>'s hands on the own windows (plan docs/plans/ingame-e2e.md › B5): every probe goes
/// to the framework thread through <see cref="IOwnedWindowsService.Probe"/>; waits run on the thread pool.
/// </summary>
public sealed class E2eUiDriver
{
    private readonly IOwnedWindowsService _windows;
    private readonly Func<Action, Task> _onFramework;
    private readonly TimeSpan _settle;
    private readonly TimeSpan _poll;

    /// <param name="settle">Pause after a step so the window's next frames apply it (deferred clicks, layout).</param>
    /// <param name="wait">How long a window may take to open or to be finalized after closing.</param>
    public E2eUiDriver(IOwnedWindowsService windows, Func<Action, Task> onFramework, TimeSpan? settle = null,
        TimeSpan? poll = null, TimeSpan? wait = null)
    {
        Wait = wait ?? DefaultWait;
        _windows = windows;
        _onFramework = onFramework;
        _settle = settle ?? TimeSpan.FromMilliseconds(400);
        _poll = poll ?? TimeSpan.FromMilliseconds(200);
    }

    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    public TimeSpan Wait { get; }

    public async Task<OwnedWindowsProbe> SendAsync(OwnedWindowsProbe probe)
    {
        await _onFramework(() => _windows.Probe(probe)).ConfigureAwait(false);
        return probe;
    }

    public async Task<IReadOnlyList<OwnedWindowState>> SnapshotAsync()
        => (await SendAsync(new OwnedWindowsProbe(OwnedWindowProbeKind.Snapshot)).ConfigureAwait(false)).Windows;

    public async Task<OwnedWindowState?> StateAsync(string id)
        => (await SnapshotAsync().ConfigureAwait(false)).FirstOrDefault(w => w.Id == id);

    /// <summary>Polls the window's state until <paramref name="condition"/> holds; the last state seen, or null on timeout.</summary>
    public async Task<OwnedWindowState?> WaitForAsync(string id, Func<OwnedWindowState, bool> condition, TimeSpan wait,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            var state = await StateAsync(id).ConfigureAwait(false);
            if (state != null && condition(state)) return state;
            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Opens the window and waits until it is open; null on success, else why not.</summary>
    public async Task<string?> OpenAsync(string id, CancellationToken ct)
    {
        var probe = await SendAsync(new OwnedWindowsProbe(OwnedWindowProbeKind.Open, id)).ConfigureAwait(false);
        if (probe.Error != null) return probe.Error;
        if (await WaitForAsync(id, w => w.IsOpen, Wait, ct).ConfigureAwait(false) == null)
            return $"{id} did not open within {Wait.TotalSeconds:0} s";
        await SettleAsync(ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>Closes the window and waits until the game has finalized it (no native addon left).</summary>
    public async Task<string?> CloseAsync(string id, CancellationToken ct)
    {
        var probe = await SendAsync(new OwnedWindowsProbe(OwnedWindowProbeKind.Close, id)).ConfigureAwait(false);
        if (probe.Error != null) return probe.Error;
        return await WaitForAsync(id, w => !w.Alive, Wait, ct).ConfigureAwait(false) == null
            ? $"{id} still alive {Wait.TotalSeconds:0} s after closing it"
            : null;
    }

    public async Task<IReadOnlyList<string>> StepsAsync(string id)
        => (await SendAsync(new OwnedWindowsProbe(OwnedWindowProbeKind.SweepSteps, id)).ConfigureAwait(false)).Steps;

    /// <summary>Runs one sweep step and lets it settle; null on success, else the probe's error.</summary>
    public async Task<string?> StepAsync(string id, string step, CancellationToken ct)
    {
        var probe = await SendAsync(new OwnedWindowsProbe(OwnedWindowProbeKind.RunStep, id, step: step)).ConfigureAwait(false);
        if (probe.Error != null) return probe.Error;
        await SettleAsync(ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> MoveAsync(string id, Vector2 position, CancellationToken ct)
    {
        var probe = await SendAsync(new OwnedWindowsProbe(OwnedWindowProbeKind.Move, id, position)).ConfigureAwait(false);
        if (probe.Error != null) return probe.Error;
        await SettleAsync(ct).ConfigureAwait(false);
        return null;
    }

    public Task SettleAsync(CancellationToken ct) => _settle > TimeSpan.Zero ? Task.Delay(_settle, ct) : Task.CompletedTask;
}
