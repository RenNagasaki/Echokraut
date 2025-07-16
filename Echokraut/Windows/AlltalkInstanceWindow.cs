using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.API;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using ImGuiNET;
using OtterGui;

namespace Echokraut.Windows;

public class AlltalkInstanceWindow : Window, IDisposable
{
    private static FileDialogManager fileDialogManager;
    private static UldWrapper uldWrapper;
    private static int partIndex = 0;
    private static bool spinnerFilling = true;
    private static string testConnectionRes = "";

    public AlltalkInstanceWindow() : base($"Echokraut Alltalk Installation###EKAlltalkInstall")
    {
        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar &
                ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        uldWrapper = Plugin.PluginInterface.UiBuilder.LoadUld("ui/uld/ActionBar.uld");
        fileDialogManager = new FileDialogManager();
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Plugin.Configuration!.IsConfigWindowMovable)
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
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    public static void DrawLocalInstance(bool firstTime)
    {
        try
        {
            if (ImGui.CollapsingHeader("Install process:"))
            {
                using (ImRaii.Disabled(AlltalkInstanceHelper.Installing))
                {
                    ImGui.Text("Local instance path:");
                    using (ImRaii.Disabled(true))
                    {
                        var localInstallPath = Plugin.Configuration.Alltalk.LocalInstallPath;
                        ImGui.InputText($"##EKLocalATPath", ref localInstallPath, 128);
                    }

                    ImGui.SameLine();
                    if (ImGuiUtil.DrawDisabledButton($"{FontAwesomeIcon.Folder.ToIconString()}##EKLocalATPathButton",
                                                     new Vector2(25, 25),
                                                     "Select a directory via dialog.", AlltalkInstanceHelper.Installing,
                                                     true))
                    {
                        var startDir = Plugin.Configuration.Alltalk.LocalInstallPath.Length > 0 &&
                                       Directory.Exists(Plugin.Configuration.Alltalk.LocalInstallPath)
                                           ? Plugin.Configuration.Alltalk.LocalInstallPath
                                           : "";

                        fileDialogManager.OpenFolderDialog("Choose alltalk instance directory",
                                                           (selected, selectedPath) =>
                                                           {
                                                               if (!selected)
                                                                   return;

                                                               var oldInstallPath = Plugin.Configuration.Alltalk
                                                                   .LocalInstallPath;
                                                               if (!string.IsNullOrWhiteSpace(oldInstallPath) &&
                                                                   Directory.Exists(
                                                                       Path.Join(oldInstallPath,
                                                                           Constants.ALLTALKFOLDERNAME)))
                                                                   Directory.Move(oldInstallPath, selectedPath);

                                                               Plugin.Configuration.Alltalk.LocalInstallPath =
                                                                   selectedPath;
                                                               Plugin.Configuration.Save();
                                                           }, startDir);
                    }

                    fileDialogManager.Draw();

                    var error = false;
                    if (Plugin.Configuration.Alltalk.LocalInstallPath.Contains(" ") ||
                        Plugin.Configuration.Alltalk.LocalInstallPath.Contains("-"))
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(
                                "The alltalk path may not contain any spaces \" \" or dashes \"-\".\r\nPlease make sure its formed correctly.");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(Plugin.Configuration.Alltalk.LocalInstallPath))
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text("The alltalk path may not be empty.\r\nPlease make sure its formed correctly.");
                        }
                    }

                    if (!AlltalkInstanceHelper.IsCudaInstalled && !AlltalkInstanceHelper.IsWindows)
                    {
                        error = true;
                        using (ImRaii.PushColor(ImGuiCol.Text, Constants.ERRORLOGCOLOR))
                        {
                            ImGui.Text(
                                "It seems you don't have the CUDA Toolkit installed.\r\nIn order to use a local install you need to install it.");
                        }
                    }

                    if (ImGui.CollapsingHeader("Custom(trained) data:"))
                    {
                        using (ImRaii.Disabled(AlltalkInstanceHelper.Installing))
                        {
                            ImGui.Text(
                                "If you prefer a custom trained xtts model, enter the direct download url here. (It needs to be a zip where all files are within one root folder)");
                            if (ImGui.InputText($"Custom model URL##EKCustomModelUrl",
                                                ref Plugin.Configuration.Alltalk.CustomModelUrl, 256))
                                Plugin.Configuration.Save();

                            ImGui.Text(
                                "If you prefer custom voices, enter the direct download url here. (It needs to be a zip where all files are within one root folder called \"voices\")");
                            if (ImGui.InputText($"Custom voices URL##EKCustomModelUrl",
                                                ref Plugin.Configuration.Alltalk.CustomVoicesUrl, 256))
                                Plugin.Configuration.Save();

                            if (Plugin.Configuration.Alltalk.LocalInstall && ImGui.Button("Install only custom data"))
                                AlltalkInstanceHelper.InstallCustomData(new EKEventId(0, TextSource.Backend), false);
                        }
                    }

                    var autoStartLocalInstance = Plugin.Configuration.Alltalk.AutoStartLocalInstance;
                    if (ImGui.Checkbox("Auto start local instance when plugin loads##EKAutoStartLocalInstance",
                                       ref autoStartLocalInstance))
                    {
                        if (autoStartLocalInstance && !firstTime && Plugin.Configuration.Alltalk.LocalInstall &&
                            !AlltalkInstanceHelper.InstanceRunning && !AlltalkInstanceHelper.InstanceStarting)
                            AlltalkInstanceHelper.StartInstance();

                        Plugin.Configuration.Alltalk.AutoStartLocalInstance = autoStartLocalInstance;
                        Plugin.Configuration.Save();
                    }

                    using (ImRaii.Disabled(error))
                    {
                        var buttonText = AlltalkInstanceHelper.Installing
                                             ? "Installing..."
                                             :
                                             Plugin.Configuration.Alltalk.LocalInstall
                                                 ?
                                                 "Reinstall (delete alltalk and install fresh)"
                                                 : "Install";
                        if (ImGui.Button(buttonText))
                            AlltalkInstanceHelper.Install(Plugin.Configuration.Alltalk.LocalInstall);
                    }

                    if (AlltalkInstanceHelper.Installing)
                        DrawLoadSpinner();
                }
            }

            if (!firstTime)
            {
                var streamingGeneration = Plugin.Configuration.Alltalk.StreamingGeneration;
                if (ImGui.Checkbox("Generate Streaming(Do not wait for whole text to be generated before playing audio)##EKGenerateStreaming", ref streamingGeneration))
                {
                    Plugin.Configuration.Alltalk.StreamingGeneration = streamingGeneration;
                    Plugin.Configuration.Save();
                }

                using (ImRaii.Disabled(AlltalkInstanceHelper.InstanceRunning || AlltalkInstanceHelper.InstanceStarting))
                {
                    var buttonText = AlltalkInstanceHelper.InstanceStarting ? "Starting..." : AlltalkInstanceHelper.InstanceRunning ? "Running" : "Start";
                    if (ImGui.Button(buttonText))
                        AlltalkInstanceHelper.StartInstance();
                }

                if (AlltalkInstanceHelper.InstanceStarting)
                    DrawLoadSpinner();

                ImGui.SameLine();
                using (ImRaii.Disabled(AlltalkInstanceHelper.InstanceStopping || (!AlltalkInstanceHelper.InstanceRunning && !AlltalkInstanceHelper.InstanceStarting)))
                {
                    var buttonText = AlltalkInstanceHelper.InstanceStopping ? "Stopping..." : "Stop";
                    if (ImGui.Button(buttonText))
                        AlltalkInstanceHelper.StopInstance(new EKEventId(0, TextSource.Backend));
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    private static void DrawLoadSpinner()
    {
        try
        {
            var iconSizeSmall = new Vector2(25, 25);
            var spinnerPart = uldWrapper.LoadTexturePart("ui/uld/IconA_Recast2.tex", partIndex);

            ImGui.SameLine();
            if (spinnerFilling)
            {
                ImGui.Image(spinnerPart!.ImGuiHandle, iconSizeSmall, new Vector2(0.0f, 0.0f),
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
                ImGui.Image(spinnerPart!.ImGuiHandle, iconSizeSmall, new Vector2(1.0f, 0.0f),
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
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    public static void DrawRemoteInstance(bool firstTime)
    {
        try
        {
            if (ImGui.InputText($"Base Url##EKBaseUrl", ref Plugin.Configuration.Alltalk.BaseUrl, 80))
                Plugin.Configuration.Save();
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
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }

    public static void DrawAlltalkServiceOptions()
    {
        var streamingGeneration = Plugin.Configuration.Alltalk.StreamingGeneration;
        if (ImGui.Checkbox("Generate streaming(Do not wait for whole text to be generated before playing audio)##EKGenerateStreaming", ref streamingGeneration))
        {
            Plugin.Configuration.Alltalk.StreamingGeneration = streamingGeneration;
            Plugin.Configuration.Save();
        }
        if (ImGui.InputText($"Model to reload##EKBaseUrl", ref Plugin.Configuration.Alltalk.ReloadModel, 40))
            Plugin.Configuration.Save();
        ImGui.SameLine();
        if (ImGui.Button($"Reload model##EKReloadModel"))
        {
            BackendReloadService(Plugin.Configuration.Alltalk.ReloadModel);
        }

        if (ImGui.Button($"Reload Voices##EKReloadVoices"))
        {
            BackendGetVoices();
        }
    }

    public static void DrawAlltalk(bool firstTime)
    {
        try
        {
            var remoteInstance = Plugin.Configuration.Alltalk.RemoteInstance;
            var localInstance = Plugin.Configuration.Alltalk.LocalInstance;
            using (ImRaii.Disabled(localInstance || AlltalkInstanceHelper.Installing))
            {
                if (ImGui.Checkbox("Local instance##EKLocalATInstance", ref localInstance))
                {
                    Plugin.Configuration.Alltalk.LocalInstance = localInstance;
                    Plugin.Configuration.Alltalk.RemoteInstance = false;
                    Plugin.Configuration.Save();
                }
            }
            ImGui.SameLine();
            using (ImRaii.Disabled(remoteInstance || AlltalkInstanceHelper.Installing))
            {
                if (ImGui.Checkbox("Remote instance##EKRemoteATInstance", ref remoteInstance))
                {
                    Plugin.Configuration.Alltalk.LocalInstance = false;
                    Plugin.Configuration.Alltalk.RemoteInstance = remoteInstance;
                    Plugin.Configuration.Save();
                }
            }

            if (!AlltalkInstanceHelper.IsWindows)
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
                }

                style.Push(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }
    private static async void BackendCheckReady(EKEventId eventId)
    {
        try
        {
            if (Plugin.Configuration.BackendSelection == TTSBackends.Alltalk)
                testConnectionRes = await BackendHelper.CheckReady(eventId);
            else
                testConnectionRes = "No backend selected";
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, $"Connection test result: {testConnectionRes}", eventId);
        }
        catch (Exception ex)
        {
            testConnectionRes = ex.ToString();
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), eventId);
        }
    }

    private static async void BackendGetVoices()
    {
        try
        {
            if (Plugin.Configuration.BackendSelection == TTSBackends.Alltalk)
                BackendHelper.SetBackendType(Plugin.Configuration.BackendSelection);

            ConfigWindow.UpdateDataBubbles = true;
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }

    private static async void BackendReloadService(string reloadModel)
    {
        try
        {
            if (BackendHelper.ReloadService(reloadModel, new EKEventId(0, TextSource.None)))
                testConnectionRes = "Successfully started service reload. Please wait for up to 30 seconds before using.";
            else
                testConnectionRes = "Error while service reload. Please check logs.";

            LogHelper.Info(MethodBase.GetCurrentMethod().Name, testConnectionRes, new EKEventId(0, TextSource.None));
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, ex.ToString(), new EKEventId(0, TextSource.None));
        }
    }
}
