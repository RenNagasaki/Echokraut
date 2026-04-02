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
    static bool IsWindows;
    static bool InstanceProcessIsRunning = false;
    static bool InstallProcessStarted = false;

    static readonly string LogFilePath = Path.Join(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        "EchokrautLocalInstaller.log");
    static readonly object LogLock = new();

    static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(message);
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch { /* don't let logging failures break the installer */ }
    }

    static void Log(string message, ConsoleColor color)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch { }
    }

    static async Task DownloadFileAsync(HttpClient client, string url, string destPath, string label)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        long downloaded = 0;
        const int barWidth = 40;
        var sw = Stopwatch.StartNew();
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloaded += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var pct = (double)downloaded / totalBytes.Value;
                var filled = (int)(pct * barWidth);
                var empty = barWidth - filled;
                var bar = new string('=', filled) + (empty > 0 ? ">" + new string(' ', empty - 1) : "");
                var mbDown = downloaded / 1048576.0;
                var mbTotal = totalBytes.Value / 1048576.0;
                Console.Write($"\r  {label}: [{bar}] {pct:P0}  {mbDown:F1}/{mbTotal:F1} MB");
            }
            else
            {
                var mbDown = downloaded / 1048576.0;
                Console.Write($"\r  {label}: {mbDown:F1} MB downloaded...");
            }
        }

        sw.Stop();
        var finalMb = downloaded / 1048576.0;
        var speed = sw.Elapsed.TotalSeconds > 0 ? finalMb / sw.Elapsed.TotalSeconds : 0;
        Console.WriteLine();
        Log($"{label}: {finalMb:F1} MB downloaded in {sw.Elapsed.TotalSeconds:F1}s ({speed:F1} MB/s)");
    }

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
            Log("ProcessExit – Stopping All");
            Dispose();
        };
        
        Log($"EchokrautLocalInstaller started. Args ({args.Length}): [{string.Join(", ", args.Select((a, i) => $"[{i}]={a}"))}]");

        if (args.Length > 1)
        {
            switch (args[0])
            {
                case "start":
                    Log($"Mode: start | installFolder={args[1]} | isWindows={args[2]}");
                    IsWindows = Convert.ToBoolean(args[2]);
                    var installFolder = Path.Join(args[1], Constants.ALLTALKFOLDERNAME);
                    StartInstance(installFolder);
                    break;
                case "install":
                    // Args: install <installFolder> <customModelUrl> <customVoicesUrl> <reinstall>
                    //        <isWindows> <isWindows11> <alltalkUrl> <voicesUrl> <voices2Url>
                    //        <msBuildToolsUrl> <xttsModelUrls(;-separated)>
                    Log($"Mode: install | installFolder={args[1]} | customModelUrl={args[2]} | customVoicesUrl={args[3]} | reinstall={args[4]} | isWindows={args[5]} | isWindows11={args[6]}");
                    Log($"URLs: alltalkUrl={args[7]} | voicesUrl={args[8]} | voices2Url={args[9]} | msBuildToolsUrl={args[10]} | xttsModelUrls={args[11]}");
                    IsWindows = Convert.ToBoolean(args[5]);
                    Install(args[1],
                            args[2],
                            args[3],
                            Convert.ToBoolean(args[4]),
                            Convert.ToBoolean(args[6]),
                            args[7],
                            args[8],
                            args[9],
                            args[10],
                            args[11].Split(';'));
                    break;
            }
        }
    }

    static void Install(string installFolder, string customModelUrl, string customVoicesUrl,
        bool reinstall, bool isWindows11,
        string alltalkUrl, string voicesUrl, string voices2Url, string msBuildToolsUrl, string[] xttsModelUrls)
    {
        try
        {
            Log($"Installing into {installFolder}");
            Log($"Args: reinstall={reinstall}, isWindows={IsWindows}, isWindows11={isWindows11}");
            var installFile = Path.Join(installFolder, "alltalk_tts.zip");
            var installMSBTFile = Path.Join(installFolder, "vs_BuildTools.exe");
            var alltalkFolderNameWrong = Path.GetFileNameWithoutExtension(alltalkUrl);
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
                        Log($"Error while installing alltalk locally: {ex}");
                    }
                }

                if (!Directory.Exists(installFolder))
                    Directory.CreateDirectory(installFolder);

                #region Prerequisites
                if (IsWindows || isWindows11)
                {
                    Log($"Downloading vs_BuildTools.exe");
                    using (var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(30) })
                    {
                        DownloadFileAsync(client, msBuildToolsUrl, installMSBTFile, "vs_BuildTools.exe").Wait();
                    }

                    var winSdk = isWindows11
                                     ? Constants.MSBUILDTOOLSWIN11SDK
                                     : Constants.MSBUILDTOOLSWIN10SDK;
                    Log($"Installing vs_BuildTools.exe");
                    var process = new Process();
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.FileName = installMSBTFile;
                    process.StartInfo.Arguments = $"--quiet --add {Constants.MSBUILDTOOLSMSVC} --add {winSdk}";
                    process.Start();
                    process.WaitForExit();
                    Log($"vs_BuildTools.exe ExitCode: {process.ExitCode}");
                    File.Delete(installMSBTFile);
                }
                #endregion

                Log($"Downloading alltalk_tts.zip from {alltalkUrl}");
                using(var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(30) })
                {
                    DownloadFileAsync(client, alltalkUrl, installFile, "alltalk_tts.zip").Wait();
                }

                Log($"Extracting alltalk_tts.zip");
                System.IO.Compression.ZipFile.ExtractToDirectory(installFile, installFolder, true);
                Log($"Moving {alltalkFolderWrong} -> {alltalkFolder}");
                Directory.Move(alltalkFolderWrong, alltalkFolder);
                File.Delete(installFile);

                Log($"Downloading xtts2.0.3 model");
                using(var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(30) })
                {
                    if (!Directory.Exists(modelFolder))
                        Directory.CreateDirectory(modelFolder);

                    foreach (var xttsUrl in xttsModelUrls)
                    {
                        var uri = new Uri(xttsUrl);
                        var fileName = Path.GetFileName(uri.LocalPath);
                        Log($"Downloading {fileName}");
                        DownloadFileAsync(client, xttsUrl, Path.Join(modelFolder, fileName), fileName).Wait();
                    }
                }

                Log($"Downloading voices.zip to {voicesFile}");
                using(var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(30) })
                {
                    try
                    {
                        DownloadFileAsync(client, voicesUrl, voicesFile, "voices.zip").Wait();
                    }
                    catch (Exception ex)
                    {
                        Log($"Error while downloading voices.zip: {ex}");
                    }
                }

                Log($"Extracting voices.zip");
                System.IO.Compression.ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                File.Delete(voicesFile);

                Log($"Downloading voices2.zip to {voices2File}");
                using(var client = new HttpClient() { Timeout = TimeSpan.FromMinutes(30) })
                {
                    try
                    {
                        DownloadFileAsync(client, voices2Url, voices2File, "voices2.zip").Wait();
                    }
                    catch (Exception ex)
                    {
                        Log($"Error while downloading voices2.zip: {ex}");
                    }
                }

                Log($"Extracting voices2.zip");
                System.IO.Compression.ZipFile.ExtractToDirectory(voices2File, alltalkFolder, true);
                File.Delete(voices2File);

                Log($"Configuring InstallProcess");
                InstallProcess.StartInfo.UseShellExecute = false;
                InstallProcess.StartInfo.CreateNoWindow = true;
                InstallProcess.StartInfo.RedirectStandardOutput = true;
                InstallProcess.StartInfo.RedirectStandardError = true;
                if (IsWindows)
                {
                    var batPath = Path.Join(alltalkFolder, "atsetup.bat");
                    InstallProcess.StartInfo.FileName = "cmd.exe";
                    InstallProcess.StartInfo.Arguments =
                        $"/C start \"atsetup\" /wait {batPath} -silent";
                    Log($"InstallProcess FileName: cmd.exe");
                    Log($"InstallProcess Arguments: /C start \"atsetup\" /wait {batPath} -silent");
                    Log($"atsetup.bat exists: {File.Exists(batPath)}");
                    if (File.Exists(batPath))
                        Log($"atsetup.bat size: {new FileInfo(batPath).Length} bytes");
                }
                else
                {
                    var shPath = Path.Join(alltalkFolder, "atsetup.sh");
                    InstallProcess.StartInfo.FileName = "/bin/bash";
                    InstallProcess.StartInfo.Arguments =
                        $"-c \"setsid bash -c '{shPath} -silent' & wait $!\"";
                    Log($"InstallProcess FileName: /bin/bash");
                    Log($"InstallProcess Arguments: -c \"setsid bash -c '{shPath} -silent' & wait $!\"");
                    Log($"atsetup.sh exists: {File.Exists(shPath)}");
                }

                Log($"Starting InstallProcess (calling atsetup)...");
                var sw = Stopwatch.StartNew();
                InstallProcess.Start();
                InstallProcessStarted = true;
                Log($"InstallProcess started, PID: {InstallProcess.Id}");

                var stdout = InstallProcess.StandardOutput.ReadToEnd();
                var stderr = InstallProcess.StandardError.ReadToEnd();
                InstallProcess.WaitForExit();
                sw.Stop();

                Log($"InstallProcess finished in {sw.Elapsed.TotalSeconds:F1}s");
                Log($"InstallProcess ExitCode: {InstallProcess.ExitCode}");
                if (!string.IsNullOrWhiteSpace(stdout))
                    Log($"InstallProcess STDOUT:\n{stdout}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    Log($"InstallProcess STDERR:\n{stderr}");

                if (InstallProcess.ExitCode == 0)
                {
                    if (IsWindows)
                    {
                        Log($"Installing espeak-ng");
                        CallCMD(IsWindows,
                                "",
                          $"msiexec /i \"{Path.Join(alltalkFolder, "system", "espeak-ng", "espeak-ng-X64.msi")}\" /quiet /norestart",
                          "Espeak-NG");
                    }

                    Log("Modifying configs:");
                    dynamic? config = JsonConvert.DeserializeObject(File.ReadAllText(confignewFile));
                    if (config != null)
                    {
                        config["gradio_port_number"] = 7852;
                        config["firstrun_model"] = false;
                        config["api_def"]["api_port_number"] = 7851;
                        config["tgwui"]["tgwui_lowvram_enabled"] = true;

                        File.WriteAllText(confignewFile, JsonConvert.SerializeObject(config));
                    }

                    dynamic? configEngines = JsonConvert.DeserializeObject(File.ReadAllText(ttsEnginesFile));
                    if (configEngines != null)
                    {
                        configEngines["engine_loaded"] = "xtts";
                        configEngines["selected_model"] = "xtts - xtts2.0.3";
                        File.WriteAllText(ttsEnginesFile, JsonConvert.SerializeObject(configEngines));
                    }

                    dynamic? configEngine = JsonConvert.DeserializeObject(File.ReadAllText(modelSettingsFile));
                    if (configEngine != null)
                    {
                        configEngine["settings"]["lowvram_enabled"] = false;
                        configEngine["settings"]["deepspeed_enabled"] = true;
                        File.WriteAllText(modelSettingsFile, JsonConvert.SerializeObject(configEngine));
                    }
                    InstallCustomData(alltalkFolder, customModelUrl, customVoicesUrl).Wait();

                    Log($"Done!");
                    }
                else
                {
                    Log($"InstallProcess failed with exit code {InstallProcess.ExitCode} — skipping post-install config");
                }
            }
            catch (OperationCanceledException)
            {
                StopInstall();
                Log($"Stopped alltalk install process");
            }
            catch (Exception ex)
            {
                StopInstall();
                Log($"Error while installing alltalk locally: {ex}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error while installing alltalk locally: {ex}");
            StopInstall();
        }
    }

    static void StopInstall()
    {
        try
        {
                Log($"Stopping alltalk install process");
                if (InstallProcessStarted && InstallProcess is { HasExited: false })
                {
                    InstallProcess.Kill(true);
                }
                InstallProcess?.Dispose();
                InstallProcess = null;
                InstallProcessStarted = false;
        }
        catch (Exception ex)
        {
            Log($"Error while stopping alltalk install: {ex}");
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
                InstanceProcess = new Process();
                var alltalkFolder = installFolder;
                Log($"Starting alltalk instance process");

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
                            Log(cleanedMessage, ConsoleColor.Green);
                        else if (Constants.ALLTALKERRORLOGCOLOR.Any(item => e.Data.Contains(item)))
                            Log(cleanedMessage, ConsoleColor.Red);
                        else
                            Log(cleanedMessage, ConsoleColor.Yellow);

                        if (e.Data.Contains("Server Ready"))
                        {
                            Log("Alltalk instance is ready");
                            var readyFile = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Ready.txt");
                            if (!File.Exists(readyFile))
                                File.WriteAllText(readyFile, " ");
                        }
                    }
                };
                InstanceProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null && !string.IsNullOrEmpty(e.Data))
                        Log(CleanAnsi(e.Data));
                };
                InstanceProcess.StartInfo = processInfo;
                InstanceProcess.Start();
                InstanceProcess.BeginOutputReadLine();
                InstanceProcess.BeginErrorReadLine();

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
            }
            catch (Exception ex)
            {
                StopInstance();
                Log($"Error while running alltalk instance: {ex}");
            }
        }
        catch (Exception ex)
        {
            Log($"Error while running alltalk instance: {ex}");
            StopInstance();
        }
    }

    static void StopInstance()
    {
        try
        {
            Log($"Stopping alltalk instance process");
            if (InstanceProcess is { HasExited: false })
            {
                InstanceProcess.CancelOutputRead();
                InstanceProcess.CancelErrorRead();
                InstanceProcess.Kill(true);
            }
            InstanceProcess?.Dispose();
            InstanceProcess = null;
            var readyFile = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Ready.txt");
            if (File.Exists(readyFile))
                File.Delete(readyFile);
        }
        catch (Exception ex)
        {
            Log($"Error while stopping alltalk instance: {ex}");
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
                Log($"Downloading custom model");
                Log($"{customVoicesUrl}");
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
                        Log($"{downloadUrl}");
                        var response = await client.GetAsync(downloadUrl);

                        if (isGoogle)
                            response = GoogleDriveHelper.DownloadGoogleDrive(downloadUrl, response, client);

                        using (var fs = new FileStream(modelFile, FileMode.Create, FileAccess.Write))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        Log($"Extracting custom model");
                        System.IO.Compression.ZipFile.ExtractToDirectory(modelFile, modelFolder, true);
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
                        Log($"Error while downloading custom model, skipping: {ex}");
                    }
                }
            }
            else
                Log($"No custom model found, skipping");

            if (!string.IsNullOrWhiteSpace(customVoicesUrl))
            {
                Log($"Downloading custom voices");
                Log($"{customVoicesUrl}");
                using (var client = new HttpClient())
                {
                    try
                    {
                        var downloadUrl =
                            GoogleDriveHelper.CheckForGoogleAndConvertToDirectDownloadLink(
                                customVoicesUrl, out bool isGoogle);
                        Log($"{downloadUrl}");
                        var response = await client.GetAsync(downloadUrl);

                        if (isGoogle)
                            response = GoogleDriveHelper.DownloadGoogleDrive(downloadUrl, response, client);

                        using (var fs = new FileStream(voicesFile, FileMode.Create, FileAccess.Write))
                        {
                            await response.Content.CopyToAsync(fs);
                        }

                        Log($"Deleting existing voices");
                        if (Directory.Exists(voicesFolder))
                            Directory.Delete(voicesFolder, true);

                        Log($"Extracting custom voices");
                        System.IO.Compression.ZipFile.ExtractToDirectory(voicesFile, alltalkFolder, true);
                        File.Delete(voicesFile);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error while downloading custom voices, skipping: {ex}");
                    }
                }
            }
            else
                Log($"No custom voices found, skipping");
        }
        catch (Exception ex)
        {
            Log($"Error while installing custom data: {ex}");
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

            Log(@$"Calling command: '{exePath} {command}'");
            process.Start();

            while (!process.HasExited)
            {
                string? output = process.StandardOutput.ReadLine();
                Log(output ?? "");
            }
        }
        catch (Exception e)
        {
            Log($"{e}");
        }
    }

    static void Dispose()
    {
        StopInstall();
        StopInstance();
    }
}
