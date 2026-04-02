using Echotools.Logging.Services;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

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

    public event Action? OnInstanceReady;

    public bool Installing { get; private set; }
    public bool InstanceRunning { get; private set; }
    public bool InstanceStarting { get; private set; }
    public bool InstanceStopping { get; private set; }
    public bool IsWindows { get; private set; }
    public bool IsCudaInstalled { get; private set; }

    private Task? _installThread;
    private Process? _installProcess;
    private Task? _instanceThread;
    private Process? _instanceProcess;
    private volatile bool _instanceProcessIsRunning;

    public AlltalkInstanceService(ILogService log, Configuration config, IGoogleDriveSyncService googleDrive, IRemoteUrlService remoteUrls)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _googleDrive = googleDrive ?? throw new ArgumentNullException(nameof(googleDrive));
        _remoteUrls = remoteUrls ?? throw new ArgumentNullException(nameof(remoteUrls));

        IsWindows = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows;
        IsCudaInstalled = IsCudaInstalledCheck(new EKEventId(0, TextSource.Backend));
    }

    public void Install()
    {
        var eventId = new EKEventId(0, TextSource.Backend);
        try
        {
            _log.Info(nameof(Install), "Starting alltalk install process", eventId);
            _installThread = Task.Run(() =>
            {
                Installing = true;
                var localInstallerLocation = CheckAndDownloadLocalInstaller(eventId);

                // Args: install <installFolder> <customModelUrl> <customVoicesUrl> <reinstall>
                //        <isWindows> <isWindows11> <alltalkUrl> <voicesUrl> <voices2Url>
                //        <msBuildToolsUrl> <xttsModelUrls(;-separated)>
                var urls = _remoteUrls.Urls;
                var processInfo = new ProcessStartInfo(localInstallerLocation)
                {
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    ArgumentList =
                    {
                        "install",
                        _config.Alltalk.LocalInstallPath,
                        _config.Alltalk.CustomModelUrl,
                        _config.Alltalk.CustomVoicesUrl,
                        "true",
                        IsWindows.ToString(),
                        _config.Alltalk.IsWindows11.ToString(),
                        urls.AlltalkUrl,
                        urls.VoicesUrl,
                        urls.Voices2Url,
                        urls.MsBuildToolsUrl,
                        string.Join(";", urls.XttsModelUrls)
                    }
                };

                _installProcess = new Process { StartInfo = processInfo };
                _installProcess.Start();
                _installProcess.WaitForExit();

                _config.Alltalk.BaseUrl = "http://127.0.0.1:7851";
                _config.Alltalk.LocalInstall = true;
                _config.FirstTime = false;
                _config.Save();
                Installing = false;
                _log.Info(nameof(Install), "Done!", eventId);

                if (_config.Alltalk.AutoStartLocalInstance)
                    StartInstance();
            });
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Install), $"Error while installing alltalk locally: {ex}", eventId);
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
                        ArgumentList = { "start", _config.Alltalk.LocalInstallPath, IsWindows.ToString() }
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
                var readyFile = Path.Join(_config.Alltalk.LocalInstallPath, "EchokrautLocalInstaller", "Ready.txt");
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

    public async Task InstallCustomData(EKEventId eventId, bool installProcess = true)
    {
        try
        {
            if (!installProcess)
                StopInstance(eventId);

            var installFolder = _config.Alltalk.LocalInstallPath;
            var alltalkFolder = Path.Join(installFolder, Constants.ALLTALKFOLDERNAME);
            var modelFolder = Path.Join(alltalkFolder, "models", "xtts");
            var voicesFile = Path.Join(alltalkFolder, "voices.zip");
            var voicesFolder = Path.Join(alltalkFolder, "voices");

            if (!string.IsNullOrWhiteSpace(_config.Alltalk.CustomModelUrl))
            {
                _log.Info(nameof(InstallCustomData), "Downloading custom model", eventId);
                using var client = new HttpClient();
                try
                {
                    var modelFolderName = "echokraut_trained";
                    modelFolder = Path.Join(modelFolder, modelFolderName);
                    if (Directory.Exists(modelFolder))
                        Directory.Delete(modelFolder, true);
                    Directory.CreateDirectory(modelFolder);

                    var modelFile = modelFolder + ".zip";
                    var downloadUrl = _googleDrive.CheckForGoogleAndConvertToDirectDownloadLink(
                        _config.Alltalk.CustomModelUrl, out bool isGoogle);
                    var response = await client.GetAsync(downloadUrl);
                    if (isGoogle)
                        response = _googleDrive.DownloadGoogleDrive(downloadUrl, response, client);

                    using (var fs = new FileStream(modelFile, FileMode.Create, FileAccess.Write))
                        await response.Content.CopyToAsync(fs);

                    ZipFile.ExtractToDirectory(modelFile, modelFolder, true);
                    File.Delete(modelFile);

                    var ttsEnginesFile = Path.Join(alltalkFolder, "system", "tts_engines", "tts_engines.json");
                    dynamic? configEngines = JsonConvert.DeserializeObject(File.ReadAllText(ttsEnginesFile));
                    if (configEngines != null)
                    {
                        configEngines["engine_loaded"] = "xtts";
                        configEngines["selected_model"] = $"xtts - {modelFolderName}";
                        File.WriteAllText(ttsEnginesFile, JsonConvert.SerializeObject(configEngines));
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(nameof(InstallCustomData), $"Error while downloading custom model, skipping: {ex}", eventId);
                }
            }
            else
                _log.Info(nameof(InstallCustomData), "No custom model found, skipping", eventId);

            if (!string.IsNullOrWhiteSpace(_config.Alltalk.CustomVoicesUrl))
            {
                _log.Info(nameof(InstallCustomData), "Downloading custom voices", eventId);
                using var client = new HttpClient();
                try
                {
                    var downloadUrl = _googleDrive.CheckForGoogleAndConvertToDirectDownloadLink(
                        _config.Alltalk.CustomVoicesUrl, out bool isGoogle);
                    var response = await client.GetAsync(downloadUrl);
                    if (isGoogle)
                        response = _googleDrive.DownloadGoogleDrive(downloadUrl, response, client);

                    using (var fs = new FileStream(voicesFile, FileMode.Create, FileAccess.Write))
                        await response.Content.CopyToAsync(fs);

                    if (Directory.Exists(voicesFolder))
                        Directory.Delete(voicesFolder, true);

                    ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                    File.Delete(voicesFile);
                }
                catch (Exception ex)
                {
                    _log.Error(nameof(InstallCustomData), $"Error while downloading custom voices, skipping: {ex}", eventId);
                }
            }
            else
                _log.Info(nameof(InstallCustomData), "No custom voices found, skipping", eventId);

            if (_config.Alltalk.AutoStartLocalInstance && !installProcess)
                StartInstance();
        }
        catch (Exception ex)
        {
            _log.Error(nameof(InstallCustomData), $"Error while installing custom data: {ex}", eventId);
        }
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
        var localInstallerLocation = Path.Join(_config.Alltalk.LocalInstallPath, "EchokrautLocalInstaller");
        var localInstallerExeLocation = Path.Join(localInstallerLocation, "EchokrautLocalInstaller.exe");

        if (!File.Exists(localInstallerExeLocation))
        {
            _log.Info(nameof(CheckAndDownloadLocalInstaller), "Downloading local installer", eventId);
            using var http = new HttpClient();
            var installerUrl = _remoteUrls.Urls.InstallerUrl;
            string fileName = Path.GetFileName(new Uri(installerUrl).LocalPath);
            string zipPath = Path.Combine(_config.Alltalk.LocalInstallPath, fileName);
            var bytes = http.GetByteArrayAsync(installerUrl).Result;
            File.WriteAllBytes(zipPath, bytes);
            Directory.CreateDirectory(localInstallerLocation);
            ZipFile.ExtractToDirectory(zipPath, localInstallerLocation, overwriteFiles: true);
        }

        _log.Debug(nameof(CheckAndDownloadLocalInstaller), $"Location: {localInstallerExeLocation}", eventId);
        return localInstallerExeLocation;
    }

    public void Dispose()
    {
        StopInstall(new EKEventId(0, TextSource.Backend));
        StopInstance(new EKEventId(0, TextSource.Backend));
    }
}
