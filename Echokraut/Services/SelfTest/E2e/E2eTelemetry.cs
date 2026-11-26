using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>
/// Measurements of an E2E run (plan › B2), kept in <c>state.json</c> (survives reloads) and written into
/// the report. Plain data: nothing here may contain the remote host (the report is scrubbed anyway).
/// </summary>
public sealed class E2eTelemetry
{
    public double FreezeLimitMs { get; set; } = E2eFrameMonitor.FreezeLimitMs;

    /// <summary>VRAM with no local engine running (game only), taken once at the start of <c>engine</c>.</summary>
    public long? VramBaselineMiB { get; set; }

    /// <summary>Longest frames during blocking engine operations (install, start, stop, switch).</summary>
    public List<E2eOpTelemetry> Ops { get; set; } = new();

    public List<E2eEngineTelemetry> Engines { get; set; } = new();

    /// <summary>The ops that froze the game (a frame above <see cref="FreezeLimitMs"/>).</summary>
    public IEnumerable<E2eOpTelemetry> Freezes() => Ops.Where(o => o.Frames.MaxMs > FreezeLimitMs);
}

/// <summary>One blocking operation, e.g. <c>install</c>, <c>stop</c>, <c>switch:xtts</c>, <c>start:xtts</c>.</summary>
public sealed record E2eOpTelemetry(string Op, long DurationMs, E2eFrameStats Frames);

/// <summary>Per engine of the loop (R1 lazy install / cuda, R2 FPS + VRAM, R5 underruns).</summary>
public sealed class E2eEngineTelemetry
{
    public string Engine { get; set; } = "";
    public string? HealthDevice { get; set; }
    public string? HealthBackend { get; set; }
    /// <summary>Frames with the engine loaded and idle (a few seconds after ready).</summary>
    public E2eFrameStats? IdleFrames { get; set; }
    /// <summary>Frames while the generation and the pipeline check ran.</summary>
    public E2eFrameStats? GenerationFrames { get; set; }
    public E2eGpuStats? IdleGpu { get; set; }
    public E2eGpuStats? GenerationGpu { get; set; }
    /// <summary>Peak VRAM during generation minus <see cref="E2eTelemetry.VramBaselineMiB"/>.</summary>
    public long? VramOverBaselineMiB { get; set; }
    public long? InstallRootBytes { get; set; }
    /// <summary>The <c>.venv*</c> folders of the wrapper after this engine.</summary>
    public List<string> Venvs { get; set; } = new();
    /// <summary><c>f5_tts</c> in the classic <c>.venv</c> before this engine was switched on.</summary>
    public bool? F5InClassicVenvBefore { get; set; }
    /// <summary>Stream underruns (rebuffer pauses) while the pipeline check played its line.</summary>
    public long? Underruns { get; set; }

    public string Summary() =>
        $"device {HealthDevice ?? "?"}; idle {IdleFrames?.ToString() ?? "-"}; generation {GenerationFrames?.ToString() ?? "-"}; " +
        $"{GenerationGpu?.ToString() ?? "no GPU samples"}" +
        (VramOverBaselineMiB is { } v ? $" (+{v} MiB over baseline)" : "") +
        $"; install root {(InstallRootBytes is { } b ? $"{b / (1024.0 * 1024 * 1024):0.0} GB" : "?")}; venvs {string.Join("/", Venvs)}" +
        (F5InClassicVenvBefore is { } f ? $"; f5_tts before: {(f ? "yes" : "no")}" : "") +
        (Underruns is { } u ? $"; underruns {u}" : "");
}

/// <summary>Everything the engine stage measures with, bundled so the stage stays testable without the game.</summary>
public sealed class E2eTelemetryProbes
{
    public E2eTelemetryProbes(E2eFrameMonitor frames, IE2eGpuProbe gpu,
        Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> health, Func<long> underruns,
        IE2eInstallFootprint footprint, TimeSpan? idleWindow = null, TimeSpan? gpuInterval = null)
    {
        Frames = frames;
        Gpu = gpu;
        Health = health;
        Underruns = underruns;
        Footprint = footprint;
        IdleWindow = idleWindow ?? TimeSpan.FromSeconds(5);
        GpuInterval = gpuInterval ?? TimeSpan.FromSeconds(1);
    }

    public E2eFrameMonitor Frames { get; }
    public IE2eGpuProbe Gpu { get; }
    public Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> Health { get; }
    public Func<long> Underruns { get; }
    public IE2eInstallFootprint Footprint { get; }
    public TimeSpan IdleWindow { get; }
    public TimeSpan GpuInterval { get; }

    /// <summary>Runs <paramref name="op"/> (blocking, on the thread pool) and records its longest frames.</summary>
    public Task<E2eOpTelemetry> MeasureAsync(string name, Func<Task> op) => Frames.MeasureAsync(name, op);
}

/// <summary>What lies on disk under the install root (plan › B2: R1 lazy install).</summary>
public interface IE2eInstallFootprint
{
    long? SizeBytes(string installRoot);
    IReadOnlyList<string> Venvs(string installRoot);
    bool? F5InClassicVenv(string installRoot);
}

public sealed class E2eInstallFootprint : IE2eInstallFootprint
{
    /// <summary>Total file size under the root; null when it can not be read. Reparse points are not followed.</summary>
    public long? SizeBytes(string installRoot)
    {
        try
        {
            if (!Directory.Exists(installRoot)) return null;
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            return new DirectoryInfo(installRoot).EnumerateFiles("*", opts).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> Venvs(string installRoot)
    {
        var wrapper = TtsPaths.EchokrauTtsRoot(installRoot);
        if (!Directory.Exists(wrapper)) return [];
        return Directory.GetDirectories(wrapper, ".venv*").Select(Path.GetFileName).OfType<string>()
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>Null when there is no classic venv yet; Windows layout (<c>Lib\site-packages</c>) first.</summary>
    public bool? F5InClassicVenv(string installRoot)
    {
        var venv = Path.Combine(TtsPaths.EchokrauTtsRoot(installRoot), ".venv");
        if (!Directory.Exists(venv)) return null;
        if (Directory.Exists(Path.Combine(venv, "Lib", "site-packages", "f5_tts"))) return true;
        var lib = Path.Combine(venv, "lib");
        return Directory.Exists(lib)
               && Directory.GetDirectories(lib, "python*").Any(p => Directory.Exists(Path.Combine(p, "site-packages", "f5_tts")));
    }
}
