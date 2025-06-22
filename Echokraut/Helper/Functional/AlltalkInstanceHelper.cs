using Echokraut.DataClasses;
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
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
        public static Task? InstallThread;
        public static CancellationTokenSource? InstallThreadCts;
        public static Task? InstanceThread;
        private static Process? InstallProcess;
        private static bool InstallProcessIsRunning;
        private static Process? InstanceProcess;
        private static bool InstanceProcessIsRunning;

        public static void Initialize()
        {
            IsWindows = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows;
            IsCudaInstalled = IsCudaInstalledCheck(new EKEventId(0, TextSource.Backend));
        }
        public static void Install(bool reinstall)
        {
            var eventId = LogHelper.Start("Install", TextSource.Backend);
            try
            {
                if (!Installing && !InstallProcessIsRunning && InstallProcess == null && InstallThread == null)
                {
                    Installing = true;
                    var installFolder = Plugin.Configuration.Alltalk.LocalInstallPath;
                    var installFile = Path.Join(installFolder, "alltalk_tts.zip");
                    var alltalkFolderName = Path.GetFileNameWithoutExtension(Constants.ALLTALKURL);
                    var alltalkFolderWrong = Path.Join(installFolder, alltalkFolderName);
                    var alltalkFolder = Path.Join(installFolder, Constants.ALLTALKFOLDERNAME);
                    var modelFolder = Path.Join(alltalkFolder, "models", "xtts", "xtts2.0.3");
                    var voicesFile = Path.Join(alltalkFolder, "voices.zip");
                    var voices2File = Path.Join(alltalkFolder, "voices2.zip");
                    var confignewFile = Path.Join(alltalkFolder, "confignew.json");
                    var ttsEnginesFile = Path.Join(alltalkFolder, "system", "tts_engines", "tts_engines.json");
                    var modelSettingsFile = Path.Join(alltalkFolder, "system", "tts_engines", "xtts", "model_settings.json");

                    InstallThreadCts = new CancellationTokenSource();
                    InstallThread = Task.Run(() =>
                    {
                        try
                        {
                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            InstallProcess = new Process();
                            if (reinstall && Directory.Exists(alltalkFolder))
                            {
                                try {
                                    Directory.Delete(alltalkFolder, true);
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Error("Install - AT", $"Error while installing alltalk locally: {ex}", eventId);
                                }
                            }

                            if (!Directory.Exists(installFolder))
                                Directory.CreateDirectory(installFolder);


                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - AT", $"Downloading alltalk_tts.zip", eventId);
                            using(var client = new HttpClient())
                            {
                                var response = client.GetByteArrayAsync(Constants.ALLTALKURL);
                                File.WriteAllBytes(installFile, response.Result);
                            }

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - AT", $"Extracting alltalk_tts.zip", eventId);
                            System.IO.Compression.ZipFile.ExtractToDirectory(installFile, installFolder, true);
                            Directory.Move(alltalkFolderWrong, alltalkFolder);
                            File.Delete(installFile);

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - MD", $"Downloading xtts2.0.3 model", eventId);
                            using(var client = new HttpClient())
                            {
                                if (!Directory.Exists(modelFolder))
                                    Directory.CreateDirectory(modelFolder);

                                foreach (var xttsUrl in Constants.XTTS203URLS)
                                {
                                    var uri = new Uri(xttsUrl);
                                    var fileName = Path.GetFileName(uri.LocalPath);
                                    LogHelper.Info("Install - MD", $"Downloading {fileName}", eventId);
                                    var response = client.GetByteArrayAsync(uri);
                                    File.WriteAllBytes(Path.Join(modelFolder, fileName), response.Result);
                                }
                            }

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - VC", $"Downloading voices.zip", eventId);
                            LogHelper.Debug("Install - VC", $"{voicesFile}", eventId);
                            using(var client = new HttpClient())
                            {
                                try
                                {
                                    var response = client.GetByteArrayAsync(Constants.VOICESURL);
                                    File.WriteAllBytes(voicesFile, response.Result);
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Error("Install - VC", $"Error while downloading voices.zip: {ex}", eventId);
                                }
                            }

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - VC", $"Extracting voices.zip", eventId);
                            System.IO.Compression.ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                            File.Delete(voicesFile);

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - VC2", $"Downloading voices2.zip", eventId);
                            LogHelper.Debug("Install - VC2", $"{voices2File}", eventId);
                            using(var client = new HttpClient())
                            {
                                try
                                {
                                    var response = client.GetByteArrayAsync(Constants.VOICES2URL);
                                    File.WriteAllBytes(voices2File, response.Result);
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.Error("Install - VC2", $"Error while downloading voices2.zip: {ex}", eventId);
                                }
                            }

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - VC2", $"Extracting voices2.zip", eventId);
                            System.IO.Compression.ZipFile.ExtractToDirectory(voices2File, alltalkFolder, true);
                            File.Delete(voices2File);

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Info("Install - PC", $"Starting install process", eventId);
                            InstallProcess.StartInfo.UseShellExecute = true;
                            InstallProcess.StartInfo.CreateNoWindow=false;
                            if (IsWindows)
                            {
                                InstallProcess.StartInfo.FileName = "cmd.exe";
                                InstallProcess.StartInfo.Arguments =
                                    $"/C start \"atsetup\" /wait {Path.Join(alltalkFolder, "atsetup.bat")} -silent";
                            }
                            else
                            {
                                InstallProcess.StartInfo.FileName = "/bin/bash";
                                InstallProcess.StartInfo.Arguments =
                                    $"-c \"setsid bash -c '{Path.Join(alltalkFolder, "atsetup.sh")} -silent' & wait $!\"";
                            }

                            LogHelper.Debug("Install - PC", $"Calling atsetup", eventId);
                            InstallProcess.Start();
                            InstallProcessIsRunning = true;
                            InstallProcess.WaitForExit();
                            InstallProcessIsRunning = false;

                            InstallThreadCts.Token.ThrowIfCancellationRequested();
                            LogHelper.Debug("Install - PC", $"Install process ExitCode: {InstallProcess.ExitCode}", eventId);
                            if (InstallProcess.ExitCode == 0)
                            {
                                if (IsWindows)
                                {
                                    LogHelper.Info("Install - ES", $"Installing espeak-ng", eventId);
                                    CMDHelper.CallCMD(eventId, "",
                                                      $"msiexec /i \"{Path.Join(alltalkFolder, "system", "espeak-ng", "espeak-ng-X64.msi")}\" /quiet /norestart",
                                                      "Espeak-NG");
                                }

                                InstallThreadCts.Token.ThrowIfCancellationRequested();
                                LogHelper.Info("Install - CF", "Modifying configs:", eventId);
                                dynamic config = JsonConvert.DeserializeObject(File.ReadAllText(confignewFile));
                                if (config != null)
                                {
                                    config["gradio_port_number"] = 7852;
                                    config["firstrun_model"] = false;
                                    config["api_def"]["api_port_number"] = 7851;
                                    config["tgwui"]["tgwui_lowvram_enabled"] = true;

                                    File.WriteAllText(confignewFile, JsonConvert.SerializeObject(config));
                                }

                                InstallThreadCts.Token.ThrowIfCancellationRequested();
                                dynamic configEngines = JsonConvert.DeserializeObject(File.ReadAllText(ttsEnginesFile));
                                if (configEngines != null)
                                {
                                    configEngines["engine_loaded"] = "xtts";
                                    configEngines["selected_model"] = "xtts - xtts2.0.3";
                                    File.WriteAllText(ttsEnginesFile, JsonConvert.SerializeObject(configEngines));
                                }

                                InstallThreadCts.Token.ThrowIfCancellationRequested();
                                dynamic configEngine = JsonConvert.DeserializeObject(File.ReadAllText(modelSettingsFile));
                                if (configEngine != null)
                                {
                                    configEngine["settings"]["lowvram_enabled"] = false;
                                    configEngine["settings"]["deepspeed_enabled"] = true;
                                    File.WriteAllText(modelSettingsFile, JsonConvert.SerializeObject(configEngine));
                                }

                                InstallThreadCts.Token.ThrowIfCancellationRequested();
                                InstallCustomData(eventId).Wait();

                                InstallThreadCts.Token.ThrowIfCancellationRequested();
                                LogHelper.Info("Install", $"Done!", eventId);
                                Plugin.Configuration.Alltalk.BaseUrl = "http://127.0.0.1:7851";
                                Plugin.Configuration.Alltalk.LocalInstall = true;
                                Plugin.Configuration.FirstTime = false;
                                Plugin.Configuration.Save();
                                Installing = false;
                                LogHelper.End("Install", eventId);
                                if (Plugin.Configuration.Alltalk.AutoStartLocalInstance)
                                    StartInstance();
                            }
                        }
                        catch (OperationCanceledException ex)
                        {
                            StopInstall(eventId);
                            LogHelper.Error("Install", $"Stopped alltalk install process", eventId);
                            LogHelper.End("Install", eventId);
                        }
                        catch (Exception ex)
                        {
                            StopInstall(eventId);
                            LogHelper.Error("Install", $"Error while installing alltalk locally: {ex}", eventId);
                            LogHelper.End("Install", eventId);
                        }
                    }, InstallThreadCts.Token);
                }
                else
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Previous thread still running or not cleaned up correctly. Please contact Support!", eventId);
                }
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
                    InstallProcessIsRunning = false;
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
                if (!InstanceProcessIsRunning && InstanceProcess == null && InstanceThread == null)
                {
                    InstanceThread = Task.Run(() =>
                    {
                        try
                        {
                            InstanceStarting = true;
                            InstanceProcess = new Process();
                            var alltalkFolder = Path.Join(Plugin.Configuration.Alltalk.LocalInstallPath,
                                                          Constants.ALLTALKFOLDERNAME);
                            LogHelper.Info("StartInstance", $"Starting alltalk instance process", eventId);

                            var cmdExe = Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows
                                             ? "cmd.exe"
                                             : "/bin/bash";
                            var processInfo = new ProcessStartInfo(cmdExe)
                            {
                                RedirectStandardInput = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true // auf true setzen, wenn du kein Konsolenfenster willst
                            };

                            InstanceProcess = new Process();
                            InstanceProcess.OutputDataReceived += (sender, e) =>
                            {
                                if (e.Data != null && !string.IsNullOrEmpty(e.Data))
                                {
                                    var cleanedMessage = CMDHelper.CleanAnsi(e.Data);
                                    if (Constants.ALLTALKDEBUGLOGCOLOR.Any(item => e.Data.Contains(item)))
                                        LogHelper.Debug("StartInstance", cleanedMessage, eventId);
                                    else if (Constants.ALLTALKERRORLOGCOLOR.Any(item => e.Data.Contains(item)))
                                        LogHelper.Debug("StartInstance", cleanedMessage, eventId);
                                    else
                                        LogHelper.Info("StartInstance", cleanedMessage, eventId);

                                    if (e.Data.Contains("Server Ready"))
                                    {
                                        //LogHelper.Info("StartInstance", "Alltalk instance is ready", eventId);
                                        InstanceStarting = false;
                                        InstanceRunning = true;
                                        BackendHelper.SetBackendType(TTSBackends.Alltalk);
                                    }
                                }
                            };
                            InstanceProcess.ErrorDataReceived += (sender, e) =>
                            {
                                if (e.Data != null && !string.IsNullOrEmpty(e.Data))
                                    LogHelper.Error("StartInstance", CMDHelper.CleanAnsi(e.Data), eventId);
                            };
                            InstanceProcess.StartInfo = processInfo;
                            InstanceProcess.Start();
                            InstanceProcess.BeginOutputReadLine();
                            InstanceProcess.BeginErrorReadLine();
                            InstanceProcessIsRunning = true;

                            // Eingabebefehle an cmd.exe senden
                            using (var sw = InstanceProcess.StandardInput)
                            {
                                if (sw.BaseStream.CanWrite)
                                {
                                    var command = "";
                                    if (Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows)
                                    {
                                        command =
                                            $"\"{Path.Join(alltalkFolder, "alltalk_environment", "conda", "condabin", "conda.bat")}\" activate \"{Path.Join(alltalkFolder, "alltalk_environment", "env")}\"";
                                        sw.WriteLine(command);
                                    }
                                    else
                                    {
                                        command =
                                            $"source \"{Path.Join(alltalkFolder, "alltalk_environment", "conda", "etc", "profile.d", "conda.sh")}\"";
                                        sw.WriteLine(command);
                                        command =
                                            $"activate \"{Path.Join(alltalkFolder, "alltalk_environment", "env")}\"";
                                        sw.WriteLine(command);
                                    }

                                    // Python-Skript ausführen
                                    command = $"python -u {Path.Join(alltalkFolder, "script.py")}";
                                    sw.WriteLine(command);
                                }
                            }

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
                else
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod().Name, "Previous thread still running or not cleaned up correctly. Please contact Support!", eventId);
                }
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
                    InstanceRunning = false;
                    InstanceStarting = false;
                    InstanceStopping = true;
                    InstanceProcessIsRunning = false;
                    if (InstanceProcess is { HasExited: false })
                    {
                        InstanceProcess.CancelOutputRead();
                        InstanceProcess.CancelErrorRead();
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
                                GoogleDriveLinkHelper.CheckForGoogleAndConvertToDirectDownloadLink(
                                    Plugin.Configuration.Alltalk.CustomModelUrl, out bool isGoogle);
                            LogHelper.Debug("InstallCustomData", $"{downloadUrl}", eventId);
                            var response = await client.GetAsync(downloadUrl);

                            if (isGoogle)
                                response = GoogleDriveLinkHelper.DownloadGoogleDrive(downloadUrl, response, client);

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
                            var downloadUrl = GoogleDriveLinkHelper.CheckForGoogleAndConvertToDirectDownloadLink(Plugin.Configuration.Alltalk.CustomVoicesUrl, out bool isGoogle);
                            LogHelper.Debug("InstallCustomData",
                                            $"{downloadUrl}", eventId);
                            var response = await client.GetAsync(downloadUrl);

                            if (isGoogle)
                                response = GoogleDriveLinkHelper.DownloadGoogleDrive(downloadUrl, response, client);

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
    }
}
