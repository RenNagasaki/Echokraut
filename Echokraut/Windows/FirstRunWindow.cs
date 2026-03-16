using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;

namespace Echokraut.Windows;
 
public class FirstTimeWindow : Window, IDisposable
{
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IFramework _framework;
    private readonly AlltalkInstanceWindow _alttalkInstanceWindow;
    private readonly ConfigWindow _configWindow;

    public FirstTimeWindow(ILogService log, Configuration config, IFramework framework, AlltalkInstanceWindow alttalkInstanceWindow, ConfigWindow configWindow)
        : base($"First time using Echokraut###EKFirstTime")
    {
        _log = log;
        _config = config;
        _framework = framework;
        _alttalkInstanceWindow = alttalkInstanceWindow;
        _configWindow = configWindow;
        Flags = ImGuiWindowFlags.NoScrollbar;
        Size = new Vector2(600, 900);
        SizeCondition = ImGuiCond.FirstUseEver;

        if (!_config.Alltalk.LocalInstance && !_config.Alltalk.RemoteInstance)
        {
            if (!string.IsNullOrWhiteSpace(_config.Alltalk.BaseUrl) && !_config.Alltalk.BaseUrl.Contains("127.0.0.1"))
                _config.Alltalk.RemoteInstance = true;
            else
                _config.Alltalk.LocalInstance = true;
        }
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
            _framework.RunOnFrameworkThread(() => { _log.UpdateMainThreadLogs(); });
            using (ImRaii.TextWrapPos(0))
            {
                ImGui.Text(
                    "Hey!\r\nIt seems like this is your first time using Echokraut. Please read this carefully.");
                ImGui.Text("This plugin is solely developed to give (nearly) every text in this game a voice.");
                ImGui.Text("To achieve that I utilised");
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Constants.INFOLOGCOLOR))
                    ImGui.Text("Alltalk by erew123.");
                ImGui.Text(
                    "You have the option to install a local instance, which will run on your GPU or link a remotely running instance to use.");
                ImGui.Text(
                    "For example: \r\n- You or someone you know hosts one or more instances on their server\r\n- You're using google collab or other services like vast.ai");
            }

            _alttalkInstanceWindow.DrawAlltalk(true);

            using (ImRaii.TextWrapPos(0))
            {
                ImGui.Text(
                    "Pressing this button will close the install window and enable you to fully use & configure Echokraut.");
                ImGui.Text("Use /ek in chat to open the full configuration window.");

                using (ImRaii.Disabled(!(_config.Alltalk.RemoteInstance || (_config.Alltalk.LocalInstance && _config.Alltalk.LocalInstall))))
                {
                    if (ImGui.Button("I Understand"))
                    {
                        _config.FirstTime = false;
                        if (!_configWindow.IsOpen)
                            _configWindow.Toggle();
                        Toggle();
                    }
                }
            }
            ConfigWindow.DrawExternalLinkButtons(ImGui.GetContentRegionAvail(), new Vector2(0, 0));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Draw), $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }
}
