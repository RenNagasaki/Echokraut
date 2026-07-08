using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echotools.Logging.Enums;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace Echokraut.Services;

/// <summary>
/// Manages the local AllTalk TTS process lifecycle (install, start, stop).
/// Fires OnInstanceReady when the backend is up so BackendService can connect.
/// </summary>
public class AlltalkInstanceService : IAlltalkInstanceService, IDisposable
{
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IGoogleDriveSyncService _googleDrive;
    private readonly IRemoteUrlService _remoteUrls;
    private readonly IClientState _clientState;
    private readonly IVoiceSampleExtractorService _voiceExtract;

    public event Action? OnInstanceReady;

    public bool Installing { get; private set; }
    public bool InstanceRunning { get; private set; }
    public bool InstanceStarting { get; private set; }
    public bool InstanceStopping { get; private set; }
    public bool IsWindows { get; private set; }
    public bool IsCudaInstalled { get; private set; }

    public string CurrentInstallStatus { get; private set; } = string.Empty;
    public float CurrentInstallProgress { get; private set; }

    private Task? _installThread;
    private Process? _installProcess;
    private Task? _instanceThread;
    private Process? _instanceProcess;
    private volatile bool _instanceProcessIsRunning;

    public AlltalkInstanceService(ILogService log, Configuration config, IGoogleDriveSyncService googleDrive,
        IRemoteUrlService remoteUrls, IClientState clientState, IVoiceSampleExtractorService voiceExtract)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _googleDrive = googleDrive ?? throw new ArgumentNullException(nameof(googleDrive));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _voiceExtract = voiceExtract ?? throw new ArgumentNullException(nameof(voiceExtract));

        IsWindows = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows;
        IsCudaInstalled = IsCudaInstalledCheck(new EKEventId(0, TextSource.Backend));
    }

    public void Install()
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        var (pathValid, _) = Windows.Native.NativeAlltalkBuilder.ValidateInstallPath(_config.TtsInstallRoot);
        if (!pathValid)
        {
            _log.Warning(nameof(Install), "Install path is invalid, aborting.", eventId);
            return;
        }
        try
        {
            _log.Info(nameof(Install), "Starting alltalk install process", eventId);
            _installThread = Task.Run(() =>
            {
                Installing = true;
                CurrentInstallStatus = "Preparing installer...";
                CurrentInstallProgress = 0.05f;
                try
                {
                var localInstallerLocation = CheckAndDownloadLocalInstaller(eventId);

                // Args: install <installFolder> <customModelUrl> <customVoicesUrl> <reinstall>
                //        <isWindows> <isWindows11> <alltalkUrl> <voicesUrl> <voices2Url>
                //        <msBuildToolsUrl> <xttsModelUrls(;-separated)>
                // Args: install <installFolder> <customModelUrl> <customVoicesUrl> <reinstall>
                //        <isWindows> <isWindows11> <alltalkUrl> <voicesUrl> <voices2Url>
                //        <msBuildToolsUrl> <xttsModelUrls(;-separated)> <cpuMode>
                var urls = _remoteUrls.Urls;
                var processInfo = new ProcessStartInfo(localInstallerLocation)
                {
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    ArgumentList =
                    {
                        "install",
                        _config.TtsInstallRoot,
                        _config.Alltalk.CustomModelUrl,
                        _config.Alltalk.CustomVoicesUrl,
                        "true",
                        IsWindows.ToString(),
                        _config.Alltalk.IsWindows11.ToString(),
                        urls.AlltalkUrl,
                        urls.VoicesUrl,
                        urls.Voices2Url,
                        urls.MsBuildToolsUrl,
                        string.Join(";", urls.XttsModelUrls),
                        _config.Alltalk.CpuMode.ToString(),
                    }
                };

                _installProcess = new Process { StartInfo = processInfo };
                _installProcess.Start();
                CurrentInstallStatus = "Installing AllTalk (downloads + setup, may take 10+ minutes)...";
                CurrentInstallProgress = 0.10f;
                _installProcess.WaitForExit();

                // Replace the legacy voices.zip / voices2.zip download with an on-the-fly
                // extract from the user's own FFXIV install. Output target is AllTalk's
                // canonical voices folder (alltalk_tts/voices/), which the extractor wipes
                // before writing so leftover files from a previous extract don't haunt the
                // voice list. StarterSetSamplesPerNpc samples per voice (decode + resample of
                // ~hundreds of NPCs once); user can rebuild with a different count later via the
                // Game Data Tools window.
                // When the user supplied their own custom voices, the installer already extracted
                // them into alltalk_tts/voices/ during the install step. Building the FFXIV starter
                // set here would WIPE that folder (the extractor clears voices/ before writing) and
                // replace the custom voices — so skip it. With no custom voices, build as usual.
                if (string.IsNullOrWhiteSpace(_config.Alltalk.CustomVoicesUrl))
                {
                    var alltalkFolder = TtsPaths.AllTalkRoot(_config.TtsInstallRoot);
                    var voicesDir = Path.Join(alltalkFolder, "voices");
                    _log.Info(nameof(Install),
                        $"Building voice starter set into {voicesDir} (this replaces the old voices.zip download)...",
                        eventId);
                    CurrentInstallStatus = "Building voice samples...";
                    CurrentInstallProgress = 0.50f;
                    // Subscribe to extractor progress for the duration of this run so the
                    // First-Time window's progress bar shows real "X/Y NPCs" granularity instead
                    // of the indeterminate sticky-50% during the opaque installer phase. Mapping:
                    // 0..1 from the extractor → 0.50..0.95 in our overall install bar.
                    Action<string, int, int> onExtractProgress = (label, current, total) =>
                    {
                        var ratio = total > 0 ? Math.Clamp((float)current / total, 0f, 1f) : 0f;
                        CurrentInstallProgress = 0.50f + ratio * 0.45f;
                        if (!string.IsNullOrEmpty(label))
                            CurrentInstallStatus = $"Voice samples — {label} ({current}/{total})";
                    };
                    try
                    {
                        _voiceExtract.ProgressChanged += onExtractProgress;
                        using var extractCts = new CancellationTokenSource();
                        _voiceExtract.RunAsync(_clientState.ClientLanguage,
                            samplesPerNpc: VoiceSampleExtractorService.StarterSetSamplesPerNpc, extractCts.Token,
                            outputRootOverride: alltalkFolder, outputSubfolder: "voices")
                            .GetAwaiter().GetResult();
                        _log.Info(nameof(Install), "Voice starter set ready.", eventId);
                    }
                    catch (Exception ex)
                    {
                        // Non-fatal: install still completes. User can re-run extract from the
                        // Game Data Tools window if voice samples are missing.
                        _log.Warning(nameof(Install),
                            $"Voice starter-set extraction failed during install: {ex.Message}. " +
                            $"Run it manually from Game Data Tools later.",
                            eventId);
                    }
                    finally
                    {
                        _voiceExtract.ProgressChanged -= onExtractProgress;
                    }
                }
                else
                {
                    _log.Info(nameof(Install),
                        "Custom voices provided — skipping FFXIV voice starter-set extraction.", eventId);
                }

                CurrentInstallStatus = "Finalizing...";
                CurrentInstallProgress = 0.95f;
                _config.Alltalk.BaseUrl = "http://127.0.0.1:7851";
                _config.Alltalk.LocalInstall = true;
                _config.FirstTime = false;
                _config.Save();
                Installing = false;
                CurrentInstallStatus = "Done";
                CurrentInstallProgress = 1.0f;
                _log.Info(nameof(Install), "Done!", eventId);

                if (_config.Alltalk.AutoStartLocalInstance)
                    StartInstance();
                }
                catch (Exception ex)
                {
                    // Errors raised inside Task.Run are otherwise swallowed (the outer catch
                    // below only sees synchronous failures of the Task-START, not of its body).
                    // Without this wrapper, an UnauthorizedAccessException from writing to a
                    // protected install path leaves the UI stuck at "Preparing installer..."
                    // with no error indication. Surface it via the same status text the outer
                    // catch uses so the First-Time wizard's progress label tells the user.
                    _log.Error(nameof(Install),
                        $"Install task failed: {ex}", eventId);
                    CurrentInstallStatus = $"Failed: {ex.Message}";
                    CurrentInstallProgress = 0f;
                    Installing = false;
                }
            });
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Install), $"Error while installing alltalk locally: {ex}", eventId);
            CurrentInstallStatus = $"Failed: {ex.Message}";
            CurrentInstallProgress = 0f;
            StopInstall(eventId);
        }
    }

    private void StopInstall(EKEventId eventId)
    {
        try
        {
            if (_installThread != null)
            {
                _log.Info(nameof(StopInstall), "Stopping alltalk install process", eventId);
                Installing = false;
                if (_installProcess is { HasExited: false })
                    _installProcess.Kill(true);
                _installProcess?.Dispose();
                _installProcess = null;
                _installThread = null;
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(StopInstall), $"Error while stopping alltalk install: {ex}", eventId);
        }
    }

    public void StartInstance()
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        var (pathValid, _) = Windows.Native.NativeAlltalkBuilder.ValidateInstallPath(_config.TtsInstallRoot);
        if (!pathValid)
        {
            _log.Warning(nameof(StartInstance), "Install path is invalid, aborting.", eventId);
            return;
        }
        try
        {
            if (!(!_instanceProcessIsRunning && _instanceProcess == null && _instanceThread == null))
                StopInstance(eventId);

            _instanceThread = Task.Run(() =>
            {
                try
                {
                    InstanceStarting = true;
                    _log.Info(nameof(StartInstance), "Starting alltalk instance process", eventId);

                    var localInstallerLocation = CheckAndDownloadLocalInstaller(eventId);

                    var processInfo = new ProcessStartInfo(localInstallerLocation)
                    {
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        ArgumentList = { "start", _config.TtsInstallRoot, IsWindows.ToString() }
                    };

                    _instanceProcess = new Process { StartInfo = processInfo };
                    _instanceProcess.Start();
                    _instanceProcessIsRunning = true;

                    while (!File.Exists(Path.Join(Path.GetDirectoryName(localInstallerLocation), "Ready.txt")))
                        Thread.Sleep(2000);

                    InstanceStarting = false;
                    InstanceRunning = true;
                    _log.Info(nameof(StartInstance), "Instance ready", eventId);
                    OnInstanceReady?.Invoke();

                    _instanceProcess.WaitForExit();

                    _instanceProcessIsRunning = false;
                    InstanceStarting = false;
                    InstanceRunning = false;
                    _log.Info(nameof(StartInstance), "Instance stopped", eventId);
                }
                catch (Exception ex)
                {
                    StopInstance(eventId);
                    _log.Error(nameof(StartInstance), $"Error while running alltalk instance: {ex}", eventId);
                }
            });
        }
        catch (Exception ex)
        {
            _log.Error(nameof(StartInstance), $"Error while running alltalk instance: {ex}", eventId);
            StopInstance(eventId);
        }
    }

    public void StopInstance(EKEventId eventId)
    {
        try
        {
            if (_instanceThread != null)
            {
                _log.Info(nameof(StopInstance), "Stopping alltalk instance process", eventId);
                var readyFile = Path.Join(_config.TtsInstallRoot, "EchokrautLocalInstaller", "Ready.txt");
                if (File.Exists(readyFile))
                    File.Delete(readyFile);

                InstanceRunning = false;
                InstanceStarting = false;
                InstanceStopping = true;
                _instanceProcessIsRunning = false;

                if (_instanceProcess is { HasExited: false })
                    _instanceProcess.Kill(true);
                _instanceProcess?.Dispose();
                _instanceProcess = null;
                _instanceThread = null;
                InstanceStopping = false;
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(StopInstance), $"Error while stopping alltalk instance: {ex}", eventId);
        }
    }

    public Task InstallCustomData(EKEventId eventId, bool installProcess = true)
    {
        var (pathValid, _) = Windows.Native.NativeAlltalkBuilder.ValidateInstallPath(_config.TtsInstallRoot);
        if (!pathValid)
        {
            _log.Warning(nameof(InstallCustomData), "Install path is invalid, aborting.", eventId);
            return Task.CompletedTask;
        }
        try
        {
            var wasRunning = InstanceRunning || InstanceStarting;
            var shouldRestart = wasRunning || (!installProcess && _config.Alltalk.AutoStartLocalInstance);

            _log.Info(nameof(InstallCustomData), $"Starting custom data install (wasRunning={wasRunning}, shouldRestart={shouldRestart})", eventId);

            // The installer's named pipe shutdown kills any running installer (and its AllTalk instance).
            // If shouldRestart, the installer will start AllTalk again after installing.
            _installThread = Task.Run(() =>
            {
                Installing = true;
                if (wasRunning)
                {
                    InstanceRunning = false;
                    InstanceStarting = false;
                }

                var localInstallerLocation = CheckAndDownloadLocalInstaller(eventId);

                // Args: installcustomdata <installFolder> <customModelUrl> <customVoicesUrl> <isWindows> <shouldRestart>
                var processInfo = new ProcessStartInfo(localInstallerLocation)
                {
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    ArgumentList =
                    {
                        "installcustomdata",
                        _config.TtsInstallRoot,
                        _config.Alltalk.CustomModelUrl,
                        _config.Alltalk.CustomVoicesUrl,
                        IsWindows.ToString(),
                        shouldRestart.ToString(),
                    }
                };

                _installProcess = new Process { StartInfo = processInfo };
                _installProcess.Start();

                if (shouldRestart)
                {
                    // Poll for Ready.txt — installer will start AllTalk after custom data install
                    while (!_installProcess.HasExited &&
                           !File.Exists(Path.Join(Path.GetDirectoryName(localInstallerLocation), "Ready.txt")))
                        Thread.Sleep(2000);

                    Installing = false;
                    InstanceRunning = true;
                    _log.Info(nameof(InstallCustomData), "Custom data installed, instance restarted", eventId);
                    OnInstanceReady?.Invoke();

                    // Keep tracking the process (it stays alive while AllTalk runs)
                    _instanceProcess = _installProcess;
                    _installProcess = null;
                    _instanceProcessIsRunning = true;

                    _instanceProcess.WaitForExit();
                    _instanceProcessIsRunning = false;
                    InstanceRunning = false;
                    _log.Info(nameof(InstallCustomData), "Instance stopped", eventId);
                }
                else
                {
                    _installProcess.WaitForExit();
                    Installing = false;
                    _log.Info(nameof(InstallCustomData), "Custom data installed", eventId);
                }
            });
        }
        catch (Exception ex)
        {
            _log.Error(nameof(InstallCustomData), $"Error while installing custom data: {ex}", eventId);
        }

        return Task.CompletedTask;
    }

    public bool IsCudaInstalledCheck(EKEventId eventId)
    {
        if (IsWindows)
        {
            _log.Debug(nameof(IsCudaInstalledCheck), "On Windows, CUDA check skipped", eventId);
            return true;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = "nvcc",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                _log.Debug(nameof(IsCudaInstalledCheck), "CUDA install found", eventId);
                return true;
            }

            _log.Debug(nameof(IsCudaInstalledCheck), "CUDA install not found", eventId);
            return false;
        }
        catch (Exception ex)
        {
            _log.Error(nameof(IsCudaInstalledCheck), $"Error while checking for CUDA install: {ex}", eventId);
            return false;
        }
    }

    private string CheckAndDownloadLocalInstaller(EKEventId eventId)
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

    public void Dispose()
    {
        StopInstall(new EKEventId(0, TextSource.Backend));
        StopInstance(new EKEventId(0, TextSource.Backend));
    }
}
