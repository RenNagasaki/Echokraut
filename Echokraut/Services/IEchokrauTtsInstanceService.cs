using Echokraut.DataClasses;
using Echokraut.Enums;
using System;
using System.Threading.Tasks;

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

    /// <summary>
    /// Newest wrapper release tag known right now: the shipped baseline from <c>RemoteUrls.json</c>
    /// (<c>echokrauTtsVersion</c>) until <see cref="CheckForWrapperUpdateAsync"/> replaces it with
    /// what GitHub reports. Surfaced here rather than injecting <c>IRemoteUrlService</c> into the
    /// UI: this service owns the wrapper's lifecycle and already holds the URL config.
    /// </summary>
    string LatestWrapperVersion { get; }

    /// <summary>Where the wrapper button stands: not checked yet, checking, update available, up to
    /// date, or the check failed. Drives the button's label and whether it can be clicked.</summary>
    WrapperUpdateState UpdateState { get; }

    /// <summary>Why the last check failed (empty unless <see cref="UpdateState"/> is
    /// <see cref="WrapperUpdateState.CheckFailed"/>) — shown next to the button.</summary>
    string UpdateCheckError { get; }

    /// <summary>
    /// Ask GitHub for the wrapper's latest release and remember both its tag and its zip URL.
    /// <para>Runs ONLY on explicit user request. Unauthenticated GitHub API calls are limited to 60
    /// per hour per user IP, and this plugin already fetches several files from GitHub at startup —
    /// so this must never be put on a timer or into the startup path.</para>
    /// </summary>
    Task CheckForWrapperUpdateAsync();

    void Install();
    void StartInstance();
    void StopInstance(EKEventId eventId);

    /// <summary>
    /// Update ONLY the wrapper code to the release behind <c>RemoteUrlsData.EchokrauTtsUrl</c>,
    /// keeping the expensive user data: the installer's <c>updateechokrautts</c> mode extracts the
    /// zip over <c>echokrautts\</c> but skips every entry under <c>samples\</c> and <c>models\</c>,
    /// so voices and downloaded models are neither deleted nor re-downloaded. The wrapper is
    /// (re)started afterwards, exactly like <see cref="Install"/> — install and serve are one
    /// process for this engine. On success <c>EchokrauTtsData.InstalledWrapperVersion</c> is set to
    /// the remote tag. Runs off the UI thread; progress surfaces via
    /// <see cref="CurrentInstallStatus"/> / <see cref="CurrentInstallProgress"/>.
    /// </summary>
    void UpdateWrapper();

    /// <summary>
    /// Install ONLY the user-supplied custom data (a model zip into
    /// <c>echokrautts/models/echokraut_custom</c> and/or a voice-samples zip into
    /// <c>echokrautts/samples</c>), without a full reinstall. Mirrors
    /// <see cref="IAlltalkInstanceService.InstallCustomData"/>. Runs off the UI thread. When a local
    /// instance is running (or <paramref name="installProcess"/> is false and auto-start is on) the
    /// wrapper is restarted afterwards so it reloads with the custom model.
    /// </summary>
    void InstallCustomData(EKEventId eventId, bool installProcess = false);

    /// <summary>
    /// Change the local sub-engine (F5/XTTS). Persists the choice; if a local instance is currently
    /// running or starting, restarts it so the wrapper reloads with the new <c>--tts-backend</c>
    /// (both engines are already installed, so this is a restart, not a reinstall). No-op when the
    /// engine is unchanged.
    /// </summary>
    void SwitchTtsBackend(EchokrauTtsEngine engine);

    /// <summary>
    /// Toggle XTTS fp16 (half-precision) on the local wrapper. Persists the choice; restarts a
    /// running/starting local instance so the model reloads with the new precision. No-op when
    /// unchanged. Only has an effect with the XTTS engine on a CUDA/ROCm GPU.
    /// </summary>
    void SetXttsFp16(bool enabled);
}
