using Echokraut.DataClasses;
using System;

namespace Echokraut.Services;

/// <summary>
/// Manages the local EchokrauTTS (F5-TTS wrapper) process lifecycle: install (download wrapper +
/// run its bootstrap via the shared EchokrautLocalInstaller), start, stop. Mirrors
/// <see cref="IAlltalkInstanceService"/> so the UI builder stays symmetric. Fires
/// <see cref="OnInstanceReady"/> when the server is up so BackendService can connect.
/// </summary>
public interface IEchokrauTtsInstanceService
{
    event Action? OnInstanceReady;

    bool Installing { get; }
    bool InstanceRunning { get; }
    bool InstanceStarting { get; }
    bool InstanceStopping { get; }

    /// <summary>Human-readable label for the current install phase (UI progress bar).</summary>
    string CurrentInstallStatus { get; }

    /// <summary>Coarse 0..1 install progress estimate.</summary>
    float CurrentInstallProgress { get; }

    void Install();
    void StartInstance();
    void StopInstance(EKEventId eventId);
}
