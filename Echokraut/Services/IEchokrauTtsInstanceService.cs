using Echokraut.DataClasses;
using Echokraut.Enums;
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

    /// <summary>
    /// Change the local sub-engine (F5/XTTS). Persists the choice; if a local instance is currently
    /// running or starting, restarts it so the wrapper reloads with the new <c>--tts-backend</c>
    /// (both engines are already installed, so this is a restart, not a reinstall). No-op when the
    /// engine is unchanged.
    /// </summary>
    void SwitchTtsBackend(EchokrauTtsEngine engine);
}
