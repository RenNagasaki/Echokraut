using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echokraut.Windows.Native;

namespace Echokraut.Windows;

public class NativeWindowManager : IWindowManager
{
    private readonly DialogTalkController _dialogController;
    private readonly NativeConfigWindow _configWindow;
    private readonly NativeFirstTimeWindow _firstTimeWindow;

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
            addonLifecycle);

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
            services.GetService<IGameObjectService>())
        {
            InternalName = "EchokrautSettings",
            Title = "Echokraut Configuration",
            Size = new Vector2(900, 650),
        };

        _firstTimeWindow = new NativeFirstTimeWindow(
            config,
            services.GetService<IAlltalkInstanceService>(),
            services.GetService<IBackendService>(),
            () => _configWindow.Toggle())
        {
            InternalName = "EchokrautFirstTime",
            Title = "First time using Echokraut",
            Size = new Vector2(550, 600),
        };
    }

    public void ToggleConfig() => _configWindow.Toggle();
    public void ToggleFirstTime() => _firstTimeWindow.Toggle();
    public void Draw() { }

    public void Dispose()
    {
        _dialogController.Dispose();
        _configWindow.Dispose();
        _firstTimeWindow.Dispose();
        KamiToolKit.KamiToolKitLibrary.Cleanup();
    }
}
