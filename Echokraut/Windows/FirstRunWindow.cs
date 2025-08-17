using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
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
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;

namespace Echokraut.Windows;
 
public class FirstTimeWindow : Window, IDisposable
{
    public FirstTimeWindow() : base($"First time using Echokraut###EKFirstTime")
    {
        Flags = ImGuiWindowFlags.NoScrollbar;
        Size = new Vector2(600, 900);
        SizeCondition = ImGuiCond.FirstUseEver;

        if (!Plugin.Configuration.Alltalk.LocalInstance && !Plugin.Configuration.Alltalk.RemoteInstance)
        { 
            if (!string.IsNullOrWhiteSpace(Plugin.Configuration.Alltalk.BaseUrl) && !Plugin.Configuration.Alltalk.BaseUrl.Contains("127.0.0.1"))
                Plugin.Configuration.Alltalk.RemoteInstance = true;
            else
                Plugin.Configuration.Alltalk.LocalInstance = true;
        }
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Plugin.Configuration.IsConfigWindowMovable)
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
            Plugin.Framework.RunOnFrameworkThread(() => {LogHelper.UpdateLogList(); });
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

            AlltalkInstanceWindow.DrawAlltalk(true);

            using (ImRaii.TextWrapPos(0))
            {
                ImGui.Text(
                    "Pressing this button will close the install window and enable you to fully use & configure Echokraut.");
                ImGui.Text("Use /ek in chat to open the full configuration window.");

                using (ImRaii.Disabled(!(Plugin.Configuration.Alltalk.RemoteInstance || (Plugin.Configuration.Alltalk.LocalInstance && Plugin.Configuration.Alltalk.LocalInstall))))
                {
                    if (ImGui.Button("I Understand"))
                    {
                        Plugin.Configuration.FirstTime = false;
                        if (!Plugin.ConfigWindow.IsOpen)
                            Plugin.ConfigWindow.Toggle();
                        Toggle();
                    }
                }
            }
            ConfigWindow.DrawExternalLinkButtons(ImGui.GetContentRegionAvail(), new Vector2(0, 0));
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.Backend));
        }
    }
}
