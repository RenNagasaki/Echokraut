using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echotools.Logging.Services;

namespace Echokraut.Windows;

public class ImGuiWindowManager : IWindowManager
{
    private readonly WindowSystem _windowSystem = new("Echokraut");
    private readonly ConfigWindow _configWindow;
    private readonly FirstTimeWindow _firstTimeWindow;
    private readonly DialogExtraOptionsWindow _dialogExtraOptionsWindow;

    public bool IsFirstTimeOpen => _firstTimeWindow.IsOpen;

    public ImGuiWindowManager(
        ServiceContainer services,
        Configuration config,
        IFramework framework,
        IClientState clientState,
        ICommandManager commandManager,
        IDalamudPluginInterface pluginInterface)
    {
        var log           = services.GetService<ILogService>();
        var volume        = services.GetService<IVolumeService>();
        var commands      = services.GetService<ICommandService>();
        var backend       = services.GetService<IBackendService>();
        var audioPlayback = services.GetService<IAudioPlaybackService>();
        var jsonData      = services.GetService<IJsonDataService>();
        var audioFile     = services.GetService<IAudioFileService>();
        var gameObject    = services.GetService<IGameObjectService>();
        var googleDrive   = services.GetService<IGoogleDriveSyncService>();
        var npcData       = services.GetService<INpcDataService>();
        var lipSync       = services.GetService<ILipSyncHelper>();
        var addonTalk     = services.GetService<IAddonTalkHelper>();
        var alttalkInstanceWindow = services.GetService<AlltalkInstanceWindow>();

        var voiceTest = services.GetService<IVoiceTestService>();

        _configWindow = new ConfigWindow(
            log, volume, config, framework, commands, commandManager,
            pluginInterface, backend, audioPlayback, clientState,
            jsonData, audioFile, gameObject, googleDrive, npcData,
            voiceTest, alttalkInstanceWindow);

        _firstTimeWindow = new FirstTimeWindow(
            log, config, framework, alttalkInstanceWindow, _configWindow);

        _dialogExtraOptionsWindow = new DialogExtraOptionsWindow(
            log, config, audioPlayback, lipSync,
            () => addonTalk.RecreateInference());

        _windowSystem.AddWindow(_configWindow);
        _windowSystem.AddWindow(alttalkInstanceWindow);
        _windowSystem.AddWindow(_firstTimeWindow);
        _windowSystem.AddWindow(_dialogExtraOptionsWindow);
    }

    public void ToggleConfig() => _configWindow.Toggle();
    public void ToggleFirstTime() => _firstTimeWindow.Toggle();
    public void Draw() => _windowSystem.Draw();

    public void Dispose()
    {
        _windowSystem.RemoveAllWindows();
        _configWindow.Dispose();
    }
}
