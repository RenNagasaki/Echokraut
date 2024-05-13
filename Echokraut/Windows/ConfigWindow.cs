using System;
using System.Collections.Generic;
using System.Numerics;
using System.Xml.Linq;
using Dalamud.Interface.Windowing;
using Echokraut.DataClasses;
using ImGuiNET;
using static Dalamud.Interface.Utility.Raii.ImRaii;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Echokraut.Enums;
using System.Linq;
using Dalamud.Interface;
using Echokraut.Backend;

namespace Echokraut.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Echokraut plugin;
    private IPluginLog log;
    private string testConnectionRes = "";

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Echokraut plugin, IPluginLog log, Configuration configuration) : base("Echokraut configuration###EKSettings")
    {
        this.plugin = plugin;
        this.log = log;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.Always;

        Configuration = configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Configuration.IsConfigWindowMovable)
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
        if (ImGui.BeginTabBar($"Echokraut##EKTab"))
        {
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Voices"))
            {
                DrawVoices();
                ImGui.EndTabItem();
            }
        }

        ImGui.EndTabBar();
    }

    private void DrawSettings()
    {
        if (ImGui.CollapsingHeader("General"))
        {
        }


        if (ImGui.CollapsingHeader("Backend"))
        {
            var backends = Enum.GetValues<TTSBackends>().ToArray();
            var backendsDisplay = backends.Select(b => b.ToString()).ToArray();
            var presetIndex = Enum.GetValues<TTSBackends>().ToList().IndexOf(this.Configuration.BackendSelection);
            if (ImGui.Combo($"##EKCBoxBackend", ref presetIndex, backendsDisplay, backendsDisplay.Length))
            {
                this.Configuration.BackendSelection = backends[presetIndex]; 
                this.Configuration.Save();

                log.Debug($"Updated backendselection to: {Constants.BACKENDS[presetIndex]}");
            }

            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
            {
                if (ImGui.InputText($"Base Url##EKBaseUrl", ref this.Configuration.Alltalk.BaseUrl, 40))
                    this.Configuration.Save();
            }

            if (ImGui.Button($"Test Connection##EKTestConnection"))
            {
                BackendCheckReady();
            }

            if (!string.IsNullOrWhiteSpace(testConnectionRes))
                ImGui.TextColored(new(1.0f, 1.0f, 1.0f, 0.6f), $"Connection test result: {testConnectionRes}");
        }
    }

    private async void BackendCheckReady()
    {
        try
        {
            if (this.Configuration.BackendSelection == TTSBackends.Alltalk)
                testConnectionRes = await plugin.BackendHelper.CheckReady();
            else
                testConnectionRes = "No backend selected";
            log.Error($"Connection test result: {testConnectionRes}");
        }
        catch (Exception ex)
        {
            testConnectionRes = ex.ToString();
            log.Error(ex.ToString());
        }
    }

    private void DrawVoices()
    {

    }
}
