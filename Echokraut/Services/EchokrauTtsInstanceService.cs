using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Helper.Functional.Scd;
using Echotools.Logging.Enums;
using System;
using System.Diagnostics;
using System.IO;
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
    private readonly IVoiceSampleExtractorService _voiceExtract;

    public event Action? OnInstanceReady;

    public bool Installing { get; private set; }
    public bool InstanceRunning { get; private set; }
    public bool InstanceStarting { get; private set; }
    public bool InstanceStopping { get; private set; }
    public string CurrentInstallStatus { get; private set; } = string.Empty;
    public float CurrentInstallProgress { get; private set; }

    private bool IsWindows { get; }

    private Task? _instanceThread;
    private Process? _instanceProcess;
    private volatile bool _instanceProcessIsRunning;

    public EchokrauTtsInstanceService(ILogService log, Configuration config, IRemoteUrlService remoteUrls,
        IClientState clientState, IVoiceSampleExtractorService voiceExtract)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));
        _clientState = clientState ?? throw new ArgumentNullException(nameof(clientState));
        _voiceExtract = voiceExtract ?? throw new ArgumentNullException(nameof(voiceExtract));
        IsWindows = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows;
    }

    /// <summary>Download the wrapper + bootstrap (install) + serve. Marks LocalInstall on ready.</summary>
    public void Install() => Launch(download: true);

    /// <summary>Run the wrapper bootstrap (install-if-needed) + serve, without re-downloading.</summary>
    public void StartInstance() => Launch(download: false);

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

    private void Launch(bool download)
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        var (pathValid, _) = Windows.Native.NativeAlltalkBuilder.ValidateInstallPath(_config.TtsInstallRoot);
        if (!pathValid)
        {
            _log.Warning(nameof(Launch), "Install path is invalid, aborting.", eventId);
            return;
        }
        if (download && string.IsNullOrWhiteSpace(_remoteUrls.Urls.EchokrauTtsUrl))
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
            _instanceThread = Task.Run(() => RunInstance(download, eventId));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Launch), $"Error while running EchokrauTTS instance: {ex}", eventId);
            StopInstance(eventId);
        }
    }

    // echokrautts <installRoot> <echokrauTtsUrl-or-empty> <isWindows> <port> <language> <parentPid>
    private ProcessStartInfo BuildProcessInfo(bool download, string installerExe) => new(installerExe)
    {
        UseShellExecute = true,
        CreateNoWindow = false,
        ArgumentList =
        {
            "echokrautts",
            _config.TtsInstallRoot,
            download ? _remoteUrls.Urls.EchokrauTtsUrl : string.Empty,
            IsWindows.ToString(),
            Port().ToString(),
            LanguageCode(_clientState.ClientLanguage),
            Environment.ProcessId.ToString(),
        }
    };

    /// <summary>
    /// Seed the EchokrauTTS voice starter set on a fresh local install — the same on-the-fly extract
    /// from the user's own FFXIV audio that <see cref="AlltalkInstanceService"/> does, but targeting
    /// EchokrauTTS's samples folder (<c>echokrautts/samples/</c>) instead of AllTalk's voices folder.
    /// Non-fatal: the install still completes if it fails; the user can rebuild from the Game Data
    /// Tools window. Extractor progress (0..1) maps onto the install bar's 0.50..0.95 band.
    /// </summary>
    private void ExtractStarterSet(EKEventId eventId)
    {
        var ekRoot = TtsPaths.EchokrauTtsRoot(_config.TtsInstallRoot);
        var samplesDir = TtsPaths.EchokrauTtsSamples(_config.TtsInstallRoot);
        _log.Info(nameof(ExtractStarterSet), $"Building voice starter set into {samplesDir}...", eventId);
        CurrentInstallStatus = "Building voice samples...";
        CurrentInstallProgress = 0.50f;

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
            _voiceExtract.RunAsync(_clientState.ClientLanguage, samplesPerNpc: 1, extractCts.Token,
                outputRootOverride: ekRoot, outputSubfolder: TtsPaths.EchokrauTtsSamplesFolder)
                .GetAwaiter().GetResult();
            _log.Info(nameof(ExtractStarterSet), "Voice starter set ready.", eventId);
        }
        catch (Exception ex)
        {
            // Non-fatal: install still completes. User can re-run extract from Game Data Tools.
            _log.Warning(nameof(ExtractStarterSet),
                $"Voice starter-set extraction failed during install: {ex.Message}. " +
                $"Run it manually from Game Data Tools later.", eventId);
        }
        finally
        {
            _voiceExtract.ProgressChanged -= onExtractProgress;
        }
    }

    private void MarkInstalled()
    {
        _config.EchokrauTts.LocalInstall = true;
        _config.FirstTime = false;
        _config.Save();
        Installing = false;
        CurrentInstallStatus = "Done";
        CurrentInstallProgress = 1.0f;
    }

    private void RunInstance(bool download, EKEventId eventId)
    {
        try
        {
            if (download)
            {
                Installing = true;
                CurrentInstallStatus = "Installing EchokrauTTS (downloads model + deps, may take a while)...";
                CurrentInstallProgress = 0.10f;
            }
            InstanceStarting = true;
            _log.Info(nameof(RunInstance), download ? "Installing + starting EchokrauTTS" : "Starting EchokrauTTS instance", eventId);

            var installerExe = EnsureInstaller(eventId);
            var readyFile = Path.Join(Path.GetDirectoryName(installerExe), ReadyFileName);
            if (File.Exists(readyFile)) { try { File.Delete(readyFile); } catch { /* will be recreated */ } }

            _instanceProcess = new Process { StartInfo = BuildProcessInfo(download, installerExe) };
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
                CurrentInstallStatus = download ? "Failed: install did not complete" : string.Empty;
                InstanceRunning = false;
                _instanceProcessIsRunning = false;
                return;
            }

            if (download)
            {
                // Fresh install: seed the voice starter set into echokrautts/samples (mirror of
                // AllTalk's install-time extract) before marking complete.
                ExtractStarterSet(eventId);
                MarkInstalled();
            }

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
