using Echokraut.Enums;

namespace Echokraut.Services;

/// <summary>
/// Aggregates "is some long-running operation in progress" across the plugin so UI can
/// dim backend / mode-affecting controls during it. Operations register themselves by
/// exposing an <c>IsRunning</c> flag on their own service; this aggregator polls those.
/// Reusable for current ops (harvest, voice-sample extract) and future ones (import,
/// export, starter set, …) — add new ops to <see cref="BatchOperation"/> and to the
/// aggregator's <c>CurrentOperation</c> resolution.
/// </summary>
public interface IBatchModeService
{
    bool IsActive { get; }
    BatchOperation CurrentOperation { get; }
}
