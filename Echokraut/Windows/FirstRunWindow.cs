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
using Echotools.Logging.DataClasses;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Services;
using Echokraut.Localization;

namespace Echokraut.Windows;
 
public class FirstTimeWindow : Window, IDisposable
{
    private readonly ILogService _log;
    private readonly Configuration _config;
    private readonly IFramework _framework;
    private readonly AlltalkInstanceWindow _alttalkInstanceWindow;
    private readonly ConfigWindow _configWindow;
    private int _wizardStep;

    public FirstTimeWindow(ILogService log, Configuration config, IFramework framework, AlltalkInstanceWindow alttalkInstanceWindow, ConfigWindow configWindow)
        : base($"{Loc.S("First time using Echokraut")}###EKFirstTime")
    {
        _log = log;
        _config = config;
        _framework = framework;
        _alttalkInstanceWindow = alttalkInstanceWindow;
        _configWindow = configWindow;
        Flags = ImGuiWindowFlags.NoScrollbar;
        Size = new Vector2(600, 900);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (_config.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        try
        {
            _framework.RunOnFrameworkThread(() => { _log.UpdateMainThreadLogs(); });

            switch (_wizardStep)
            {
                case 0: DrawStepWelcome(); break;
                case 1: DrawStepConfigure(); break;
                case 2: DrawStepFinish(); break;
            }

            ConfigWindow.DrawExternalLinkButtons(ImGui.GetContentRegionAvail(), new Vector2(0, 0));
        }
        catch (Exception ex)
        {
            _log.Error(nameof(Draw), ex.ToString(), new EKEventId(0, TextSource.Backend));
        }
    }

    private void DrawStepWelcome()
    {
        using (ImRaii.TextWrapPos(0))
        {
            ImGui.Text(Loc.S("Welcome to Echokraut!"));
            ImGui.NewLine();
            ImGui.Text(Loc.S("This plugin gives nearly every text in the game a voice using Alltalk TTS."));
            ImGui.Text(Loc.S("Choose how you want to set up text-to-speech:"));
            ImGui.NewLine();
        }

        var buttonSize = new Vector2(ImGui.GetContentRegionAvail().X, 60);

        if (ImGui.Button(Loc.S("Local TTS\nRuns on your GPU — best quality, requires ~20GB disk space"), buttonSize))
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.Local;
            _config.Save();
            _wizardStep = 1;
        }

        ImGui.NewLine();
        if (ImGui.Button(Loc.S("Remote Server\nConnect to a server running Alltalk (yours or someone else's)"), buttonSize))
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.Remote;
            _config.Save();
            _wizardStep = 1;
        }

        ImGui.NewLine();
        if (ImGui.Button(Loc.S("Audio Files Only\nNo generation — use pre-made audio from friends or Google Drive"), buttonSize))
        {
            _config.Alltalk.InstanceType = AlltalkInstanceType.None;
            _config.Save();
            _wizardStep = 1;
        }
    }

    private void DrawStepConfigure()
    {
        if (ImGui.Button(Loc.S("Back")))
        {
            _wizardStep = 0;
            return;
        }

        ImGui.NewLine();

        var instanceType = _config.Alltalk.InstanceType;

        if (instanceType == AlltalkInstanceType.Local)
        {
            _alttalkInstanceWindow.DrawAlltalk(true);

            ImGui.NewLine();
            using (ImRaii.Disabled(!_config.Alltalk.LocalInstall))
            {
                if (ImGui.Button(Loc.S("Next")))
                    _wizardStep = 2;
            }
        }
        else if (instanceType == AlltalkInstanceType.Remote)
        {
            _alttalkInstanceWindow.DrawRemoteInstance(true);

            ImGui.NewLine();
            if (ImGui.Button(Loc.S("Next")))
                _wizardStep = 2;
        }
        else
        {
            using (ImRaii.TextWrapPos(0))
            {
                using (ImRaii.PushColor(ImGuiCol.Text, LogConstants.ErrorLogColor))
                {
                    ImGui.Text(Loc.S("No audio will be generated in this mode."));
                    ImGui.Text(Loc.S("You will need to get audio files from a friend or via Google Drive."));
                }

                ImGui.NewLine();
                ImGui.Text(Loc.S("Local audio directory (where audio files will be stored):"));
            }

            var localSaveLocation = _config.LocalSaveLocation;
            if (ImGui.InputText("##EKFTLocalPath", ref localSaveLocation, 260))
            {
                _config.LocalSaveLocation = localSaveLocation;
                _config.Save();
            }

            var gdDownload = _config.GoogleDriveDownload;
            if (ImGui.Checkbox(Loc.S("Download from Google Drive"), ref gdDownload))
            {
                _config.GoogleDriveDownload = gdDownload;
                _config.Save();
            }

            using (ImRaii.Disabled(!gdDownload))
            {
                var gdShareLink = _config.GoogleDriveShareLink;
                if (ImGui.InputText($"{Loc.S("Google Drive share link")}##EKFTGDLink", ref gdShareLink, 100))
                {
                    _config.GoogleDriveShareLink = gdShareLink;
                    _config.Save();
                }
            }

            ImGui.NewLine();
            if (ImGui.Button(Loc.S("Next")))
                _wizardStep = 2;
        }
    }

    private void DrawStepFinish()
    {
        if (ImGui.Button(Loc.S("Back")))
        {
            _wizardStep = 1;
            return;
        }

        ImGui.NewLine();
        using (ImRaii.TextWrapPos(0))
        {
            var instanceType = _config.Alltalk.InstanceType;
            ImGui.Text($"Setup mode: {instanceType}");
            ImGui.NewLine();
            ImGui.Text(Loc.S("You're all set! Press the button below to start using Echokraut."));
            ImGui.Text(Loc.S("Use /ek in chat to open the full configuration window at any time."));
            ImGui.NewLine();
        }

        var canFinish = _config.Alltalk.InstanceType == AlltalkInstanceType.Remote
            || (_config.Alltalk.InstanceType == AlltalkInstanceType.Local && _config.Alltalk.LocalInstall)
            || _config.Alltalk.InstanceType == AlltalkInstanceType.None;

        using (ImRaii.Disabled(!canFinish))
        {
            if (ImGui.Button(Loc.S("I Understand")))
            {
                _config.FirstTime = false;
                _config.Save();
                if (!_configWindow.IsOpen)
                    _configWindow.Toggle();
                Toggle();
            }
        }
    }
}
