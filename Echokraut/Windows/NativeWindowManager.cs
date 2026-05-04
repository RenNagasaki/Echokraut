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
    private readonly NativeGameDataToolsWindow _gameDataToolsWindow;
    private readonly IFramework _framework;
    private readonly Configuration _config;

    public bool IsFirstTimeOpen => _firstTimeWindow.IsOpen;

    public NativeWindowManager(
        ServiceContainer services,
        Configuration config,
        IAddonLifecycle addonLifecycle,
        ICommandManager commandManager,
        IClientState clientState,
        IFramework framework)
    {
        _framework = framework;
        _config = config;
        var addonTalk = services.GetService<IAddonTalkHelper>();

        _dialogController = new DialogTalkController(
            config,
            services.GetService<IAudioPlaybackService>(),
            services.GetService<ILipSyncHelper>(),
            () => addonTalk.RecreateInference(),
            () => _voiceClipManagerWindow.Toggle(),
            addonLifecycle,
            services.GetService<ILogService>(),
            services.GetService<INpcDataService>(),
            services.GetService<IEchokrautIpc>());

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
            services.GetService<IGameObjectService>(),
            config)
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
            services.GetService<IGameObjectService>(),
            clientState,
            services.GetService<ILogService>(),
            services.GetService<IBackendService>(),
            config,
            ToggleConfig,
            ToggleGameDataTools)
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
            framework,
            () => _configWindow.Toggle())
        {
            InternalName = "EchokrautFirstTime",
            Title = Loc.S("First Time Setup"),
            Size = new Vector2(550, 600),
            RespectCloseAll = false,
        };

        _gameDataToolsWindow = new NativeGameDataToolsWindow(
            services.GetService<IDialogHarvestService>(),
            services.GetService<IVoiceSampleExtractorService>(),
            clientState,
            services.GetService<ILogService>(),
            config,
            ToggleConfig,
            ToggleVoiceClipManager)
        {
            InternalName = "EchokrautGameTools",
            Title = Loc.S("Game Data Tools"),
            Size = new Vector2(720, 540),
            RespectCloseAll = false,
        };

        _configWindow.OnToggleVoiceClipManager = () => _voiceClipManagerWindow.Toggle();
        // Route through ToggleGameDataTools so the None-mode guard catches every call site
        // (the config-window button + the VC Manager button + the public method itself).
        _configWindow.OnToggleGameDataTools = ToggleGameDataTools;

        // Suppress Talk advance when clicking inside our native windows
        _dialogController.SetWindowHitTest(pos => IsInsideWindow(_configWindow, pos)
            || IsInsideWindow(_firstTimeWindow, pos)
            || IsInsideWindow(_voiceClipManagerWindow, pos)
            || IsInsideWindow(_voiceClipDetailWindow, pos)
            || IsInsideWindow(_gameDataToolsWindow, pos));
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
    public void ToggleGameDataTools()
    {
        // None-mode users have no live backend, so the harvest + voice-extract operations
        // exposed by Game Data Tools have nothing useful to drive. Refusing to open the
        // window is the functional twin of the dimmed buttons in NativeConfigWindow / VCM.
        if (!_config.Alltalk.HasLiveGeneration) return;
        _gameDataToolsWindow.Toggle();
    }
    public void Draw() { }

    public void Dispose()
    {
        // Close all native windows first to start the (async, framework-thread) ATK detach,
        // THEN dispose. NativeAddon.Dispose() internally calls Close() too but immediately
        // continues with managed cleanup — without explicit close-first, a still-attached
        // ATK addon can dangle a pointer into freed managed memory and crash later in
        // AtkUldManager.UpdateDrawNodeList when the game tries to tear down the addon.
        SafeClose(_configWindow);
        SafeClose(_firstTimeWindow);
        SafeClose(_voiceClipManagerWindow);
        SafeClose(_voiceClipDetailWindow);
        SafeClose(_gameDataToolsWindow);

        // Give the framework thread a chance to actually process the queued ATK Close calls
        // BEFORE we free our managed handles. We can only block when we're NOT on the
        // framework thread ourselves — otherwise we'd deadlock the very thread that needs
        // to drain the queue. Plugin.Dispose is sometimes on the framework thread (manual
        // toggle in /xlplugins) and sometimes on a thread-pool thread (file-watcher hot
        // reload via LocalDevPlugin) — we have to handle both.
        if (!_framework.IsInFrameworkUpdateThread)
        {
            try { _framework.DelayTicks(2).Wait(300); } catch { }
        }

        _dialogController.Dispose();
        SafeDispose(_configWindow);
        SafeDispose(_firstTimeWindow);
        SafeDispose(_voiceClipManagerWindow);
        SafeDispose(_voiceClipDetailWindow);
        SafeDispose(_gameDataToolsWindow);
    }

    private static void SafeClose(KamiToolKit.NativeAddon? addon)
    {
        if (addon == null) return;
        try { if (addon.IsOpen) addon.Close(); } catch { }
    }

    private static void SafeDispose(KamiToolKit.NativeAddon? addon)
    {
        if (addon == null) return;
        try { addon.Dispose(); } catch { }
    }
}
