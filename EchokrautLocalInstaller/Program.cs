using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

public class Program
{
    delegate bool ConsoleCtrlDelegate(int ctrlType);
    static readonly ConsoleCtrlDelegate _handler = Handler;

    [DllImport("Kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    static bool Handler(int ctrlType)
    {
        Dispose();
        return false;
    }
    
    static Process? InstallProcess = null;
    static Process? InstanceProcess = null;
    static bool Installing;
    static bool InstanceRunning;
    static bool InstanceStarting;
    static bool InstanceStopping;
    static bool IsWindows;
    static bool InstallProcessIsRunning;
    static bool InstanceProcessIsRunning = false;

    public static void Main(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", "EchokrautLocalInstaller", PipeDirection.Out);
            client.Connect(200);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("shutdown");
        }
        catch
        { }
        
        _ = Task.Run(async () =>
        { 
            while (true)
            {
                using var server = new NamedPipeServerStream("EchokrautLocalInstaller");
                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server);
                var msg = await reader.ReadLineAsync();

                if (msg == "shutdown")
                    Environment.Exit(0);
            }
        });

        SetConsoleCtrlHandler(_handler, true);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Console.WriteLine("ProcessExit – Stopping All");
            Dispose();
        };

        if (args.Length > 1)
        {
            switch (args[0])
            {
                case "start":
                    IsWindows = Convert.ToBoolean(args[2]);
                    var installFolder = Path.Join(args[1], Constants.ALLTALKFOLDERNAME);
                    StartInstance(installFolder);
                    break;
                case "install":
                    IsWindows = Convert.ToBoolean(args[5]);
                    Install(args[1],
                            args[2],
                            args[3],
                            Convert.ToBoolean(args[4]),
                            Convert.ToBoolean(args[6]));
                    break;
            }
        }
    }

    static void Install(string installFolder, string customModelUrl, string customVoicesUrl, bool reinstall, bool isWindows11)
    {
        try
        {
            Console.WriteLine($"Installing into {installFolder}");
            Installing = true;
            var installFile = Path.Join(installFolder, "alltalk_tts.zip");
            var installMSBTFile = Path.Join(installFolder, "vs_BuildTools.exe");
            var alltalkFolderNameWrong = Path.GetFileNameWithoutExtension(Constants.ALLTALKURL);
            var alltalkFolderWrong = Path.Join(installFolder, alltalkFolderNameWrong);
            var alltalkFolder = Path.Join(installFolder, Constants.ALLTALKFOLDERNAME);
            var modelFolder = Path.Join(alltalkFolder, "models", "xtts", "xtts2.0.3");
            var voicesFile = Path.Join(alltalkFolder, "voices.zip");
            var voices2File = Path.Join(alltalkFolder, "voices2.zip");
            var confignewFile = Path.Join(alltalkFolder, "confignew.json");
            var ttsEnginesFile = Path.Join(alltalkFolder, "system", "tts_engines", "tts_engines.json");
            var modelSettingsFile = Path.Join(alltalkFolder, "system", "tts_engines", "xtts", "model_settings.json");

            try
            {
                InstallProcess = new Process();
                if (reinstall && Directory.Exists(alltalkFolder))
                {
                    try {
                        Directory.Delete(alltalkFolder, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while installing alltalk locally: {ex}");
                    }
                }

                if (!Directory.Exists(installFolder))
                    Directory.CreateDirectory(installFolder);

                #region Prerequisites
                if (IsWindows)
                {
                    Console.WriteLine($"Downloading vs_BuildTools.exe");
                    using (var client = new HttpClient())
                    {
                        var response = client.GetByteArrayAsync(Constants.MSBUILDTOOLSURL);
                        File.WriteAllBytes(installMSBTFile, response.Result);
                    }

                    var winSdk = isWindows11
                                     ? Constants.MSBUILDTOOLSWIN11SDK
                                     : Constants.MSBUILDTOOLSWIN10SDK;
                    Console.WriteLine($"Installing vs_BuildTools.exe");
                    var process = new Process();
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.FileName = installMSBTFile;
                    process.StartInfo.Arguments = $"--quiet --add {Constants.MSBUILDTOOLSMSVC} --add {winSdk}";
                    process.Start();
                    process.WaitForExit();
                    File.Delete(installMSBTFile);
                }
                #endregion

                Console.WriteLine($"Downloading alltalk_tts.zip");
                using(var client = new HttpClient())
                {
                    var response = client.GetByteArrayAsync(Constants.ALLTALKURL);
                    File.WriteAllBytes(installFile, response.Result);
                }

                Console.WriteLine($"Extracting alltalk_tts.zip");
                System.IO.Compression.ZipFile.ExtractToDirectory(installFile, installFolder, true);
                Directory.Move(alltalkFolderWrong, alltalkFolder);
                File.Delete(installFile);

                Console.WriteLine($"Downloading xtts2.0.3 model");
                using(var client = new HttpClient())
                {
                    if (!Directory.Exists(modelFolder))
                        Directory.CreateDirectory(modelFolder);

                    foreach (var xttsUrl in Constants.XTTS203URLS)
                    {
                        var uri = new Uri(xttsUrl);
                        var fileName = Path.GetFileName(uri.LocalPath);
                        Console.WriteLine($"Downloading {fileName}");
                        var response = client.GetByteArrayAsync(uri);
                        File.WriteAllBytes(Path.Join(modelFolder, fileName), response.Result);
                    }
                }

                Console.WriteLine($"Downloading voices.zip");
                Console.WriteLine($"{voicesFile}");
                using(var client = new HttpClient())
                {
                    try
                    {
                        var response = client.GetByteArrayAsync(Constants.VOICESURL);
                        File.WriteAllBytes(voicesFile, response.Result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while downloading voices.zip: {ex}");
                    }
                }

                Console.WriteLine($"Extracting voices.zip");
                System.IO.Compression.ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                File.Delete(voicesFile);

                Console.WriteLine($"Downloading voices2.zip");
                Console.WriteLine($"{voices2File}");
                using(var client = new HttpClient())
                {
                    try
                    {
                        var response = client.GetByteArrayAsync(Constants.VOICES2URL);
                        File.WriteAllBytes(voices2File, response.Result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while downloading voices2.zip: {ex}");
                    }
                }

                Console.WriteLine($"Extracting voices2.zip");
                System.IO.Compression.ZipFile.ExtractToDirectory(voices2File, alltalkFolder, true);
                File.Delete(voices2File);

                Console.WriteLine($"Starting install process");
                InstallProcess.StartInfo.UseShellExecute = false;
                InstallProcess.StartInfo.CreateNoWindow = true;
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

                Console.WriteLine($"Calling atsetup");
                InstallProcess.Start();
                InstallProcessIsRunning = true;
                InstallProcess.WaitForExit();
                InstallProcessIsRunning = false;

                Console.WriteLine($"Install process ExitCode: {InstallProcess.ExitCode}");
                if (InstallProcess.ExitCode == 0)
                {
                    if (IsWindows)
                    {
                        Console.WriteLine($"Installing espeak-ng");
                        CallCMD(IsWindows, 
                                "",
                          $"msiexec /i \"{Path.Join(alltalkFolder, "system", "espeak-ng", "espeak-ng-X64.msi")}\" /quiet /norestart",
                          "Espeak-NG");
                    }

                    Console.WriteLine("Modifying configs:");
                    dynamic config = JsonConvert.DeserializeObject(File.ReadAllText(confignewFile));
                    if (config != null)
                    {
                        config["gradio_port_number"] = 7852;
                        config["firstrun_model"] = false;
                        config["api_def"]["api_port_number"] = 7851;
                        config["tgwui"]["tgwui_lowvram_enabled"] = true;

                        File.WriteAllText(confignewFile, JsonConvert.SerializeObject(config));
                    }

                    dynamic configEngines = JsonConvert.DeserializeObject(File.ReadAllText(ttsEnginesFile));
                    if (configEngines != null)
                    {
                        configEngines["engine_loaded"] = "xtts";
                        configEngines["selected_model"] = "xtts - xtts2.0.3";
                        File.WriteAllText(ttsEnginesFile, JsonConvert.SerializeObject(configEngines));
                    }

                    dynamic configEngine = JsonConvert.DeserializeObject(File.ReadAllText(modelSettingsFile));
                    if (configEngine != null)
                    {
                        configEngine["settings"]["lowvram_enabled"] = false;
                        configEngine["settings"]["deepspeed_enabled"] = true;
                        File.WriteAllText(modelSettingsFile, JsonConvert.SerializeObject(configEngine));
                    }
                    InstallCustomData(alltalkFolder, customModelUrl, customVoicesUrl).Wait();

                    Console.WriteLine($"Done!");
                    Installing = false;
                }
            }
            catch (OperationCanceledException ex)
            {
                StopInstall();
                Console.WriteLine($"Stopped alltalk install process");
            }
            catch (Exception ex)
            {
                StopInstall();
                Console.WriteLine($"Error while installing alltalk locally: {ex}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while installing alltalk locally: {ex}");
            StopInstall();
        }
    }

    static void StopInstall()
    {
        try
        {
                Console.WriteLine($"Stopping alltalk install process");
                Installing = false;
                InstallProcessIsRunning = false;
                if (InstallProcess is { HasExited: false })
                {
                    InstallProcess?.Kill(true);
                }
                InstallProcess?.Dispose();
                InstallProcess = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping alltalk install: {ex}");
        }
    }

    static void StartInstance(string installFolder)
    {
        try
        {
            if (!(!InstanceProcessIsRunning && InstanceProcess == null))
                StopInstance();
            
            try
            {
                InstanceStarting = true;
                InstanceProcess = new Process();
                var alltalkFolder = installFolder;
                Console.WriteLine($"Starting alltalk instance process");

                var cmdExe = IsWindows
                                 ? "cmd.exe"
                                 : "/bin/bash";
                var processInfo = new ProcessStartInfo(cmdExe)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                InstanceProcess = new Process();
                InstanceProcess.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null && !string.IsNullOrEmpty(e.Data))
                    {
                        var cleanedMessage = CleanAnsi(e.Data);
                        if (Constants.ALLTALKDEBUGLOGCOLOR.Any(item => e.Data.Contains(item)))
                            Console.ForegroundColor = ConsoleColor.Green;
                        else if (Constants.ALLTALKERRORLOGCOLOR.Any(item => e.Data.Contains(item)))
                            Console.ForegroundColor = ConsoleColor.Red;
                        else
                            Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(cleanedMessage);
                        Console.ResetColor();

                        if (e.Data.Contains("Server Ready"))
                        {
                            Console.WriteLine("Alltalk instance is ready");
                            InstanceStarting = false;
                            InstanceRunning = true;
                            var readyFile = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Ready.txt");
                            if (!File.Exists(readyFile))
                                File.WriteAllText(readyFile, " ");
                        }
                    }
                };
                InstanceProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null && !string.IsNullOrEmpty(e.Data))
                        Console.WriteLine(CleanAnsi(e.Data));
                };
                InstanceProcess.StartInfo = processInfo;
                InstanceProcess.Start();
                InstanceProcess.BeginOutputReadLine();
                InstanceProcess.BeginErrorReadLine();
                InstanceProcessIsRunning = true;

                using (var sw = InstanceProcess.StandardInput)
                {
                    if (sw.BaseStream.CanWrite)
                    {
                        var command = "";
                        if (IsWindows)
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

                        command = $"python -u {Path.Join(alltalkFolder, "script.py")}";
                        sw.WriteLine(command);
                    }
                }

                InstanceProcess.WaitForExit();

                InstanceProcessIsRunning = false;
                InstanceStarting = false;
                InstanceRunning = false;
            }
            catch (Exception ex)
            {
                StopInstance();
                Console.WriteLine($"Error while running alltalk instance: {ex}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while running alltalk instance: {ex}");
            StopInstance();
        }
    }

    static void StopInstance()
    {
        try
        {
            Console.WriteLine($"Stopping alltalk instance process");
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
            InstanceStopping = false;
            var readyFile = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Ready.txt");
            if (File.Exists(readyFile))
                File.Delete(readyFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while stopping alltalk instance: {ex}");
        }
    }

    static async Task InstallCustomData(
        string alltalkFolder, string customModelUrl, string customVoicesUrl,
        bool installProcess = true)
    {
        try
        {
            if (!installProcess)
                StopInstance();

            var modelFolder = Path.Join(alltalkFolder, "models", "xtts");
            var voicesFile = Path.Join(alltalkFolder, "voices.zip");
            var voicesFolder = Path.Join(alltalkFolder, "voices");
            if (!string.IsNullOrWhiteSpace(customModelUrl))
            {
                Console.WriteLine($"Downloading custom model");
                Console.WriteLine($"{customVoicesUrl}");
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
                                customModelUrl, out bool isGoogle);
                        Console.WriteLine($"{downloadUrl}");
                        var response = await client.GetAsync(downloadUrl);

                        if (isGoogle)
                            response = GoogleDriveHelper.DownloadGoogleDrive(downloadUrl, response, client);

                        using (var fs = new FileStream(modelFile, FileMode.Create, FileAccess.Write))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        Console.WriteLine($"Extracting custom model");
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
                        Console.WriteLine($"Error while downloading custom model, skipping: {ex}");
                    }
                }
            }
            else
                Console.WriteLine($"No custom model found, skipping");

            if (!string.IsNullOrWhiteSpace(customVoicesUrl))
            {
                Console.WriteLine($"Downloading custom voices");
                Console.WriteLine($"{customVoicesUrl}");
                using (var client = new HttpClient())
                {
                    try
                    {
                        var downloadUrl =
                            GoogleDriveHelper.CheckForGoogleAndConvertToDirectDownloadLink(
                                customVoicesUrl, out bool isGoogle);
                        Console.WriteLine($"{downloadUrl}");
                        var response = await client.GetAsync(downloadUrl);

                        if (isGoogle)
                            response = GoogleDriveHelper.DownloadGoogleDrive(downloadUrl, response, client);

                        using (var fs = new FileStream(voicesFile, FileMode.Create, FileAccess.Write))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        Console.WriteLine($"Deleting existing voices");
                        if (Directory.Exists(voicesFolder))
                            Directory.Delete(voicesFolder, true);

                        Console.WriteLine($"Extracting custom voices");
                        System.IO.Compression.ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                        File.Delete(voicesFile);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while downloading custom voices, skipping: {ex}");
                    }
                }
            }
            else
                Console.WriteLine($"No custom voices found, skipping");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while installing custom data: {ex}");
        }
    }

    static string CleanAnsi(string input)
    {
        return Regex.Replace(input, @"\x1B\[[0-9;]*[mK]", "").Replace(" ", "  ");
    }

    static void CallCMD(bool isWindows, string exePath, string command, string methodExtra)
    {
        try
        {
            var process = new Process();

            if (isWindows)
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = @$"/c {exePath} {command}";
            }
            else
            {
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = @$"-c {exePath} {command}";
            }

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            Console.WriteLine(@$"Calling command: '{exePath} {command}'");
            process.Start();

            while (!process.HasExited)
            {
                string output = process.StandardOutput.ReadLine();
                Console.WriteLine(output);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    static void Dispose()
    {
        StopInstall();
        StopInstance();
    }
}
