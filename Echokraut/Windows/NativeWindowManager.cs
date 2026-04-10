using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Localization;
using Echokraut.Services;
using Echotools.Logging.Services;
using Echokraut.Windows.Native;
using KamiToolKit;

namespace Echokraut.Windows;

public class NativeWindowManager : IWindowManager
{
    private readonly DialogTalkController _dialogController;
    private readonly NativeConfigWindow _configWindow;
    private readonly NativeFirstTimeWindow _firstTimeWindow;
    private readonly NativeVoiceClipManagerWindow _voiceClipManagerWindow;
    private readonly NativeVoiceClipDetailWindow _voiceClipDetailWindow;

    public bool IsFirstTimeOpen => _firstTimeWindow.IsOpen;

    public NativeWindowManager(
        ServiceContainer services,
        Configuration config,
        IAddonLifecycle addonLifecycle,
        ICommandManager commandManager,
        IClientState clientState)
    {
        var addonTalk = services.GetService<IAddonTalkHelper>();

        _dialogController = new DialogTalkController(
            config,
            services.GetService<IAudioPlaybackService>(),
            services.GetService<ILipSyncHelper>(),
            () => addonTalk.RecreateInference(),
            () => _configWindow.Toggle(),
            addonLifecycle,
            services.GetService<ILogService>(),
            services.GetService<INpcDataService>());

        _configWindow = new NativeConfigWindow(
            config,
            services.GetService<IAudioPlaybackService>(),
            services.GetService<IBackendService>(),
            services.GetService<IGoogleDriveSyncService>(),
            services.GetService<IAlltalkInstanceService>(),
            services.GetService<IAudioFileService>(),
            services.GetService<IJsonDataService>(),
            services.GetService<ICommandService>(),
            commandManager,
            clientState,
            services.GetService<ILogService>(),
            services.GetService<INpcDataService>(),
            services.GetService<IVolumeService>(),
            services.GetService<IGameObjectService>(),
            services.GetService<IVoiceTestService>(),
            services.GetService<IDialogHarvestService>(),
            services.GetService<IDatabaseService>())
        {
            InternalName = "EchokrautSettings",
            Title = Loc.S("Configuration"),
            Size = new Vector2(970, 650),
            RespectCloseAll = false,
        };

        _voiceClipDetailWindow = new NativeVoiceClipDetailWindow(
            services.GetService<IDatabaseService>(),
            services.GetService<IVoiceClipManagerService>(),
            services.GetService<IAudioPlaybackService>(),
            services.GetService<IGameObjectService>())
        {
            InternalName = "EchokrautEncounterDetail",
            Title = Loc.S("Voice Clip Detail"),
            Size = new Vector2(900, 500),
            RespectCloseAll = false,
        };

        _voiceClipManagerWindow = new NativeVoiceClipManagerWindow(
            services.GetService<IDatabaseService>(),
            services.GetService<IVoiceClipManagerService>(),
            services.GetService<IAudioPlaybackService>(),
            services.GetService<INpcDataService>(),
            services.GetService<IDialogHarvestService>(),
            services.GetService<IGameObjectService>(),
            clientState)
        {
            InternalName = "EchokrautEncounters",
            Title = Loc.S("Voice Clip Manager"),
            Size = new Vector2(1000, 600),
            RespectCloseAll = false,
        };
        _voiceClipManagerWindow.SetDetailWindow(_voiceClipDetailWindow);

        _firstTimeWindow = new NativeFirstTimeWindow(
            config,
            services.GetService<IAlltalkInstanceService>(),
            services.GetService<IBackendService>(),
            () => _configWindow.Toggle())
        {
            InternalName = "EchokrautFirstTime",
            Title = Loc.S("First Time Setup"),
            Size = new Vector2(550, 600),
            RespectCloseAll = false,
        };

        _configWindow.OnToggleVoiceClipManager = () => _voiceClipManagerWindow.Toggle();

        // Suppress Talk advance when clicking inside our native windows
        _dialogController.SetWindowHitTest(pos => IsInsideWindow(_configWindow, pos) || IsInsideWindow(_firstTimeWindow, pos) || IsInsideWindow(_voiceClipManagerWindow, pos) || IsInsideWindow(_voiceClipDetailWindow, pos));
    }

    private static unsafe bool IsInsideWindow(NativeAddon window, Vector2 pos)
    {
        if (!window.IsOpen) return false;
        var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)window;
        if (addon == null) return false;
        var x = addon->GetX();
        var y = addon->GetY();
        return pos.X >= x && pos.X <= x + window.Size.X
            && pos.Y >= y && pos.Y <= y + window.Size.Y;
    }

    public void ToggleConfig() => _configWindow.Toggle();
    public void ToggleFirstTime() => _firstTimeWindow.Toggle();
    public void ToggleVoiceClipManager() => _voiceClipManagerWindow.Toggle();
    public void Draw() { }

    public void Dispose()
    {
        _dialogController.Dispose();
        _configWindow.Dispose();
        _firstTimeWindow.Dispose();
        _voiceClipManagerWindow.Dispose();
        _voiceClipDetailWindow.Dispose();
    }
}
