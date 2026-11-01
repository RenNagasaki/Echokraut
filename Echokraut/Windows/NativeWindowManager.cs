using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
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
    private readonly NativeChangelogWindow _changelogWindow;
    private readonly IFramework _framework;
    private readonly Configuration _config;
    private readonly IChangelogService _changelog;

    public bool IsFirstTimeOpen => _firstTimeWindow.IsOpen;
    public bool IsChangelogOpen => _changelogWindow.IsOpen;

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
        _changelog = services.GetService<IChangelogService>();
        var addonTalk = services.GetService<IAddonTalkHelper>();

        _dialogController = new DialogTalkController(
            config,
            services.GetService<IAudioPlaybackService>(),
            services.GetService<ILipSyncHelper>(),
            () => addonTalk.RecreateInference(),
            () => _voiceClipManagerWindow.Toggle(),
            addonLifecycle,
            framework,
            services.GetService<ILogService>(),
            services.GetService<INpcDataService>(),
            services.GetService<IEchokrautIpc>(),
            services.GetService<IDialogStateService>());

        _configWindow = new NativeConfigWindow(
            config,
            services.GetService<IAudioPlaybackService>(),
            services.GetService<IBackendService>(),
            services.GetService<IGoogleDriveSyncService>(),
            services.GetService<IAlltalkInstanceService>(),
            services.GetService<IEchokrauTtsInstanceService>(),
            services.GetService<ITtsVoiceSyncService>(),
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
            services.GetService<IDatabaseService>(),
            services.GetService<IBatchModeService>())
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
            services.GetService<IEchokrauTtsInstanceService>(),
            services.GetService<IBackendService>(),
            framework,
            services.GetService<IBatchModeService>(),
            () =>
            {
                // Brand-new installs land on this branch when the user finishes the wizard.
                // Bump LastSeenChangelogVersion to current so the changelog popup doesn't
                // immediately follow the wizard with notes about features the user already
                // starts with — they have nothing to "catch up" on. Existing users (who
                // never see the wizard a second time) keep their stored marker.
                _changelog.MarkAllSeen();
                _configWindow.Toggle();
            })
        {
            InternalName = "EchokrautFirstTime",
            Title = Loc.S("First Time Setup"),
            Size = new Vector2(550, 600),
            RespectCloseAll = false,
        };

        _changelogWindow = new NativeChangelogWindow(_changelog)
        {
            InternalName = "EchokrautChangelog",
            Title = Loc.S("What's New"),
            Size = new Vector2(720, 600),
            RespectCloseAll = false,
        };

        _gameDataToolsWindow = new NativeGameDataToolsWindow(
            services.GetService<IDialogHarvestService>(),
            services.GetService<IVoicePackService>(),
            services.GetService<IAudioPackageService>(),
            services.GetService<INpcAttributionRepairService>(),
            services.GetService<IDatabaseService>(),
            services.GetService<IGameObjectService>(),
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

        // Suppress Talk advance when clicking inside our native windows.
        // EVERY owned window has to be listed: a window missing here lets its clicks reach the
        // Talk addon, which advances the dialogue underneath. The two popups are created on
        // demand by their parent window, so they are reached through the parent's accessor.
        _dialogController.SetWindowHitTest(pos => OwnedWindows().Any(w => IsInsideWindow(w, pos)));
    }

    /// <summary>
    /// Every native window Echokraut owns, including the two popups their parent windows create on
    /// demand. Single source of truth for the dialogue-advance hit-test.
    /// </summary>
    private IEnumerable<KamiToolKit.BaseTypes.NativeAddon?> OwnedWindows()
    {
        yield return _configWindow;
        yield return _firstTimeWindow;
        yield return _voiceClipManagerWindow;
        yield return _voiceClipDetailWindow;
        yield return _gameDataToolsWindow;
        yield return _changelogWindow;
        yield return _configWindow.VoiceConfigWindow;
        yield return _voiceClipManagerWindow.NpcEditWindow;
    }

    private static unsafe bool IsInsideWindow(KamiToolKit.BaseTypes.NativeAddon? window, Vector2 pos)
    {
        if (window is null || !window.IsOpen) return false;
        var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)window;
        if (addon == null) return false;

        // The game draws the addon at Size * Scale. Comparing against the configured Size alone
        // made the rectangle too small at any HUD scale above 100%, and clicks in the missing strip
        // advanced the dialogue. GetScaledWidth/Height is the addon's own measurement and wins;
        // Size * Scale covers the frames before it has measured itself.
        var origin = new Vector2(addon->GetX(), addon->GetY());
        var extent = WindowHitTest.ScreenExtent(
            window.Size, addon->Scale, addon->GetScaledWidth(true), addon->GetScaledHeight(true));
        return WindowHitTest.Contains(pos, origin, extent);
    }

    public void ToggleConfig() => _configWindow.Toggle();
    public void ToggleFirstTime() => _firstTimeWindow.Toggle();
    public void ToggleVoiceClipManager() => _voiceClipManagerWindow.Toggle();
    public void ToggleGameDataTools()
    {
        // None-mode users have no live backend, so the harvest + voice-extract operations
        // exposed by Game Data Tools have nothing useful to drive. Refusing to open the
        // window is the functional twin of the dimmed buttons in NativeConfigWindow / VCM.
        if (!_config.HasLiveGeneration) return;
        _gameDataToolsWindow.Toggle();
    }
    public void ToggleChangelog() => _changelogWindow.Toggle();
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
        SafeClose(_changelogWindow);

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
        SafeDispose(_changelogWindow);
    }

    private static void SafeClose(KamiToolKit.BaseTypes.NativeAddon? addon)
    {
        if (addon == null) return;
        try { if (addon.IsOpen) addon.Close(); } catch { }
    }

    private static void SafeDispose(KamiToolKit.BaseTypes.NativeAddon? addon)
    {
        if (addon == null) return;
        try { addon.Dispose(); } catch { }
    }
}
