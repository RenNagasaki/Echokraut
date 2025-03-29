using System;
using System.Numerics;
using System.Reflection;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Data;
using Echokraut.Helper.Functional;
using ImGuiNET;
using OtterGui;
using OtterGui.Raii;

namespace Echokraut.Windows;

public class AlltalkInstallWindow : Window, IDisposable
{
    private readonly Configuration? configuration;
    public static bool installing;

    public AlltalkInstallWindow(Configuration configuration) : base($"Echokraut Alltalk Installation###EKAlltalkInstall")
    {
        Flags = ImGuiWindowFlags.AlwaysVerticalScrollbar & ImGuiWindowFlags.HorizontalScrollbar &
                ImGuiWindowFlags.AlwaysHorizontalScrollbar;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.configuration = configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (configuration!.IsConfigWindowMovable)
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
            var localSaveLocation = configuration.LocalSaveLocation;
            if (ImGui.InputText($"##EKSavePath", ref localSaveLocation, 40))
            {
                configuration.LocalSaveLocation = localSaveLocation;
                configuration.Save();
            }

            using (ImRaii.Disabled(installing))
            {
                if (ImGuiUtil.DrawDisabledButton(
                        $"Install local Alltalk##installAlltalkLocally",
                        new Vector2(500, 25),
                        "Will install an alltalk instance locally and set it up for immediate use.",
                        false
                    )
                   )
                {
                    AlltalkInstallHelper.Install(configuration.Alltalk.AlltalkUrl, configuration.Alltalk.LocalInstallPath, configuration.Alltalk.ModelUrl, configuration.Alltalk.VoicesUrl, new EKEventId(0, TextSource.AlltalkInstall));
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod().Name, $"Something went wrong: {ex}", new EKEventId(0, TextSource.AlltalkInstall));
        }
    }
}
