using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.Enums;
using Echokraut.Helper.API;
using Newtonsoft.Json;

namespace Echokraut.Helper.Functional
{
    public static class AlltalkInstanceHelper
    {
        public static bool Installing;
        public static bool InstanceRunning;
        public static bool InstanceStarting;
        public static bool InstanceStopping;
        public static bool IsWindows;
        public static bool IsCudaInstalled;
        private static Task? InstallThread;
        private static Process? InstallProcess;
        private static Task? InstanceThread;
        private static Process? InstanceProcess;
        private static bool InstanceProcessIsRunning;

        public static void Initialize()
        {
            IsWindows = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows;
            IsCudaInstalled = IsCudaInstalledCheck(new EKEventId(0, TextSource.Backend));
        }
        public static void Install()
        {
            var eventId = LogHelper.Start("Install", TextSource.Backend);
            try
            {
                LogHelper.Info("InstallInstance", $"Starting alltalk install process", eventId);

                InstallThread = Task.Run(() =>
                {
                    Installing = true;
                    var localInstallerLocation = CheckAndDownloadLocalInstaller(eventId);
                    
                    var processInfo = new ProcessStartInfo(localInstallerLocation)
                    {
                        UseShellExecute = true, 
                        CreateNoWindow = false,
                        ArgumentList = { 
                            "install", 
                            Plugin.Configuration.Alltalk.LocalInstallPath, 
                            Plugin.Configuration.Alltalk.CustomModelUrl, 
                            Plugin.Configuration.Alltalk.CustomVoicesUrl, 
                            "true", 
                            Plugin.Configuration.Alltalk.AutoStartLocalInstance.ToString(), 
                            IsWindows.ToString(), 
                            Plugin.Configuration.Alltalk.IsWindows11.ToString() 
                        }
                    };

                    InstallProcess = new Process();
                    InstallProcess.StartInfo = processInfo;
                    InstallProcess.Start();
                    InstallProcess.WaitForExit();

                    LogHelper.Info("Install", $"Done!", eventId);
                    Plugin.Configuration.Alltalk.BaseUrl = "http://127.0.0.1:7851";
                    Plugin.Configuration.Alltalk.LocalInstall = true;
                    Plugin.Configuration.FirstTime = false;
                    Plugin.Configuration.Save();
                    Installing = false;
                    LogHelper.End("Install", eventId);
                    if (Plugin.Configuration.Alltalk.AutoStartLocalInstance)
                        StartInstance();
                });
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while installing alltalk locally: {ex}", eventId);
                StopInstall(eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            }
        }

        internal static void StopInstall(EKEventId eventId)
        {
            try
            {
                if (InstallThread != null)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod().Name, $"Stopping alltalk install process",
                                   eventId);
                    Installing = false;
                    if (InstallProcess is { HasExited: false })
                    {
                        InstallProcess?.Kill(true);
                    }
                    InstallProcess?.Dispose();
                    InstallProcess = null;
                    InstallThread = null;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while stopping alltalk install: {ex}", eventId);
            }
        }

        internal static void StartInstance()
        {
            var eventId = LogHelper.Start("StartInstance", TextSource.Backend);
            try
            {
                if (!(!InstanceProcessIsRunning && InstanceProcess == null && InstanceThread == null))
                    StopInstance(eventId);
                InstanceThread = Task.Run(() =>
                {
                    try
                    {
                        InstanceStarting = true;
                        InstanceProcess = new Process();
                        LogHelper.Info("StartInstance", $"Starting alltalk instance process", eventId);

                        var localInstallerLocation = CheckAndDownloadLocalInstaller(eventId);
                        
                        var processInfo = new ProcessStartInfo(localInstallerLocation)
                        {
                            UseShellExecute = true, 
                            CreateNoWindow = false,
                            ArgumentList = { 
                                "start", 
                                Plugin.Configuration.Alltalk.LocalInstallPath, 
                                IsWindows.ToString()
                            }
                        };

                        InstanceProcess = new Process();
                        InstanceProcess.StartInfo = processInfo;
                        InstanceProcess.Start();
                        InstanceProcessIsRunning = true;

                        while (!File.Exists(Path.Join(Path.GetDirectoryName(localInstallerLocation), "Ready.txt")))
                        {
                            Thread.Sleep(2000);
                        }

                        InstanceStarting = false;
                        InstanceRunning = true;
                        BackendHelper.Initialize(Plugin.Configuration.BackendSelection);
                        
                        InstanceProcess.WaitForExit();

                        InstanceProcessIsRunning = false;
                        InstanceStarting = false;
                        InstanceRunning = false;
                        LogHelper.End("StartInstance", eventId);
                    }
                    catch (Exception ex)
                    {
                        StopInstance(eventId);
                        LogHelper.Error(MethodBase.GetCurrentMethod().Name,
                                        $"Error while running alltalk instance: {ex}", eventId);
                        LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while running alltalk instance: {ex}",
                                eventId);
                StopInstance(eventId);
                LogHelper.End(MethodBase.GetCurrentMethod().Name, eventId);
            }
        }

        internal static void StopInstance(EKEventId eventId)
        {
            try
            {
                if (InstanceThread != null)
                {
                    LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, $"Stopping alltalk instance process",
                                   eventId);
                    var readyFile = Path.Join(Plugin.Configuration.Alltalk.LocalInstallPath, "EchokrautLocalInstaller", "Ready.txt");
                    if (File.Exists(readyFile))
                        File.Delete(readyFile);
                    InstanceRunning = false; 
                    InstanceStarting = false;
                    InstanceStopping = true; 
                    InstanceProcessIsRunning = false;
                    if (InstanceProcess is { HasExited: false })
                    {
                        InstanceProcess.Kill(true);
                    }
                    InstanceProcess?.Dispose();
                    InstanceProcess = null;
                    InstanceThread = null;
                    InstanceStopping = false;
                } 
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, $"Error while stopping alltalk instance: {ex}", eventId);
            }
        }

        public static void Dispose()
        {
            StopInstall(new EKEventId(0, TextSource.Backend));
            StopInstance(new EKEventId(0, TextSource.Backend));
        }

        public static bool IsCudaInstalledCheck(EKEventId eventId)
        {
            if (IsWindows)
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"On windows, CUDA check skipped", eventId);
                return true;
            }

            string command = "which";
            string arg = "nvcc";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arg,
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
                    LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"CUDA install found", eventId);
                    return true;
                }

                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"CUDA install not found", eventId);
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Error while checking for CUDA install: {ex}", eventId);
            }

            return false;
        }

        public static async Task InstallCustomData(EKEventId eventId, bool installProcess = true)
        {
            try
            {
                if (!installProcess)
                    StopInstance(eventId);

                var installFolder = Plugin.Configuration.Alltalk.LocalInstallPath;
                var alltalkFolder = Path.Join(installFolder, Constants.ALLTALKFOLDERNAME);
                var modelFolder = Path.Join(alltalkFolder, "models", "xtts");
                var voicesFile = Path.Join(alltalkFolder, "voices.zip");
                var voicesFolder = Path.Join(alltalkFolder, "voices");
                if (!string.IsNullOrWhiteSpace(Plugin.Configuration.Alltalk.CustomModelUrl))
                {
                    LogHelper.Info("InstallCustomData", $"Downloading custom model", eventId);
                    LogHelper.Debug("InstallCustomData", $"{Plugin.Configuration.Alltalk.CustomVoicesUrl}", eventId);
                    using (var client = new HttpClient())
                    {
                        try
                        {
                            var modelFolderName = "echokraut_trained";
                            modelFolder = Path.Join(modelFolder, modelFolderName);
                            if (Directory.Exists(modelFolder))
                                Directory.Delete(modelFolder, true);

                            Directory.CreateDirectory(modelFolder);
                            var modelFile = modelFolder + ".zip";
                            var downloadUrl =
                                GoogleDriveHelper.CheckForGoogleAndConvertToDirectDownloadLink(
                                    Plugin.Configuration.Alltalk.CustomModelUrl, out bool isGoogle);
                            LogHelper.Debug("InstallCustomData", $"{downloadUrl}", eventId);
                            var response = await client.GetAsync(downloadUrl);

                            if (isGoogle)
                                response = GoogleDriveHelper.DownloadGoogleDrive(downloadUrl, response, client);

                            using (var fs = new FileStream(modelFile, FileMode.Create, FileAccess.Write))
                            {
                                await response.Content.CopyToAsync(fs);
                            }

                            LogHelper.Info("InstallCustomData", $"Extracting custom model", eventId);
                            System.IO.Compression.ZipFile.ExtractToDirectory(modelFile, modelFolder, true);
                            File.Delete(modelFile);

                            var ttsEnginesFile = Path.Join(alltalkFolder, "system", "tts_engines", "tts_engines.json");
                            dynamic configEngines = JsonConvert.DeserializeObject(File.ReadAllText(ttsEnginesFile));
                            if (configEngines != null)
                            {
                                configEngines["engine_loaded"] = "xtts";
                                configEngines["selected_model"] = $"xtts - {modelFolderName}";
                                File.WriteAllText(ttsEnginesFile, JsonConvert.SerializeObject(configEngines));
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error("InstallCustomData",
                                            $"Error while downloading custom model, skipping: {ex}", eventId);
                        }
                    }
                }
                else
                    LogHelper.Info("InstallCustomData", $"No custom model found, skipping", eventId);

                if (!string.IsNullOrWhiteSpace(Plugin.Configuration.Alltalk.CustomVoicesUrl))
                {
                    LogHelper.Info("InstallCustomData", $"Downloading custom voices", eventId);
                    LogHelper.Debug("InstallCustomData",
                                    $"{Plugin.Configuration.Alltalk.CustomVoicesUrl}", eventId);
                    using (var client = new HttpClient())
                    {
                        try
                        {
                            var downloadUrl = GoogleDriveHelper.CheckForGoogleAndConvertToDirectDownloadLink(Plugin.Configuration.Alltalk.CustomVoicesUrl, out bool isGoogle);
                            LogHelper.Debug("InstallCustomData",
                                            $"{downloadUrl}", eventId);
                            var response = await client.GetAsync(downloadUrl);

                            if (isGoogle)
                                response = GoogleDriveHelper.DownloadGoogleDrive(downloadUrl, response, client);

                            using (var fs = new FileStream(voicesFile, FileMode.Create, FileAccess.Write))
                            {
                                await response.Content.CopyToAsync(fs);
                            }

                            LogHelper.Info("InstallCustomData", $"Deleting existing voices", eventId);
                            if (Directory.Exists(voicesFolder))
                                Directory.Delete(voicesFolder, true);

                            LogHelper.Info("InstallCustomData", $"Extracting custom voices", eventId);
                            System.IO.Compression.ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                            File.Delete(voicesFile);
                        }
                        catch (Exception ex)
                        {
                            LogHelper.Error("InstallCustomData",
                                            $"Error while downloading custom voices, skipping: {ex}", eventId);
                        }
                    }
                }
                else
                    LogHelper.Info("InstallCustomData", $"No custom voices found, skipping", eventId);

                if (Plugin.Configuration.Alltalk.AutoStartLocalInstance && !installProcess)
                    StartInstance();
            }
            catch (Exception ex)
            {
                LogHelper.Error("InstallCustomData", $"Error while installing custom data: {ex}", eventId);
            }
        }

        private static string CheckAndDownloadLocalInstaller(EKEventId eventId)
        {
            var localInstallerLocation = Path.Join(Plugin.Configuration.Alltalk.LocalInstallPath, "EchokrautLocalInstaller");
            var localInstallerExeLocation = Path.Join(localInstallerLocation, "EchokrautLocalInstaller.exe");

            if (!File.Exists(localInstallerExeLocation))
            {
                LogHelper.Info("CheckAndDownloadLocalInstaller", $"Downloading local installer", eventId);
                using var http = new HttpClient();

                string fileName = Path.GetFileName(new Uri(Constants.EKLOCALINSTALLERURL).LocalPath);
                string zipPath = Path.Combine(Plugin.Configuration.Alltalk.LocalInstallPath, fileName);

                var bytes = http.GetByteArrayAsync(Constants.EKLOCALINSTALLERURL).Result;
                File.WriteAllBytes(zipPath, bytes);

                Directory.CreateDirectory(localInstallerLocation);

                ZipFile.ExtractToDirectory(zipPath, localInstallerLocation, overwriteFiles: true);
            }

            LogHelper.Debug("InstallInstance", $"Location: {localInstallerExeLocation}", eventId);
            return localInstallerExeLocation;
        }
    }
}
