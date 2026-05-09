using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.Windows;
using Dalamud.Game;
using Echokraut.Enums;
using Echotools.Logging.Enums;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!}";

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

        if (!_kamiToolKitInitialized)
        {
            KamiToolKit.KamiToolKitLibrary.Initialize(_pluginInterface, $"Echokraut {PluginVersion}");
            _kamiToolKitInitialized = true;
        }
        _windowManager = new NativeWindowManager(_services, _configuration, _addonLifecycle, _commandManager, _clientState, _framework);
        WireEvents();

        _framework.Update += OnFrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowManager.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += _commands.ToggleConfigUi;
        _pluginInterface.UiBuilder.OpenMainUi += _commands.ToggleVoiceClipManagerUi;
        _clientState.Login += OnLogin;

        HandleStartup();

        _log.Info(nameof(Plugin), "Echokraut initialized", new EKEventId(0, TextSource.None));
    }

    private void WireEvents()
    {
        _audioPlayback.AutoAdvanceRequested += eventId => _addonTalkHelper.Click(eventId);
        _audioPlayback.CurrentMessageChanged += msg => DialogState.CurrentVoiceMessage = msg;

        _commands.ToggleConfigRequested    += _windowManager.ToggleConfig;
        _commands.ToggleFirstTimeRequested += _windowManager.ToggleFirstTime;
        _commands.ToggleVoiceClipManagerRequested += _windowManager.ToggleVoiceClipManager;
        _commands.ToggleGameDataToolsRequested += _windowManager.ToggleGameDataTools;
        _commands.CancelAllRequested += _ => _audioPlayback?.ClearQueue();
    }

    private void HandleStartup()
    {
        if (_configuration.FirstTime && !_windowManager.IsFirstTimeOpen && _clientState.IsLoggedIn)
            _commands.ToggleFirstTimeUi();

        // Show the changelog window after a plugin update — but only AFTER FirstTime
        // has been completed at least once (else a brand-new install would see the
        // wizard AND the changelog stacked, which is noisy and shows changes the user
        // hasn't lived through). The FirstTime "I Understand" callback bumps
        // LastSeenChangelogVersion to current, so brand-new installs naturally have no
        // unseen entries the first time this gate fires.
        if (!_configuration.FirstTime
            && _clientState.IsLoggedIn
            && !_windowManager.IsChangelogOpen
            && _services.GetService<IChangelogService>().HasUnseenChangelogs())
        {
            _windowManager.ToggleChangelog();
        }

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

        // Run Lodestone enrichment for player records with Race=Unknown.
        // Requires login (HomeWorld lookup needs LocalPlayer); if not logged in yet, OnLogin runs it.
        if (_clientState.IsLoggedIn)
            StartPlayerEnrichment();

        // JSON-config + audio-file legacy migrations need a logged-in player (placeholder
        // detection in the audio backfill reads LocalPlayerName / LocalPlayerContentId).
        // If we caught a plugin reload mid-session run them now; otherwise OnLogin picks up.
        if (_clientState.IsLoggedIn)
            RunDataMigrationsIfLoggedIn();
    }

    /// <summary>
    /// Runs the one-shot JSON-config → SQLite migration and the legacy audio-file
    /// backfill, in that order. Both are gated on the player being logged in (needed
    /// for the per-row Language stamp and the placeholder-detection heuristic). Both
    /// guard themselves: <see cref="IDatabaseService.NeedsMigration"/> returns false
    /// after the JSON lists are emptied; the backfill consults
    /// <see cref="Configuration.AudioFilesBackfillPending"/> and clears it on success.
    /// Idempotent — safe to call from both <see cref="HandleStartup"/> (plugin reload
    /// while logged in) and <see cref="OnLogin"/> (cold login).
    /// </summary>
    private void RunDataMigrationsIfLoggedIn()
    {
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            var db = _services.GetService<IDatabaseService>();
            if (db.NeedsMigration(_configuration))
            {
                _log.Info(nameof(RunDataMigrationsIfLoggedIn),
                    "Running JSON-config → SQLite migration", eventId);
                db.MigrateFromConfig(_configuration);
            }

            if (_configuration.AudioFilesBackfillPending)
            {
                _log.Info(nameof(RunDataMigrationsIfLoggedIn),
                    "Running legacy audio-file backfill", eventId);
                var gameObjects = _services.GetService<IGameObjectService>();
                var audioFiles = _services.GetService<IAudioFileService>();
                db.BackfillAudioFiles(_configuration, gameObjects, audioFiles);
            }
        }
        catch (Exception ex)
        {
            _log.Error(nameof(RunDataMigrationsIfLoggedIn),
                $"Data migration failed: {ex}", eventId);
        }
    }

    private void StartPlayerEnrichment()
    {
        try
        {
            var enricher = _services.GetService<IPlayerLodestoneEnricher>();
            _ = Task.Run(() => enricher.RunAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(StartPlayerEnrichment), $"Failed to start enrichment: {ex.Message}",
                new EKEventId(0, TextSource.None));
        }
    }

    private void OnLogin()
    {
        try 
        { 
            if (_configuration.GoogleDriveDownload)
                _googleDrive.DownloadFolder(_configuration.LocalSaveLocation, _configuration.GoogleDriveShareLink);
 
            if (_configuration.FirstTime && !_windowManager.IsFirstTimeOpen)
                _commands.ToggleFirstTimeUi();

            // Mirror the changelog gate from HandleStartup: cold-login covers the case
            // where the plugin was already loaded (HandleStartup ran before login) and
            // the user only just became eligible to see windows.
            if (!_configuration.FirstTime
                && !_windowManager.IsChangelogOpen
                && _services.GetService<IChangelogService>().HasUnseenChangelogs())
            {
                _windowManager.ToggleChangelog();
            }

            if (!_configuration.FirstTime
                && _configuration.Alltalk.LocalInstall
                && _configuration.Alltalk.InstanceType == Echokraut.Enums.AlltalkInstanceType.Local
                && !_alltalkInstance.InstanceRunning
                && !_alltalkInstance.InstanceStarting)
            {
                _alltalkInstance.StartInstance();
                _backend.RefreshBackend();
            }

            // Lodestone enrichment runs once per session — HomeWorld is now available.
            StartPlayerEnrichment();

            // JSON-config + audio-file legacy migrations were deferred from plugin init
            // because they need LocalPlayerName / LocalPlayerContentId. Run them now that
            // we have both. Internally idempotent — safe even if HandleStartup already
            // fired them on a hot reload.
            RunDataMigrationsIfLoggedIn();
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
        _pluginInterface.UiBuilder.OpenMainUi -= _commands.ToggleVoiceClipManagerUi;
        _clientState.Login -= OnLogin;
        _googleDrive.StopSync();

        // Stop background harvest before disposing windows so it can't fire DB events
        // into half-disposed UI nodes.
        try { _services.GetService<IDialogHarvestService>()?.Dispose(); } catch { }

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

        if (_kamiToolKitInitialized)
        {
            try { KamiToolKit.KamiToolKitLibrary.Cleanup(); } catch { }
            _kamiToolKitInitialized = false;
        }
    }
}
