using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.Windows;
using Dalamud.Game;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.IO;
using Echokraut.DataClasses;
using Echotools.Logging.DataClasses;
using Dalamud.Game.Text.SeStringHandling;
using GameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.IoC;
using Echokraut.Backend;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Echokraut.Services;
using Echotools.Logging.Services;

namespace Echokraut;

public partial class Plugin : IDalamudPlugin
{
    public static readonly string PluginVersion =
        $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!.ToString(3)}";

    // Service container for dependency injection
    private readonly ServiceContainer _services;

    // Commonly used services
    private readonly ILogService _log;
    private readonly IVoiceMessageProcessor _voiceProcessor;
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly IBackendService _backend;
    private readonly IGoogleDriveSyncService _googleDrive;
    private readonly IAlltalkInstanceService _alltalkInstance;
    private readonly ICommandService _commands;

    // Camera pointer (unsafe, stays in Plugin)
    private static unsafe Camera* _camera = null;

    // Dalamud services
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ICommandManager _commandManager;
    private readonly IAddonLifecycle _addonLifecycle;

    // Configuration and window manager
    private readonly Configuration _configuration;
    private IWindowManager _windowManager;
    private bool _kamiToolKitInitialized;

    // Addon helpers
    private readonly ISoundHelper _soundHelper;
    private readonly IAddonTalkHelper _addonTalkHelper;
    private readonly IAddonBattleTalkHelper _addonBattleTalkHelper;
    private readonly IAddonSelectStringHelper _addonSelectStringHelper;
    private readonly IAddonCutSceneSelectStringHelper _addonCutSceneSelectStringHelper;
    private readonly IAddonBubbleHelper _addonBubbleHelper;
    private readonly IChatTalkHelper _chatTalkHelper;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        ICommandManager commandManager,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        ICondition condition,
        IObjectTable objectTable,
        IDataManager dataManager,
        IChatGui chatGui,
        IGameGui gameGui,
        ISigScanner sigScanner,
        IGameInteropProvider gameInteropProvider,
        IGameConfig gameConfig,
        IAddonLifecycle addonLifecycle,
        ITextureProvider textureProvider)
    {
        NativeLibraryLoader.Initialize(
            Path.GetDirectoryName(pluginInterface.AssemblyLocation.FullName)!);

        _pluginInterface = pluginInterface;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _commandManager = commandManager;
        _addonLifecycle = addonLifecycle;

        Localization.Loc.Init(clientState.ClientLanguage);

        _configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.Initialize(pluginInterface);
        pluginInterface.UiBuilder.DisableCutsceneUiHide = !_configuration.HideUiInCutscenes;

        _services = ServiceBuilder.BuildServices(
            log, gameConfig, clientState, objectTable, commandManager,
            chatGui, condition, _configuration, framework, dataManager,
            addonLifecycle, sigScanner, gameInteropProvider, pluginInterface);

        _log          = _services.GetService<ILogService>();
        _voiceProcessor = _services.GetService<IVoiceMessageProcessor>();
        _audioPlayback  = _services.GetService<IAudioPlaybackService>();
        _backend        = _services.GetService<IBackendService>();
        _googleDrive    = _services.GetService<IGoogleDriveSyncService>();
        _alltalkInstance = _services.GetService<IAlltalkInstanceService>();
        _commands       = _services.GetService<ICommandService>();

        var npcData = _services.GetService<INpcDataService>();
        npcData.RefreshSelectables(_configuration.EchokrautVoices);

        _soundHelper                    = _services.GetService<ISoundHelper>();
        _addonTalkHelper                = _services.GetService<IAddonTalkHelper>();
        _addonBattleTalkHelper          = _services.GetService<IAddonBattleTalkHelper>();
        _addonSelectStringHelper        = _services.GetService<IAddonSelectStringHelper>();
        _addonCutSceneSelectStringHelper = _services.GetService<IAddonCutSceneSelectStringHelper>();
        _addonBubbleHelper              = _services.GetService<IAddonBubbleHelper>();
        _chatTalkHelper                 = _services.GetService<IChatTalkHelper>();

        _windowManager = CreateWindowManager();
        WireEvents();

        _framework.Update += OnFrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowManager.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += _commands.ToggleConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi += _commands.ToggleConfigUi;
        _clientState.Login += OnLogin;

        HandleStartup();

        _log.Info(nameof(Plugin), "Echokraut initialized", new EKEventId(0, TextSource.None));
    }

    private IWindowManager CreateWindowManager()
    {
        if (_configuration.UseNativeUI)
        {
            if (!_kamiToolKitInitialized)
            {
                KamiToolKit.KamiToolKitLibrary.Initialize(_pluginInterface, $"Echokraut {PluginVersion}");
                _kamiToolKitInitialized = true;
            }
            return new NativeWindowManager(_services, _configuration, _addonLifecycle, _commandManager, _clientState);
        }
        return new ImGuiWindowManager(_services, _configuration, _framework, _clientState, _commandManager, _pluginInterface);
    }

    public void SwitchUiMode()
    {
        // Defer to next framework tick — can't dispose a window from within its own draw/click callback
        _framework.RunOnTick(() =>
        {
            // Unwire events from old window manager
            _commands.ToggleConfigRequested    -= _windowManager.ToggleConfig;
            _commands.ToggleFirstTimeRequested -= _windowManager.ToggleFirstTime;
            _pluginInterface.UiBuilder.Draw   -= _windowManager.Draw;

            // Dispose old window manager
            _windowManager.Dispose();

            // Create new window manager
            _windowManager = CreateWindowManager();

            // Rewire events
            _commands.ToggleConfigRequested    += _windowManager.ToggleConfig;
            _commands.ToggleFirstTimeRequested += _windowManager.ToggleFirstTime;
            _pluginInterface.UiBuilder.Draw   += _windowManager.Draw;

            // Open the config window in the new UI mode
            _windowManager.ToggleConfig();
        });
    }

    private void WireEvents()
    {
        _audioPlayback.AutoAdvanceRequested += eventId => _addonTalkHelper.Click(eventId);
        _audioPlayback.CurrentMessageChanged += msg => DialogState.CurrentVoiceMessage = msg;

        _commands.ToggleConfigRequested    += _windowManager.ToggleConfig;
        _commands.ToggleFirstTimeRequested += _windowManager.ToggleFirstTime;
        _commands.CancelAllRequested += _ => _audioPlayback?.ClearQueue();
        _commands.UiModeSwitchRequested += SwitchUiMode;
    }

    private void HandleStartup()
    {
        if (_configuration.FirstTime && !_windowManager.IsFirstTimeOpen && _clientState.IsLoggedIn)
            _commands.ToggleFirstTimeUi();

        if (!_configuration.FirstTime && _clientState.IsLoggedIn
            && _configuration.Alltalk.LocalInstall
            && _configuration.Alltalk.InstanceType == Echokraut.Enums.AlltalkInstanceType.Local
            && _configuration.Alltalk.AutoStartLocalInstance)
        {
            _alltalkInstance.StartInstance();
            _backend.RefreshBackend();
        }
 
        if (_configuration.GoogleDriveDownloadPeriodically)
            _googleDrive.StartSync();
    }

    private void OnLogin()
    {
        try 
        { 
            if (_configuration.GoogleDriveDownload)
                _googleDrive.DownloadFolder(_configuration.LocalSaveLocation, _configuration.GoogleDriveShareLink);
 
            if (_configuration.FirstTime && !_windowManager.IsFirstTimeOpen)
                _commands.ToggleFirstTimeUi();

            if (!_configuration.FirstTime
                && _configuration.Alltalk.LocalInstall
                && _configuration.Alltalk.InstanceType == Echokraut.Enums.AlltalkInstanceType.Local
                && !_alltalkInstance.InstanceRunning
                && !_alltalkInstance.InstanceStarting)
            { 
                _alltalkInstance.StartInstance();
                _backend.RefreshBackend();
            }  
        }
        catch (Exception e)
        { 
            _log.Error(nameof(OnLogin), $"Error while starting voice inference: {e}", new EKEventId(0, TextSource.None));
        }
    }
 
    private unsafe void OnFrameworkUpdate(Dalamud.Plugin.Services.IFramework fw)
    {
        if (_camera == null) 
        { 
            var mgr = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance();
            if (mgr != null) _camera = mgr->GetActiveCamera();
        }

        var localPlayer = _objectTable.LocalPlayer;
        if (_camera != null && localPlayer != null)
        {
            var matrix = _camera->CameraBase.SceneCamera.ViewMatrix;
            _audioPlayback.UpdateListenerState(
                localPlayer.Position,
                matrix[2], matrix[1], matrix[0],
                matrix[6], matrix[5], matrix[4]);
        }
    }

    public void Dispose()
    {
        _log.Info(nameof(Dispose), "Echokraut shutting down", new EKEventId(0, TextSource.None));
        _framework.Update -= OnFrameworkUpdate;
        _pluginInterface.UiBuilder.Draw -= _windowManager.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= _commands.ToggleConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi -= _commands.ToggleConfigUi;
        _clientState.Login -= OnLogin;
        _googleDrive.StopSync();
        _soundHelper.Dispose();
        _addonTalkHelper.Dispose();
        _addonBattleTalkHelper.Dispose();
        _addonCutSceneSelectStringHelper.Dispose();
        _addonSelectStringHelper.Dispose();
        _addonBubbleHelper.Dispose();
        _chatTalkHelper.Dispose();

        _configuration.Save();
        _windowManager.Dispose();
        _commands.Dispose();
        _services.Dispose();
    }
}
