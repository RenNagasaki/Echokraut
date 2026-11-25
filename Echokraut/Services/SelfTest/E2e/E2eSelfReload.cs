using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Phase A shared by the stages that swap the config files under the plugin (<c>backup</c>, <c>restore</c>):
/// stop the local engines, require their ports to be free (a wrapper this plugin did not start would stay
/// up [BLK-2]), close the own windows and wait until the game has finalized them (a window still alive at
/// unload is finalized later through code of the unloaded plugin and crashes the game, run 20261123-130938),
/// suppress the config save on Dispose [H6], move the run to the pending phase and touch the
/// own DLL so Dalamud reloads the plugin. The file work happens in the next ctor (<see cref="E2eStartupReset"/>).
/// </summary>
public sealed class E2eSelfReload
{
    public const int DefaultEnginePort = 8765;
    /// <summary>When the reload was asked for (UTC, round-trip format).</summary>
    public const string ReloadRequestedMark = "reload-requested-utc";
    internal static readonly TimeSpan DefaultReloadWait = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan DefaultPortWait = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan DefaultWindowWait = TimeSpan.FromSeconds(10);

    private static readonly EKEventId EventId = new(0, TextSource.Backend);

    private readonly IEchokrauTtsInstanceService _echokrauTts;
    private readonly IAlltalkInstanceService _alltalk;
    private readonly Configuration _config;
    private readonly Func<int, CancellationToken, Task<bool>> _portOpen;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string, DateTime> _touch;
    private readonly TimeSpan _poll;
    private readonly IOwnedWindowsService? _windows;
    private readonly Func<Action, Task> _onFramework;

    public E2eSelfReload(IEchokrauTtsInstanceService echokrauTts, IAlltalkInstanceService alltalk, Configuration config,
        string dllPath, Func<int, CancellationToken, Task<bool>>? portOpen = null, Func<DateTime>? utcNow = null,
        Action<string, DateTime>? touch = null, TimeSpan? reloadWait = null, TimeSpan? portWait = null,
        TimeSpan? pollInterval = null, IOwnedWindowsService? windows = null, Func<Action, Task>? onFramework = null,
        TimeSpan? windowWait = null)
    {
        _echokrauTts = echokrauTts;
        _alltalk = alltalk;
        _config = config;
        DllPath = dllPath;
        _portOpen = portOpen ?? IsPortOpenAsync;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _touch = touch ?? File.SetLastWriteTimeUtc;
        ReloadWait = reloadWait ?? DefaultReloadWait;
        PortWait = portWait ?? DefaultPortWait;
        _poll = pollInterval ?? TimeSpan.FromSeconds(1);
        _windows = windows;
        _onFramework = onFramework ?? (a => { a(); return Task.CompletedTask; });
        WindowWait = windowWait ?? DefaultWindowWait;
    }

    public string DllPath { get; }
    public TimeSpan ReloadWait { get; }
    public TimeSpan PortWait { get; }
    public TimeSpan WindowWait { get; }

    /// <summary>Stage timeout that covers stopping, the port wait, the window wait and the reload wait.</summary>
    public TimeSpan StageTimeout => ReloadWait + PortWait + WindowWait + TimeSpan.FromMinutes(1);

    /// <summary>The default engine port and the port of the configured local instance.</summary>
    public IReadOnlyList<int> Ports()
    {
        var ports = new List<int> { DefaultEnginePort };
        if (Uri.TryCreate(_config.EchokrauTts.BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback && !ports.Contains(uri.Port))
            ports.Add(uri.Port);
        return ports;
    }

    /// <summary>Stops both local engines (thread pool only); null when every port is free, else why not.</summary>
    public async Task<string?> StopEnginesAsync(E2eContext ctx, CancellationToken ct)
    {
        ctx.Progress("stopping the local engine");
        _echokrauTts.StopInstance(EventId);
        _alltalk.StopInstance(EventId);

        var deadline = DateTime.UtcNow + PortWait;
        while (true)
        {
            var busy = new List<int>();
            foreach (var p in Ports())
                if (await _portOpen(p, ct).ConfigureAwait(false)) busy.Add(p);
            if (busy.Count == 0) return null;
            if (DateTime.UtcNow >= deadline)
                return $"Port {string.Join(", ", busy)} still answers after stopping the engine - a wrapper this plugin did not start is running; close it first.";
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes every own window (framework thread) and waits until none has a native addon left; null when
    /// all are gone (or no window service is wired), else why not. Must run before the DLL touch.
    /// </summary>
    public async Task<string?> CloseWindowsAsync(E2eContext ctx, CancellationToken ct)
    {
        if (_windows == null || !_windows.AnyAlive) return null;
        ctx.Progress("closing the plugin windows");
        await _onFramework(_windows.RequestCloseAll).ConfigureAwait(false);
        var deadline = DateTime.UtcNow + WindowWait;
        while (_windows.AnyAlive)
        {
            if (DateTime.UtcNow >= deadline)
                return $"A plugin window was still alive {WindowWait.TotalSeconds:0} s after closing it - no reload (it would crash the game when finalized after the unload).";
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>
    /// Suppresses the Dispose save, sets <paramref name="pending"/> + <paramref name="requestedMark"/> = run id,
    /// saves, touches the DLL and waits <see cref="ReloadWait"/>. The reload cancels the wait; returning means
    /// no reload came (the caller reverts with <see cref="Revert"/>).
    /// </summary>
    public async Task RequestReloadAsync(E2eContext ctx, E2ePhase pending, string requestedMark, CancellationToken ct)
    {
        var s = ctx.State;
        var now = _utcNow();
        ctx.SuppressSaveOnDispose(true);
        s.Phase = pending;
        s.Marks[requestedMark] = s.Id;
        s.Marks[ReloadRequestedMark] = now.ToString("O", CultureInfo.InvariantCulture);
        ctx.Save(); // before the touch: the reload may come within the watcher's 500 ms debounce
        ctx.Progress("reload-requested");
        _touch(DllPath, now);
        await Task.Delay(ReloadWait, ct).ConfigureAwait(false);
    }

    /// <summary>Undoes <see cref="RequestReloadAsync"/>: save on Dispose again, phase back, marks gone.</summary>
    public static void Revert(E2eContext ctx, E2ePhase phase, string requestedMark)
    {
        ctx.SuppressSaveOnDispose(false);
        ctx.State.Phase = phase;
        ctx.State.Marks.Remove(requestedMark);
        ctx.State.Marks.Remove(ReloadRequestedMark);
        ctx.Save();
    }

    /// <summary>True when something accepts a TCP connection on <c>127.0.0.1:port</c> within 1 s.</summary>
    internal static async Task<bool> IsPortOpenAsync(int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
