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
using Echokraut.Localization;
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
            _log.Error(nameof(Draw), ex.ToString(), new EKEventId(0, TextSource.Backend));
        }
    }

    public void DrawLocalInstance(bool firstTime)
    {
        try
        {
            if (ImGui.CollapsingHeader(Loc.S("Install process:")))
            {
                using (ImRaii.Disabled(_alltalkInstance.Installing))
                {
                    ImGui.Text(Loc.S("Local instance path:"));
                    using (ImRaii.Disabled(true))
                    {
                        var localInstallPath = _config.Alltalk.LocalInstallPath;
                        ImGui.InputText($"##EKLocalATPath", ref localInstallPath, 128);
                    }

                    ImGui.SameLine();
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##EKLocalATPathButton",
                                                     new Vector2(25, 25),
                                                     Loc.S("Select a directory via dialog."), _alltalkInstance.Installing,
                                                     true))
                    {
                        var startDir = _config.Alltalk.LocalInstallPath.Length > 0 &&
                                       Directory.Exists(_config.Alltalk.LocalInstallPath)
                                           ? _config.Alltalk.LocalInstallPath
                                           : "";

                        fileDialogManager.OpenFolderDialog(Loc.S("Choose alltalk instance directory"),
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
                                Loc.S("The Alltalk path must not contain spaces or dashes.\r\nPlease make sure it's formatted correctly."));
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_config.Alltalk.LocalInstallPath))
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(Loc.S("The Alltalk path must not be empty.\r\nPlease enter a valid path."));
                        }
                    }

                    if (!_alltalkInstance.IsCudaInstalled && !_alltalkInstance.IsWindows)
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(
                                Loc.S("The CUDA Toolkit does not appear to be installed.\r\nIt is required for local Alltalk instances."));
                        }
                    }

                    if (ImGui.CollapsingHeader(Loc.S("Advanced options")))
                    {
                        var isWindows11 = _config.Alltalk.IsWindows11;
                        if (ImGui.Checkbox($"{Loc.S("Is Windows 11")}##EKIsWin11", ref isWindows11))
                        {
                            _config.Alltalk.IsWindows11 = isWindows11;
                            _config.Save();
                        }

                        ImGui.Text(
                            Loc.S("Custom XTTS model URL (zip file with all files in one root folder):"));
                        if (ImGui.InputText($"{Loc.S("Custom model URL")}##EKCustomModelUrl",
                                            ref _config.Alltalk.CustomModelUrl, 256))
                            _config.Save();

                        ImGui.Text(
                            Loc.S("Custom voices URL (zip file with a \"voices\" root folder):"));
                        if (ImGui.InputText($"{Loc.S("Custom voices URL")}##EKCustomModelUrl",
                                            ref _config.Alltalk.CustomVoicesUrl, 256))
                            _config.Save();

                        using (ImRaii.Disabled(!_config.Alltalk.LocalInstall || _alltalkInstance.Installing))
                        {
                            if (ImGui.Button(Loc.S("Install only custom data")))
                                _alltalkInstance.InstallCustomData(new EKEventId(0, TextSource.Backend), false);
                        }

                        var autoStartLocalInstance = _config.Alltalk.AutoStartLocalInstance;
                        if (ImGui.Checkbox($"{Loc.S("Auto-start local instance on plugin load")}##EKAutoStartLocalInstance",
                                           ref autoStartLocalInstance))
                        {
                            if (autoStartLocalInstance && !firstTime && _config.Alltalk.LocalInstall &&
                                !_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting)
                                _alltalkInstance.StartInstance();

                            _config.Alltalk.AutoStartLocalInstance = autoStartLocalInstance;
                            _config.Save();
                        }
                    }

                    using (ImRaii.Disabled(error))
                    {
                        var buttonText = _alltalkInstance.Installing
                                             ? Loc.S("Installing...")
                                             : _config.Alltalk.LocalInstall
                                                 ? Loc.S("Reinstall (removes existing and installs fresh)")
                                                 : Loc.S("Install");
                        if (ImGui.Button(buttonText))
                        {
                            if (_config.Alltalk.LocalInstall && (_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting))
                                _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));
                            _alltalkInstance.Install();
                        }

                        ImGui.Text(Loc.S("The installation requires about 20 GB of disk space and may take a while depending on your connection."));
                        ImGui.Text(Loc.S("Up to two shell windows may open during installation — you can follow the progress there."));
                    }

                    if (_alltalkInstance.Installing)
                        DrawLoadSpinner();
                }
            }

            if (!firstTime)
            {
                using (ImRaii.Disabled(_alltalkInstance.InstanceRunning || _alltalkInstance.InstanceStarting))
                {
                    var buttonText = _alltalkInstance.InstanceStarting ? Loc.S("Starting...") : _alltalkInstance.InstanceRunning ? Loc.S("Running") : Loc.S("Start");
                    if (ImGui.Button(buttonText))
                        _alltalkInstance.StartInstance();
                }

                if (_alltalkInstance.InstanceStarting)
                    DrawLoadSpinner();

                ImGui.SameLine();
                using (ImRaii.Disabled(_alltalkInstance.InstanceStopping || (!_alltalkInstance.InstanceRunning && !_alltalkInstance.InstanceStarting)))
                {
                    var buttonText = _alltalkInstance.InstanceStopping ? Loc.S("Stopping...") : Loc.S("Stop");
                    if (ImGui.Button(buttonText))
                        _alltalkInstance.StopInstance(new EKEventId(0, TextSource.Backend));
                }

                ImGui.NewLine();
                
                DrawAlltalkServiceOptions();
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawLocalInstance), ex.ToString(), new EKEventId(0, TextSource.Backend));
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
            _log.Error(nameof(DrawLoadSpinner), ex.ToString(), new EKEventId(0, TextSource.Backend));
        }
    }

    public void DrawRemoteInstance(bool firstTime)
    {
        try
        {
            if (ImGui.InputText($"{Loc.S("Base Url")}##EKBaseUrl", ref _config.Alltalk.BaseUrl, 80))
                _config.Save();
            ImGui.SameLine();
            if (ImGui.Button($"{Loc.S("Test Connection")}##EKTestConnection"))
            {
                BackendCheckReady(new EKEventId(0, TextSource.None));
            }

            DrawAlltalkServiceOptions();

            if (!string.IsNullOrWhiteSpace(testConnectionRes))
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawRemoteInstance), ex.ToString(), new EKEventId(0, TextSource.Backend));
        }
    }

    public void DrawAlltalkServiceOptions()
    {
        var streamingGeneration = _config.Alltalk.StreamingGeneration;
        if (ImGui.Checkbox($"{Loc.S("Streaming generation (play audio before full text is generated)")}##EKGenerateStreaming", ref streamingGeneration))
        {
            _config.Alltalk.StreamingGeneration = streamingGeneration;
            _config.Save();
        }
        if (ImGui.InputText($"{Loc.S("Model to reload")}##EKBaseUrl", ref _config.Alltalk.ReloadModel, 40))
            _config.Save();
        ImGui.SameLine();
        if (ImGui.Button($"{Loc.S("Reload model")}##EKReloadModel"))
        {
            BackendReloadService(_config.Alltalk.ReloadModel);
        }

        if (ImGui.Button($"{Loc.S("Reload voices")}##EKReloadVoices"))
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
                if (ImGui.Checkbox($"{Loc.S("Local instance")}##EKLocalATInstance", ref localInstance))
                {
                    _config.Alltalk.InstanceType = Enums.AlltalkInstanceType.Local;
                    _config.Save();
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(remoteInstance || _alltalkInstance.Installing))
            {
                if (ImGui.Checkbox($"{Loc.S("Remote instance")}##EKRemoteATInstance", ref remoteInstance))
                {
                    _config.Alltalk.InstanceType = Enums.AlltalkInstanceType.Remote;
                    _config.Save();
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(noInstance || _alltalkInstance.Installing))
            {
                if (ImGui.Checkbox($"{Loc.S("No instance")}##EKNoATInstance", ref noInstance))
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
                        Loc.S("You are not on Windows. Additional setup steps are required to use Alltalk locally."));
                    ImGui.Text(
                        Loc.S("Please refer to the Discord or the install instructions from erew123 (link above)."));
                    ImGui.Text(Loc.S("If you have already completed these steps, you can ignore this message."));
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
                                Loc.S("No audio will be generated in this mode."));
                            ImGui.Text(
                                Loc.S("Only use this if you are unable to run Alltalk at all."));
                            ImGui.Text(
                                Loc.S("You will need to obtain audio files from a friend or via a Google Drive share link."));
                        }
                    }
                }

                style.Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(DrawAlltalk), ex.ToString(), new EKEventId(0, TextSource.Backend));
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
