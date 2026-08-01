using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.DataClasses;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Echokraut.Services;

/// <summary>
/// Local EchokrauTTS process lifecycle, parallel to <see cref="AlltalkInstanceService"/>. Drives the
/// shared EchokrautLocalInstaller's <c>echokrautts</c> mode, which (optionally downloads then) runs
/// the wrapper's self-bootstrap. Unlike AllTalk, the wrapper bootstrap is a SINGLE long-running
/// process that installs (idempotent) AND serves — so Install and Start are the same launch, Install
/// just downloads the wrapper first. Readiness is signalled by a <c>Ready.EchokrauTTS.txt</c> file
/// (separate from AllTalk's Ready.txt so both engines coexist).
/// </summary>
public sealed class EchokrauTtsInstanceService : IEchokrauTtsInstanceService, IDisposable
{
    private const string ReadyFileName = "Ready.EchokrauTTS.txt";
    private const int DefaultPort = 8765;

    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IRemoteUrlService _remoteUrls;
    private readonly IClientState _clientState;
    private readonly IVoicePackService _voicePack;

    public event Action? OnInstanceReady;

    public bool Installing { get; private set; }
    public bool InstanceRunning { get; private set; }
    public bool InstanceStarting { get; private set; }
    public bool InstanceStopping { get; private set; }
    public string CurrentInstallStatus { get; private set; } = string.Empty;
    public float CurrentInstallProgress { get; private set; }

    // Result of the last "Check for updates". Null until the user asks; once set it takes precedence
    // over the RemoteUrls baseline for BOTH the tag and the download URL — they must stay paired, or
    // an update would install the old zip and record the new version against it.
    private GitHubReleaseParser.Release? _foundRelease;

    /// <inheritdoc/>
    public string LatestWrapperVersion =>
        _foundRelease?.Tag ?? _remoteUrls.Urls.EchokrauTtsVersion ?? string.Empty;

    /// <inheritdoc/>
    public WrapperUpdateState UpdateState { get; private set; } = WrapperUpdateState.NotChecked;

    /// <inheritdoc/>
    public string UpdateCheckError { get; private set; } = string.Empty;

    /// <summary>Zip to install: the release found by the update check, else the shipped baseline.</summary>
    private string WrapperDownloadUrl =>
        _foundRelease?.DownloadUrl ?? _remoteUrls.Urls.EchokrauTtsUrl;

    /// <inheritdoc/>
    public async Task CheckForWrapperUpdateAsync()
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        var apiUrl = _remoteUrls.Urls.EchokrauTtsReleasesUrl;
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            UpdateCheckError = "no release URL configured";
            UpdateState = WrapperUpdateState.CheckFailed;
            _log.Warning(nameof(CheckForWrapperUpdateAsync),
                "RemoteUrls.echokrauTtsReleasesUrl is empty — cannot check for wrapper updates.", eventId);
            return;
        }

        UpdateState = WrapperUpdateState.Checking;
        UpdateCheckError = string.Empty;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            // GitHub rejects API requests without a User-Agent, and asks for an explicit API version.
            req.Headers.TryAddWithoutValidation("User-Agent", "Echokraut");
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            var res = await _updateCheckClient.SendAsync(req).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                // 403 here is almost always the unauthenticated rate limit (60/h per IP), which is
                // worth naming: it is not an outage and it resolves by waiting.
                var hint = res.StatusCode == HttpStatusCode.Forbidden ? " (GitHub rate limit?)" : string.Empty;
                Fail($"GitHub returned {(int)res.StatusCode}{hint}", eventId);
                return;
            }

            if (!GitHubReleaseParser.TryParseLatestRelease(body, out var release, out var reason))
            {
                Fail(reason, eventId);
                return;
            }

            _foundRelease = release;
            UpdateState = WrapperUpdatePolicy.IsUpdateAvailable(
                _config.EchokrauTts.LocalInstall, _config.EchokrauTts.InstalledWrapperVersion, release!.Tag)
                ? WrapperUpdateState.UpdateAvailable
                : WrapperUpdateState.UpToDate;
            _log.Info(nameof(CheckForWrapperUpdateAsync),
                $"Latest wrapper release is {release.Tag}; installed is " +
                $"{WrapperUpdatePolicy.Display(_config.EchokrauTts.InstalledWrapperVersion)} → {UpdateState}", eventId);
        }
        catch (Exception ex)
        {
            // Offline, DNS, timeout — recoverable and user-triggered, so Warning, not Error.
            Fail(ex.Message, eventId);
        }
    }

    /// <summary>A failed lookup must never look like "you are up to date" — it returns the button to
    /// "Check for updates" and keeps the reason for the label next to it.</summary>
    private void Fail(string reason, EKEventId eventId)
    {
        UpdateCheckError = reason;
        UpdateState = WrapperUpdateState.CheckFailed;
        _log.Warning(nameof(CheckForWrapperUpdateAsync), $"Wrapper update check failed: {reason}", eventId);
    }

    private bool IsWindows { get; }

    // Short timeout: the update check is a foreground action the user is waiting on, and a hanging
    // GitHub request must not leave the button stuck in "Checking...".
    private static readonly HttpClient _updateCheckClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private Task? _instanceThread;
    private Process? _instanceProcess;
    private volatile bool _instanceProcessIsRunning;

    public EchokrauTtsInstanceService(ILogService log, Configuration config, IRemoteUrlService remoteUrls,
        IClientState clientState, IVoicePackService voicePack)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _voicePack = voicePack ?? throw new ArgumentNullException(nameof(voicePack));
        IsWindows = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows;
    }

    /// <summary>Download the wrapper + bootstrap (install) + serve. Marks LocalInstall on ready.</summary>
    public void Install() => Launch(download: true, update: false);

    /// <summary>Run the wrapper bootstrap (install-if-needed) + serve, without re-downloading.</summary>
    public void StartInstance() => Launch(download: false, update: false);

    /// <inheritdoc/>
    public void UpdateWrapper() => Launch(download: true, update: true);

    /// <inheritdoc/>
    public void InstallCustomData(EKEventId eventId, bool installProcess = false)
    {
        var (pathValid, _) = Windows.Native.NativeAlltalkBuilder.ValidateInstallPath(_config.TtsInstallRoot);
        if (!pathValid)
        {
            _log.Warning(nameof(InstallCustomData), "Install path is invalid, aborting.", eventId);
            return;
        }
        if (string.IsNullOrWhiteSpace(_config.EchokrauTts.CustomModelUrl)
            && string.IsNullOrWhiteSpace(_config.EchokrauTts.CustomVoicesUrl))
        {
            _log.Warning(nameof(InstallCustomData), "No custom model or voices URL set — nothing to install.", eventId);
            return;
        }

        try
        {
            // Restart the wrapper afterwards if it's running now, or if it would auto-start —
            // otherwise a freshly installed custom model wouldn't be picked up until next launch.
            var wasRunning = InstanceRunning || InstanceStarting;
            var shouldRestart = wasRunning || (!installProcess && _config.EchokrauTts.AutoStartLocalInstance);

            if (_instanceProcessIsRunning || _instanceProcess != null || _instanceThread != null)
                StopInstance(eventId);
            _instanceThread = Task.Run(() => RunCustomDataInstall(shouldRestart, eventId));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(InstallCustomData), $"Error while installing EchokrauTTS custom data: {ex}", eventId);
            StopInstance(eventId);
        }
    }

    /// <inheritdoc/>
    public void SwitchTtsBackend(Echokraut.Enums.EchokrauTtsEngine engine)
    {
        if (_config.EchokrauTts.TtsBackend == engine) return;

        _config.EchokrauTts.TtsBackend = engine;
        _config.Save();
        RestartLocalIfRunning($"engine set to {engine}");
    }

    /// <inheritdoc/>
    public void SetXttsFp16(bool enabled)
    {
        if (_config.EchokrauTts.XttsFp16 == enabled) return;

        _config.EchokrauTts.XttsFp16 = enabled;
        _config.Save();
        RestartLocalIfRunning($"XTTS fp16 set to {enabled}");
    }

    /// <summary>
    /// Persist-then-apply helper for local-instance settings that only take effect at wrapper startup
    /// (engine, fp16). Logs the change; if a local instance is running/starting, restarts it so the
    /// new args are picked up. Everything needed is already installed, so this is a plain restart.
    /// </summary>
    private void RestartLocalIfRunning(string reason)
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        _log.Info(nameof(RestartLocalIfRunning), $"EchokrauTTS local: {reason}", eventId);
        if (InstanceRunning || InstanceStarting)
        {
            _log.Info(nameof(RestartLocalIfRunning), "Restarting local instance to apply change", eventId);
            StopInstance(eventId);
            StartInstance();
        }
    }

    private string EnsureInstaller(EKEventId eventId)
    {
        var exe = LocalInstallerProvisioner.Ensure(
            _config.TtsInstallRoot, _remoteUrls.Urls.InstallerUrl, _remoteUrls.Urls.InstallerVersion,
            _config.InstalledInstallerVersion, _log, eventId, out var downloadedVersion);
        if (downloadedVersion != null)
        {
            _config.InstalledInstallerVersion = downloadedVersion;
            _config.Save();
        }
        return exe;
    }

    private static string LanguageCode(Dalamud.Game.ClientLanguage lang) => VoiceScdPaths.LanguageCodeForScd(lang);

    private int Port()
    {
        try { return new Uri(_config.EchokrauTts.BaseUrl).Port; }
        catch { return DefaultPort; }
    }

    private void Launch(bool download, bool update)
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        var (pathValid, _) = Windows.Native.NativeAlltalkBuilder.ValidateInstallPath(_config.TtsInstallRoot);
        if (!pathValid)
        {
            _log.Warning(nameof(Launch), "Install path is invalid, aborting.", eventId);
            return;
        }
        if (download && string.IsNullOrWhiteSpace(WrapperDownloadUrl))
        {
            _log.Warning(nameof(Launch),
                "EchokrauTTS wrapper URL is not configured (RemoteUrls.echokrauTtsUrl) — local install " +
                "is unavailable until the wrapper release is published. Use Remote mode meanwhile.", eventId);
            CurrentInstallStatus = "EchokrauTTS download URL not configured";
            return;
        }

        try
        {
            if (_instanceProcessIsRunning || _instanceProcess != null || _instanceThread != null)
                StopInstance(eventId);
            _instanceThread = Task.Run(() => RunInstance(download, update, eventId));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Launch), $"Error while running EchokrauTTS instance: {ex}", eventId);
            StopInstance(eventId);
        }
    }

    // echokrautts|updateechokrautts <installRoot> <echokrauTtsUrl-or-empty> <isWindows> <port> <language> <parentPid> <ttsBackend> <xttsFp16>
    // The update mode takes the SAME arguments; it only changes how the zip is unpacked (samples/
    // and models/ are skipped so user voices and downloaded models survive).
    private ProcessStartInfo BuildProcessInfo(bool download, bool update, string installerExe) => new(installerExe)
    {
        UseShellExecute = true,
        CreateNoWindow = false,
        ArgumentList =
        {
            update ? "updateechokrautts" : "echokrautts",
            _config.TtsInstallRoot,
            download ? WrapperDownloadUrl : string.Empty,
            IsWindows.ToString(),
            Port().ToString(),
            LanguageCode(_clientState.ClientLanguage),
            Environment.ProcessId.ToString(),
            _config.EchokrauTts.TtsBackendArg,
            _config.EchokrauTts.XttsFp16Arg,
        }
    };

    /// <summary>
    /// Seed the EchokrauTTS voices on a fresh local install — the same curated, freely-licensed
    /// voice pack <see cref="AlltalkInstanceService"/> downloads, but targeting EchokrauTTS's
    /// samples folder (<c>echokrautts/samples/</c>) instead of AllTalk's voices folder.
    /// Non-fatal: the install still completes if it fails; the user can re-run the download from
    /// the Game Data Tools window. Pack progress (0..1) maps onto the install bar's 0.50..0.95 band.
    /// </summary>
    private void DownloadVoicePack(EKEventId eventId)
    {
        var ekRoot = TtsPaths.EchokrauTtsRoot(_config.TtsInstallRoot);
        var samplesDir = TtsPaths.EchokrauTtsSamples(_config.TtsInstallRoot);
        _log.Info(nameof(DownloadVoicePack), $"Downloading voice pack into {samplesDir}...", eventId);
        CurrentInstallStatus = "Downloading voices...";
        CurrentInstallProgress = 0.50f;

        Action<string, int, int> onPackProgress = (label, current, total) =>
        {
            var ratio = total > 0 ? Math.Clamp((float)current / total, 0f, 1f) : 0f;
            CurrentInstallProgress = 0.50f + ratio * 0.45f;
            if (!string.IsNullOrEmpty(label))
                CurrentInstallStatus = $"Voices — {label} ({current}/{total})";
        };
        try
        {
            _voicePack.ProgressChanged += onPackProgress;
            using var packCts = new CancellationTokenSource();
            _voicePack.DownloadAsync(packCts.Token,
                outputRootOverride: ekRoot, outputSubfolder: TtsPaths.EchokrauTtsSamplesFolder)
                .GetAwaiter().GetResult();
            _log.Info(nameof(DownloadVoicePack), "Voice pack ready.", eventId);
        }
        catch (Exception ex)
        {
            // Non-fatal: install still completes. User can re-run the download from Game Data Tools.
            _log.Warning(nameof(DownloadVoicePack),
                $"Voice pack download failed during install: {ex.Message}. " +
                $"Run it manually from Game Data Tools later.", eventId);
        }
        finally
        {
            _voicePack.ProgressChanged -= onPackProgress;
        }
    }

    // installcustomdataek <installRoot> <customModelUrl> <customVoicesUrl> <isWindows> <shouldRestart> <port> <language> <parentPid> <ttsBackend> <xttsFp16>
    private ProcessStartInfo BuildCustomDataProcessInfo(bool shouldRestart, string installerExe) => new(installerExe)
    {
        UseShellExecute = true,
        CreateNoWindow = false,
        ArgumentList =
        {
            "installcustomdataek",
            _config.TtsInstallRoot,
            _config.EchokrauTts.CustomModelUrl ?? string.Empty,
            _config.EchokrauTts.CustomVoicesUrl ?? string.Empty,
            IsWindows.ToString(),
            shouldRestart.ToString(),
            Port().ToString(),
            LanguageCode(_clientState.ClientLanguage),
            Environment.ProcessId.ToString(),
            _config.EchokrauTts.TtsBackendArg,
            _config.EchokrauTts.XttsFp16Arg,
        }
    };

    /// <summary>
    /// Runs the installer's <c>installcustomdataek</c> mode: it drops the custom model/samples into
    /// the wrapper layout and, when <paramref name="shouldRestart"/>, relaunches the wrapper (serve)
    /// as one long-running process — mirroring <see cref="RunInstance"/>'s ready-file polling. When
    /// no restart is wanted the installer just applies the data and exits.
    /// </summary>
    private void RunCustomDataInstall(bool shouldRestart, EKEventId eventId)
    {
        try
        {
            Installing = true;
            CurrentInstallStatus = "Installing custom data (downloads may take a while)...";
            CurrentInstallProgress = 0.10f;
            _log.Info(nameof(RunCustomDataInstall), $"Installing EchokrauTTS custom data (shouldRestart={shouldRestart})", eventId);

            var installerExe = EnsureInstaller(eventId);
            var readyFile = Path.Join(Path.GetDirectoryName(installerExe), ReadyFileName);
            if (File.Exists(readyFile)) { try { File.Delete(readyFile); } catch { /* will be recreated */ } }

            if (shouldRestart) InstanceStarting = true;
            _instanceProcess = new Process { StartInfo = BuildCustomDataProcessInfo(shouldRestart, installerExe) };
            _instanceProcess.Start();
            _instanceProcessIsRunning = true;

            if (!shouldRestart)
            {
                // Installer applies the custom data and exits — no serving process to track.
                _instanceProcess.WaitForExit();
                _instanceProcessIsRunning = false;
                _instanceProcess.Dispose();
                _instanceProcess = null;
                _instanceThread = null;
                Installing = false;
                CurrentInstallStatus = "Done";
                CurrentInstallProgress = 1.0f;
                _log.Info(nameof(RunCustomDataInstall), "Custom data installed", eventId);
                return;
            }

            // Installer applies the custom data then relaunches the wrapper (serve): poll ready.
            while (!File.Exists(readyFile) && !_instanceProcess.HasExited)
                Thread.Sleep(2000);

            InstanceStarting = false;
            Installing = false;
            if (!File.Exists(readyFile))
            {
                _log.Warning(nameof(RunCustomDataInstall), "Installer exited before EchokrauTTS became ready", eventId);
                CurrentInstallStatus = "Failed: install did not complete";
                InstanceRunning = false;
                _instanceProcessIsRunning = false;
                return;
            }

            CurrentInstallStatus = "Done";
            CurrentInstallProgress = 1.0f;
            InstanceRunning = true;
            _log.Info(nameof(RunCustomDataInstall), "Custom data installed, instance restarted", eventId);
            OnInstanceReady?.Invoke();

            _instanceProcess.WaitForExit();
            _instanceProcessIsRunning = false;
            InstanceRunning = false;
            _log.Info(nameof(RunCustomDataInstall), "EchokrauTTS instance stopped", eventId);
        }
        catch (Exception ex)
        {
            StopInstance(eventId);
            Installing = false;
            CurrentInstallStatus = $"Failed: {ex.Message}";
            CurrentInstallProgress = 0f;
            _log.Error(nameof(RunCustomDataInstall), $"Error while installing EchokrauTTS custom data: {ex}", eventId);
        }
    }

    private void MarkInstalled()
    {
        _config.EchokrauTts.LocalInstall = true;
        // LatestWrapperVersion, not the RemoteUrls baseline: after a successful update check this is
        // the tag of the release we actually downloaded, and tag + zip must stay paired.
        _config.EchokrauTts.InstalledWrapperVersion = LatestWrapperVersion;
        _config.FirstTime = false;
        _config.Save();
        RefreshUpdateStateAfterInstall();
        Installing = false;
        CurrentInstallStatus = "Done";
        CurrentInstallProgress = 1.0f;
    }

    /// <summary>
    /// Records the wrapper tag after an update. Unlike <see cref="MarkInstalled"/> this must NOT
    /// touch <c>LocalInstall</c>/<c>FirstTime</c> — an update only happens on an install that
    /// already exists, and the first-time wizard state is none of its business.
    /// </summary>
    private void MarkUpdated()
    {
        // LatestWrapperVersion, not the RemoteUrls baseline: after a successful update check this is
        // the tag of the release we actually downloaded, and tag + zip must stay paired.
        _config.EchokrauTts.InstalledWrapperVersion = LatestWrapperVersion;
        _config.Save();
        RefreshUpdateStateAfterInstall();
        Installing = false;
        CurrentInstallStatus = "Done";
        CurrentInstallProgress = 1.0f;
    }

    /// <summary>
    /// Puts the button back to "Check for updates" once an install/update wrote a new tag. Without
    /// this the state stays <see cref="WrapperUpdateState.UpdateAvailable"/> and the button keeps
    /// offering an install that already happened. <see cref="_foundRelease"/> is deliberately kept —
    /// it is still the newest release we know of, and it must stay paired with its download URL for
    /// a later reinstall.
    /// </summary>
    private void RefreshUpdateStateAfterInstall()
    {
        UpdateState = WrapperUpdatePolicy.StateAfterInstall();
        UpdateCheckError = string.Empty;
    }

    private string LaunchDescription(bool download, bool update)
    {
        if (update) return $"Updating EchokrauTTS wrapper to {_remoteUrls.Urls.EchokrauTtsVersion}";
        return download ? "Installing + starting EchokrauTTS" : "Starting EchokrauTTS instance";
    }

    /// <summary>Status text when the installer exited before the ready file appeared. A plain start
    /// (no download) shows nothing — there was no install phase to fail.</summary>
    private static string FailureStatus(bool download, bool update)
    {
        if (update) return "Failed: update did not complete";
        return download ? "Failed: install did not complete" : string.Empty;
    }

    /// <summary>
    /// Post-ready bookkeeping for the two download flavours. <b>An update must never reach
    /// <see cref="DownloadVoicePack"/></b>: the installer deliberately left <c>samples/</c> alone,
    /// and the pack service wipes that folder before unpacking — it would take the user's voices
    /// with it, which is exactly what the update promises not to do.
    /// </summary>
    private void CompleteDownloadPhase(bool download, bool update, EKEventId eventId)
    {
        if (update)
        {
            MarkUpdated();
            _log.Info(nameof(CompleteDownloadPhase),
                $"EchokrauTTS wrapper updated to {_config.EchokrauTts.InstalledWrapperVersion}", eventId);
            return;
        }
        if (!download) return;

        // Fresh install: seed the curated voice pack into echokrautts/samples (mirror of AllTalk's
        // install-time download) before marking complete — UNLESS the user supplied their own custom
        // voices, in which case they don't want the default pack (they install their voices via
        // "Install only custom data").
        if (string.IsNullOrWhiteSpace(_config.EchokrauTts.CustomVoicesUrl))
            DownloadVoicePack(eventId);
        else
            _log.Info(nameof(CompleteDownloadPhase),
                "Custom voices provided — skipping voice pack download.", eventId);
        MarkInstalled();
    }

    private void RunInstance(bool download, bool update, EKEventId eventId)
    {
        try
        {
            if (download)
            {
                Installing = true;
                CurrentInstallStatus = update
                    ? "Updating the EchokrauTTS wrapper (voices and models are kept)..."
                    : "Installing EchokrauTTS (downloads model + deps, may take a while)...";
                CurrentInstallProgress = 0.10f;
            }
            InstanceStarting = true;
            _log.Info(nameof(RunInstance), LaunchDescription(download, update), eventId);

            var installerExe = EnsureInstaller(eventId);
            var readyFile = Path.Join(Path.GetDirectoryName(installerExe), ReadyFileName);
            if (File.Exists(readyFile)) { try { File.Delete(readyFile); } catch { /* will be recreated */ } }

            _instanceProcess = new Process { StartInfo = BuildProcessInfo(download, update, installerExe) };
            _instanceProcess.Start();
            _instanceProcessIsRunning = true;

            // The bootstrap installs-then-serves in one long-running process: wait for the ready
            // file (written by the installer on the NDJSON 'ready' event), NOT for exit.
            while (!File.Exists(readyFile) && !_instanceProcess.HasExited)
                Thread.Sleep(2000);

            InstanceStarting = false;
            if (!File.Exists(readyFile))
            {
                _log.Warning(nameof(RunInstance), "Installer exited before EchokrauTTS became ready", eventId);
                Installing = false;
                CurrentInstallStatus = FailureStatus(download, update);
                InstanceRunning = false;
                _instanceProcessIsRunning = false;
                return;
            }

            CompleteDownloadPhase(download, update, eventId);

            InstanceRunning = true;
            _log.Info(nameof(RunInstance), "EchokrauTTS instance ready", eventId);
            OnInstanceReady?.Invoke();

            _instanceProcess.WaitForExit();
            _instanceProcessIsRunning = false;
            InstanceRunning = false;
            _log.Info(nameof(RunInstance), "EchokrauTTS instance stopped", eventId);
        }
        catch (Exception ex)
        {
            StopInstance(eventId);
            Installing = false;
            CurrentInstallStatus = $"Failed: {ex.Message}";
            CurrentInstallProgress = 0f;
            _log.Error(nameof(RunInstance), $"Error while running EchokrauTTS instance: {ex}", eventId);
        }
    }

    public void StopInstance(EKEventId eventId)
    {
        try
        {
            if (_instanceThread == null && _instanceProcess == null) return;

            _log.Info(nameof(StopInstance), "Stopping EchokrauTTS instance", eventId);
            InstanceStopping = true;

            // Best-effort graceful shutdown so the Python server tears down cleanly + frees the port.
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                http.PostAsync(_config.EchokrauTts.BaseUrl.TrimEnd('/') + _config.EchokrauTts.ShutdownPath,
                    new StringContent("")).GetAwaiter().GetResult();
            }
            catch { /* server may already be down — fall through to kill */ }

            var readyFile = Path.Join(_config.TtsInstallRoot, LocalInstallerProvisioner.InstallerFolderName, ReadyFileName);
            if (File.Exists(readyFile)) File.Delete(readyFile);

            InstanceRunning = false;
            InstanceStarting = false;
            _instanceProcessIsRunning = false;

            if (_instanceProcess is { HasExited: false })
                _instanceProcess.Kill(true);
            _instanceProcess?.Dispose();
            _instanceProcess = null;
            _instanceThread = null;
            InstanceStopping = false;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(StopInstance), $"Error while stopping EchokrauTTS instance: {ex}", eventId);
        }
    }

    public void Dispose() => StopInstance(new EKEventId(0, TextSource.Backend));
}
