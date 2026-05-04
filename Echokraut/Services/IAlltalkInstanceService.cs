using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using System;
using System.Threading.Tasks;

namespace Echokraut.Services;

public interface IAlltalkInstanceService
{
    event Action? OnInstanceReady;

    bool Installing { get; }
    bool InstanceRunning { get; }
    bool InstanceStarting { get; }
    bool InstanceStopping { get; }
    bool IsWindows { get; }
    bool IsCudaInstalled { get; }

    /// <summary>
    /// Human-readable label for the current install phase (e.g. "Installing AllTalk...",
    /// "Building voice samples 200/520"). Empty string when no install is active. UI-bound:
    /// the First-Time window's progress bar reads this every frame while
    /// <see cref="Installing"/> is true.
    /// </summary>
    string CurrentInstallStatus { get; }

    /// <summary>
    /// Coarse 0..1 estimate of install progress. Phase 1 (installer subprocess: SDK, AllTalk
    /// download, xtts model, atsetup) is opaque from the outside, so it sticks at ~0.5 the
    /// whole time; phase 2 (voice-sample extract) reports real progress through
    /// <see cref="IVoiceSampleExtractorService.ProgressChanged"/> and gets mapped to 0.5..0.95;
    /// finalization bumps to 1.0.
    /// </summary>
    float CurrentInstallProgress { get; }

    void Install();
    void StartInstance();
    void StopInstance(EKEventId eventId);
    Task InstallCustomData(EKEventId eventId, bool installProcess = true);
}
