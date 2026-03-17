using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using OtterGui;

namespace Echokraut.Windows;

public class AlltalkInstanceWindow : Window, IDisposable
{
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IAlltalkInstanceService _alltalkInstance;
    private readonly IBackendService _backend;
    private readonly FileDialogManager fileDialogManager;
    private readonly UldWrapper uldWrapper;
    private int partIndex = 0;
    private bool spinnerFilling = true;
    private string testConnectionRes = "";

    public AlltalkInstanceWindow(ILogService log, Configuration config, IAlltalkInstanceService alltalkInstance, IBackendService backend, IDalamudPluginInterface pluginInterface)
        : base($"Echokraut Alltalk Installation###EKAlltalkInstall")
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _alltalkInstance = alltalkInstance ?? throw new ArgumentNullException(nameof(alltalkInstance));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar &
                ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        uldWrapper = pluginInterface.UiBuilder.LoadUld("ui/uld/ActionBar.uld");
        fileDialogManager = new FileDialogManager();
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (_config.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        try
        {
            DrawAlltalk(false);
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Draw), $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    public void DrawLocalInstance(bool firstTime)
    {
        try
        {
            if (ImGui.CollapsingHeader("Install process:"))
            {
                using (ImRaii.Disabled(_alltalkInstance.Installing))
                {
                    ImGui.Text("Local instance path:");
                    using (ImRaii.Disabled(true))
                    {
                        var localInstallPath = _config.Alltalk.LocalInstallPath;
                        ImGui.InputText($"##EKLocalATPath", ref localInstallPath, 128);
                    }

                    ImGui.SameLine();
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##EKLocalATPathButton",
                                                     new Vector2(25, 25),
                                                     "Select a directory via dialog.", _alltalkInstance.Installing,
                                                     true))
                    {
                        var startDir = _config.Alltalk.LocalInstallPath.Length > 0 &&
                                       Directory.Exists(_config.Alltalk.LocalInstallPath)
                                           ? _config.Alltalk.LocalInstallPath
                                           : "";

                        fileDialogManager.OpenFolderDialog("Choose alltalk instance directory",
                                                           (selected, selectedPath) =>
                                                           {
                                                               if (!selected)
                                                                   return;

                                                               var oldInstallPath = _config.Alltalk
                                                                   .LocalInstallPath;
                                                               if (!string.IsNullOrWhiteSpace(oldInstallPath) &&
                                                                   Directory.Exists(
                                                                       Path.Join(oldInstallPath,
                                                                           Constants.ALLTALKFOLDERNAME)))
                                                                   Directory.Move(oldInstallPath, selectedPath);

                                                               _config.Alltalk.LocalInstallPath =
                                                                   selectedPath;
                                                               _config.Save();
                                                           }, startDir);
                    }

                    fileDialogManager.Draw();

                    var error = false;
                    if (_config.Alltalk.LocalInstallPath.Contains(" ") ||
                        _config.Alltalk.LocalInstallPath.Contains("-"))
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(
                                "The alltalk path may not contain any spaces \" \" or dashes \"-\".\r\nPlease make sure its formed correctly.");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_config.Alltalk.LocalInstallPath))
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text("The alltalk path may not be empty.\r\nPlease make sure its formed correctly.");
                        }
                    }

                    if (!_alltalkInstance.IsCudaInstalled && !_alltalkInstance.IsWindows)
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(
                                "It seems you don't have the CUDA Toolkit installed.\r\nIn order to use a local install you need to install it.");
                        }
                    }

                    var isWindows11 = _config.Alltalk.IsWindows11;
                    if (ImGui.Checkbox("Is Windows 11##EKIsWin11", ref isWindows11))
                    {
                        _config.Alltalk.IsWindows11 = isWindows11;
                        _config.Save();
                    }

                    if (ImGui.CollapsingHeader("Custom(trained) data:"))
                    {
                        using (ImRaii.Disabled(_alltalkInstance.Installing))
                        { 
                            ImGui.Text(
                                "If you prefer a custom trained xtts model, enter the direct download url here. (It needs to be a zip where all files are within one root folder)");
                            if (ImGui.InputText($"Custom model URL##EKCustomModelUrl",
                                                ref _config.Alltalk.CustomModelUrl, 256))
                                _config.Save();

                            ImGui.Text(
                                "If you prefer custom voices, enter the direct download url here. (It needs to be a zip where all files are within one root folder called \"voices\")");
                            if (ImGui.InputText($"Custom voices URL##EKCustomModelUrl",
                                                ref _config.Alltalk.CustomVoicesUrl, 256))
                                _config.Save();

                            if (_config.Alltalk.LocalInstall && ImGui.Button("Install only custom data"))
                                _alltalkInstance.InstallCustomData(new EKEventId(0, TextSource.Backend), false);
                        }
                    }

                    var autoStartLocalInstance = _config.Alltalk.AutoStartLocalInstance;
                    if (ImGui.Checkbox("Auto start local instance when plugin loads##EKAutoStartLocalInstance",
                                       ref autoStartLocalInstance))
                    {
                        if (autoStartLocalInstance && !firstTime && _config.Alltalk.LocalInstall &&
                            !_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting)
                            _alltalkInstance.StartInstance();

                        _config.Alltalk.AutoStartLocalInstance = autoStartLocalInstance;
                        _config.Save();
                    }

                    using (ImRaii.Disabled(error))
                    {
                        var buttonText = _alltalkInstance.Installing
                                             ? "Installing..."
                                             :
                                             _config.Alltalk.LocalInstall
                                                 ?
                                                 "Reinstall (delete alltalk and install fresh)"
                                                 : "Install";
                        if (ImGui.Button(buttonText))
                        {
                            if (_config.Alltalk.LocalInstall && (_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting))
                                _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));
                            _alltalkInstance.Install();
                        }
                        
                        ImGui.Text("Please be aware that the install process needs about 20GB of space on disk and, depending on your connection, may take quite some time to install");
                        ImGui.Text("The process should open up to two shell/cmd windows while installing, you can follow the process there");
                    }

                    if (_alltalkInstance.Installing)
                        DrawLoadSpinner();
                }
            }

            if (!firstTime)
            {
                using (ImRaii.Disabled(_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting))
                {
                    var buttonText = _alltalkInstance.InstanceStarting ? "Starting..." : _alltalkInstance.InstanceRunning ? "Running" : "Start";
                    if (ImGui.Button(buttonText))
                        _alltalkInstance.StartInstance();
                }

                if (_alltalkInstance.InstanceStarting)
                    DrawLoadSpinner();

                ImGui.SameLine();
                using (ImRaii.Disabled(_alltalkInstance.InstanceStopping || (!_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting)))
                {
                    var buttonText = _alltalkInstance.InstanceStopping ? "Stopping..." : "Stop";
                    if (ImGui.Button(buttonText))
                        _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));
                }

                ImGui.NewLine();
                
                DrawAlltalkServiceOptions();
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawLocalInstance), $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    private void DrawLoadSpinner()
    {
        try
        {
            var iconSizeSmall = new Vector2(25, 25);
            var spinnerPart = uldWrapper.LoadTexturePart("ui/uld/IconA_Recast2.tex", partIndex);

            ImGui.SameLine();
            if (spinnerFilling)
            {
                ImGui.Image(spinnerPart!.Handle, iconSizeSmall, new Vector2(0.0f, 0.0f),
                            new Vector2(1.0f, 1.0f));
                partIndex++;

                if (partIndex >= 80)
                {
                    partIndex = 78;
                    spinnerFilling = false;
                }
            }
            else
            {
                ImGui.Image(spinnerPart!.Handle, iconSizeSmall, new Vector2(1.0f, 0.0f),
                            new Vector2(0.0f, 1.0f));
                partIndex--;

                if (partIndex < 0)
                {
                    partIndex = 1;
                    spinnerFilling = true;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawLoadSpinner), $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    public void DrawRemoteInstance(bool firstTime)
    {
        try
        {
            if (ImGui.InputText($"Base Url##EKBaseUrl", ref _config.Alltalk.BaseUrl, 80))
                _config.Save();
            ImGui.SameLine();
            if (ImGui.Button($"Test Connection##EKTestConnection"))
            {
                BackendCheckReady(new EKEventId(0, TextSource.None));
            }

            DrawAlltalkServiceOptions();

            if (!string.IsNullOrWhiteSpace(testConnectionRes))
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawRemoteInstance), $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    public void DrawAlltalkServiceOptions()
    {
        var streamingGeneration = _config.Alltalk.StreamingGeneration;
        if (ImGui.Checkbox("Generate streaming(Do not wait for whole text to be generated before playing audio)##EKGenerateStreaming", ref streamingGeneration))
        {
            _config.Alltalk.StreamingGeneration = streamingGeneration;
            _config.Save();
        }
        if (ImGui.InputText($"Model to reload##EKBaseUrl", ref _config.Alltalk.ReloadModel, 40))
            _config.Save();
        ImGui.SameLine();
        if (ImGui.Button($"Reload model##EKReloadModel"))
        {
            BackendReloadService(_config.Alltalk.ReloadModel);
        }

        if (ImGui.Button($"Reload Voices##EKReloadVoices"))
        {
            BackendGetVoices();
        }
    }

    public void DrawAlltalk(bool firstTime)
    {
        try
        {
            var instanceType = _config.Alltalk.InstanceType;
            var localInstance = instanceType == Enums.AlltalkInstanceType.Local;
            var remoteInstance = instanceType == Enums.AlltalkInstanceType.Remote;
            var noInstance = instanceType == Enums.AlltalkInstanceType.None;
            using (ImRaii.Disabled(localInstance || _alltalkInstance.Installing))
            {
                if (ImGui.Checkbox("Local instance##EKLocalATInstance", ref localInstance))
                {
                    _config.Alltalk.InstanceType = Enums.AlltalkInstanceType.Local;
                    _config.Save();
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(remoteInstance || _alltalkInstance.Installing))
            {
                if (ImGui.Checkbox("Remote instance##EKRemoteATInstance", ref remoteInstance))
                {
                    _config.Alltalk.InstanceType = Enums.AlltalkInstanceType.Remote;
                    _config.Save();
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(noInstance || _alltalkInstance.Installing))
            {
                if (ImGui.Checkbox("No instance##EKNoATInstance", ref noInstance))
                {
                    _config.Alltalk.InstanceType = Enums.AlltalkInstanceType.None;
                    _config.Save();
                }
            }

            if (!_alltalkInstance.IsWindows)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                {
                    ImGui.Text(
                        "You're not on windows, this means you need to fulfill some extra steps in order to use alltalk locally.");
                    ImGui.Text(
                        "Please refer to my discord or if you prefer, the install intructions from erew123 by using the link above.");
                    ImGui.Text("If you already did all necessary steps, ignore this message.");
                }
            }
            using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            using (var child = ImRaii.Child("##EKInstallArea", new Vector2(ImGui.GetContentRegionAvail().X, -ImGui.GetFrameHeight() * 3f),
                                            true, ImGuiWindowFlags.HorizontalScrollbar))
            {
                style.Pop();
                if (child)
                {
                    if (localInstance)
                    {
                        DrawLocalInstance(firstTime);
                    }
                    else if (remoteInstance)
                    {
                        DrawRemoteInstance(firstTime);
                    }
                    else if (noInstance)
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(
                                "Please be aware that selecting 'No Instance' is only meant to be used if you're unable to use Alltalk at all.");
                            ImGui.Text(
                                "It will result in you not generating any audio.");
                            ImGui.Text(
                                "You will need to procure the audio files directly from a friend or via the Google Drive Share Link.");
                        }
                    }
                }

                style.Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawAlltalk), $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }
    private async void BackendCheckReady(EKEventId eventId)
    {
        try
        {
            if (_config.BackendSelection == TTSBackends.Alltalk)
                testConnectionRes = await _backend.CheckReady(eventId);
            else
                testConnectionRes = "No backend selected";
            _log.Debug(nameof(BackendCheckReady), $"Connection test result: {testConnectionRes}", eventId);
        }
        catch (Exception ex)
        {
            testConnectionRes = ex.ToString();
            _log.Error(nameof(BackendCheckReady), ex.ToString(), eventId);
        }
    }

    private async void BackendGetVoices()
    {
        try
        {
            if (_config.BackendSelection == TTSBackends.Alltalk)
                _backend.SetBackendType(_config.BackendSelection);

            _backend.NotifyCharacterMapped();
        }
        catch (Exception ex)
        {
            _log.Error(nameof(BackendGetVoices), ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private async void BackendReloadService(string reloadModel)
    {
        try
        {
            if (_backend.ReloadService(reloadModel, new EKEventId(0, TextSource.None)))
                testConnectionRes = "Successfully started service reload. Please wait for up to 30 seconds before using.";
            else
                testConnectionRes = "Error while service reload. Please check logs.";

            _log.Info(nameof(BackendReloadService), testConnectionRes, new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(BackendReloadService), ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }
}
