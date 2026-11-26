using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>Frame times of one recording window (plan › B2). Times in milliseconds.</summary>
public sealed record E2eFrameStats(int Frames, double MeanMs, double P95Ms, double MaxMs, int Hitches)
{
    /// <summary>Mean frames per second over the window (0 without frames).</summary>
    public double MeanFps => MeanMs > 0 ? Math.Round(1000 / MeanMs, 1) : 0;

    public override string ToString() =>
        Frames == 0 ? "no frames" : $"{MeanFps:0.#} fps, p95 {P95Ms:0} ms, max {MaxMs:0} ms, {Hitches} hitch(es)";
}

/// <summary>
/// Frame-time recorder for the E2E telemetry (plan › B2). <see cref="OnFrame"/> runs once per
/// <c>IFramework.Update</c> (through <see cref="IE2eRunService.Tick"/>); a stage opens a window with
/// <see cref="Begin"/> around an operation and gets the frame times that fell into it. A frame that
/// took longer than <see cref="FreezeLimitMs"/> is a freeze (the operation blocked the game); longer
/// than <see cref="HitchMs"/> is a hitch (counted, not judged).
/// </summary>
public sealed class E2eFrameMonitor
{
    /// <summary>A frame above this is a freeze: fails the part that caused it.</summary>
    public const double FreezeLimitMs = 1000;

    /// <summary>A frame above this is a visible hitch; only counted.</summary>
    public const double HitchMs = 250;

    private readonly Func<long> _timestamp;
    private readonly double _msPerTick;
    private readonly object _lock = new();
    private readonly List<Window> _open = new();
    private long _last = long.MinValue;

    public E2eFrameMonitor(Func<long>? timestamp = null, long? ticksPerSecond = null)
    {
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
        _msPerTick = 1000.0 / (ticksPerSecond ?? Stopwatch.Frequency);
    }

    /// <summary>One framework tick: the time since the previous tick goes into every open window.</summary>
    public void OnFrame()
    {
        var now = _timestamp();
        lock (_lock)
        {
            if (_last != long.MinValue && _open.Count > 0)
            {
                var ms = (now - _last) * _msPerTick;
                foreach (var w in _open) w.Add(ms);
            }
            _last = now;
        }
    }

    /// <summary>Starts recording; the first frame counted is the one that ends after this call.</summary>
    public Window Begin()
    {
        var w = new Window(this);
        lock (_lock) _open.Add(w);
        return w;
    }

    /// <summary>Runs <paramref name="op"/> and records its duration and the frames that fell into it.</summary>
    public async Task<E2eOpTelemetry> MeasureAsync(string name, Func<Task> op)
    {
        var w = Begin();
        var started = Stopwatch.GetTimestamp();
        try { await op().ConfigureAwait(false); }
        finally { w.Stop(); }
        return new E2eOpTelemetry(name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, w.Stop());
    }

    private void Close(Window w)
    {
        lock (_lock) _open.Remove(w);
    }

    /// <summary>Frame times since <see cref="Begin"/>; <see cref="Stop"/> ends it (idempotent).</summary>
    public sealed class Window
    {
        private readonly E2eFrameMonitor _owner;
        private readonly List<double> _ms = new();
        private E2eFrameStats? _stats;

        internal Window(E2eFrameMonitor owner) => _owner = owner;

        internal void Add(double ms) => _ms.Add(ms);

        public E2eFrameStats Stop()
        {
            _owner.Close(this);
            lock (_owner._lock) return _stats ??= Summarize(_ms);
        }
    }

    internal static E2eFrameStats Summarize(IReadOnlyCollection<double> ms)
    {
        if (ms.Count == 0) return new E2eFrameStats(0, 0, 0, 0, 0);
        var sorted = ms.OrderBy(x => x).ToArray();
        var p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
        return new E2eFrameStats(sorted.Length, Math.Round(sorted.Average(), 2), Math.Round(p95, 1),
            Math.Round(sorted[^1], 1), sorted.Count(x => x > HitchMs));
    }
}
