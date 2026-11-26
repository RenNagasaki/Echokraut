using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>One reading of the whole GPU (all processes, the game included).</summary>
public sealed record E2eGpuSample(long VramUsedMiB, long VramTotalMiB, int GpuUtilPercent);

/// <summary>GPU readings over a window: peak VRAM, mean and peak load.</summary>
public sealed record E2eGpuStats(int Samples, long VramPeakMiB, long VramTotalMiB, double GpuUtilMean, int GpuUtilMax)
{
    public override string ToString() =>
        Samples == 0 ? "no GPU samples" : $"VRAM peak {VramPeakMiB} MiB, GPU {GpuUtilMean:0}% mean / {GpuUtilMax}% max";

    internal static E2eGpuStats? Of(IReadOnlyCollection<E2eGpuSample> samples) =>
        samples.Count == 0
            ? null
            : new E2eGpuStats(samples.Count, samples.Max(s => s.VramUsedMiB), samples.Max(s => s.VramTotalMiB),
                Math.Round(samples.Average(s => s.GpuUtilPercent), 1), samples.Max(s => s.GpuUtilPercent));
}

/// <summary>Reads VRAM and GPU load (plan › B2). Null when there is no reading (no NVIDIA GPU, no tool).</summary>
public interface IE2eGpuProbe
{
    Task<E2eGpuSample?> SampleAsync(CancellationToken ct);
}

/// <summary>
/// <c>nvidia-smi --query-gpu=…</c>, read-only, first GPU only, without a console window (a window would
/// take the focus from the game and change its frame rate). E2E runs are dev loads only, so this never
/// runs on a user's machine.
/// </summary>
public sealed class NvidiaSmiGpuProbe : IE2eGpuProbe
{
    private const string Query = "--query-gpu=memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits";
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private bool _missing;

    public async Task<E2eGpuSample?> SampleAsync(CancellationToken ct)
    {
        if (_missing) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Executable(), Query)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p == null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CallTimeout);
            var output = await p.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return p.ExitCode == 0 ? Parse(output) : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _missing = true; // not installed: do not try every second
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static string Executable()
    {
        var system = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        return File.Exists(system) ? system : "nvidia-smi";
    }

    /// <summary>First line <c>used, total, util</c> (MiB, MiB, %); null when it does not parse.</summary>
    internal static E2eGpuSample? Parse(string output)
    {
        var line = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line == null) return null;
        var parts = line.Split(',').Select(s => s.Trim()).ToArray();
        if (parts.Length < 3) return null;
        var inv = CultureInfo.InvariantCulture;
        if (!long.TryParse(parts[0], NumberStyles.Integer, inv, out var used)
            || !long.TryParse(parts[1], NumberStyles.Integer, inv, out var total)
            || !int.TryParse(parts[2], NumberStyles.Integer, inv, out var util))
            return null;
        return new E2eGpuSample(used, total, util);
    }
}

/// <summary>Samples <see cref="IE2eGpuProbe"/> in the background until <see cref="StopAsync"/>.</summary>
public sealed class E2eGpuSampler
{
    private readonly List<E2eGpuSample> _samples = new();
    private readonly CancellationTokenSource _cts;
    private readonly Task _loop;

    private E2eGpuSampler(IE2eGpuProbe probe, TimeSpan interval, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                var s = await probe.SampleAsync(_cts.Token).ConfigureAwait(false);
                if (s != null) lock (_samples) _samples.Add(s);
                try { await Task.Delay(interval, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    public static E2eGpuSampler Start(IE2eGpuProbe probe, TimeSpan interval, CancellationToken ct) => new(probe, interval, ct);

    public async Task<E2eGpuStats?> StopAsync()
    {
        _cts.Cancel();
        try { await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* stopped */ }
        _cts.Dispose();
        lock (_samples) return E2eGpuStats.Of(_samples);
    }
}
