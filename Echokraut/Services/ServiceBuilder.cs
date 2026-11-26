using Echotools.Logging.Services;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Services.Queue;

namespace Echokraut.Services;

/// <summary>
/// Configures and builds the service container with all plugin services
/// </summary>
public static class ServiceBuilder
{
    public static ServiceContainer BuildServices(
        IPluginLog pluginLog,
        IGameConfig gameConfig,
        IClientState clientState,
        IObjectTable objectTable,
        ICommandManager commandManager,
        IChatGui chatGui,
        ICondition condition,
        Configuration configuration,
        IFramework framework,
        IDataManager dataManager,
        IAddonLifecycle addonLifecycle,
        ISigScanner sigScanner,
        IGameInteropProvider gameInteropProvider,
        IDalamudPluginInterface pluginInterface,
        IPlayerState playerState,
        IUnlockState unlockState,
        IGameGui gameGui)
    {
        var container = new ServiceContainer();

        // Register core services
        container.RegisterFactory<ILogService>(c => new LogService(pluginLog));

        container.RegisterFactory<IDatabaseService>(c => new DatabaseService(
            c.GetService<ILogService>(),
            pluginInterface.GetPluginConfigDirectory(),
            configuration,
            clientState.ClientLanguage));

        container.RegisterFactory<IRemoteUrlService>(c => new RemoteUrlService(
            c.GetService<ILogService>()));

        container.RegisterFactory<ITtsEngineCatalogService>(c => new TtsEngineCatalogService(
            c.GetService<ILogService>(),
            c.GetService<IRemoteUrlService>()));

        container.RegisterFactory<IJsonDataService>(c => new JsonDataService(
            c.GetService<ILogService>(),
            c.GetService<IRemoteUrlService>(),
            clientState.ClientLanguage));

        container.RegisterFactory<ILanguageDetectionService>(c => new LanguageDetectionService(
            configuration,
            clientState,
            c.GetService<ILogService>()));

        container.RegisterFactory<IVolumeService>(c => new VolumeService(
            gameConfig,
            c.GetService<ILogService>(),
            configuration));

        container.RegisterFactory<ILuminaService>(c => new LuminaService(
            c.GetService<ILogService>(),
            clientState,
            dataManager));

        container.RegisterFactory<ILodestoneService>(c => new LodestoneService(
            c.GetService<ILogService>(),
            c.GetService<IDatabaseService>()));

        container.RegisterFactory<IPlayerLodestoneEnricher>(c => new PlayerLodestoneEnricher(
            c.GetService<ILogService>(),
            c.GetService<INpcDataService>(),
            c.GetService<ILodestoneService>(),
            c.GetService<ILuminaService>(),
            objectTable));

        container.RegisterFactory<ICharacterDataService>(c => new CharacterDataService(
            c.GetService<ILogService>(),
            c.GetService<IJsonDataService>(),
            c.GetService<ILuminaService>()));

        container.RegisterFactory<IDialogHarvestService>(c => new DialogHarvestService(
            dataManager,
            c.GetService<IJsonDataService>(),
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IDatabaseService>(),
            c.GetService<IBackendService>(),
            c.GetService<INpcDataService>(),
            c.GetService<IRemoteUrlService>(),
            c.GetService<ILineSubmissionService>()));

        container.RegisterFactory<IVoicePackService>(c => new VoicePackService(
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IRemoteUrlService>(),
            () => clientState.ClientLanguage));

        container.RegisterFactory<IAudioPackageService>(c => new AudioPackageService(
            c.GetService<IDatabaseService>(),
            configuration,
            c.GetService<ILogService>()));

        // Game-voice reference (plan docs/plans/game-voice-reference.md). The EchokrauTTS client is
        // stateless (static HttpClients, reads the config per call), so one instance serves it.
        container.RegisterFactory<IGameVoiceDataSource>(c => new GameVoiceDataSource(
            dataManager,
            c.GetService<ILogService>()));

        container.RegisterFactory<IStoryProgressService>(c => new StoryProgressService(
            framework, unlockState, playerState, dataManager, c.GetService<ILogService>()));

        container.RegisterFactory<IGameVoiceMappingService>(c => new GameVoiceMappingService(
            c.GetService<ILogService>(),
            pluginInterface.GetPluginConfigDirectory(),
            c.GetService<IRemoteUrlService>().Urls.GameVoiceAliasesUrl,
            c.GetService<IRemoteUrlService>().Urls.GameVoiceActorSplitsUrl));

        container.RegisterFactory<IGameVoiceReferenceService>(c => new GameVoiceReferenceService(
            configuration,
            c.GetService<ILogService>(),
            c.GetService<IDatabaseService>(),
            c.GetService<IGameVoiceDataSource>(),
            new Echokraut.Backend.EchokrauTtsBackend(configuration, c.GetService<ILogService>()),
            c.GetService<IGameVoiceMappingService>(),
            c.GetService<IStoryProgressService>(),
            c.GetService<IEchokrauTtsInstanceService>()));

        container.RegisterFactory<IBatchModeService>(c => new BatchModeService(
            c.GetService<IDialogHarvestService>(),
            c.GetService<IVoicePackService>(),
            c.GetService<IGameVoiceReferenceService>()));

        container.RegisterFactory<INpcAttributionRepairService>(c => new NpcAttributionRepairService(
            c.GetService<IDatabaseService>(),
            dataManager,
            c.GetService<IJsonDataService>(),
            c.GetService<ILogService>()));

        container.RegisterFactory<ILipSyncHelper>(c => new LipSyncHelper(
            c.GetService<ILogService>(),
            framework,
            objectTable));

        // Register queue system
        container.RegisterFactory<IVoiceMessageQueue>(c => new VoiceMessageQueue());

        container.RegisterFactory<ILiveGenerationLogger>(c => new LiveGenerationLogger(
            c.GetService<IDatabaseService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ILogService>()));

        container.RegisterFactory<IAudioPlaybackService>(c => new AudioPlaybackService(
            c.GetService<IVoiceMessageQueue>(),
            c.GetService<ILogService>(),
            configuration,
            framework,
            c.GetService<ILipSyncHelper>(),
            c.GetService<IAudioFileService>(),
            c.GetService<ILiveGenerationLogger>()));

        // AlltalkInstance must be registered before BackendService (BackendService depends on it)
        container.RegisterFactory<IAlltalkInstanceService>(c => new AlltalkInstanceService(
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IGoogleDriveSyncService>(),
            c.GetService<IRemoteUrlService>(),
            clientState,
            c.GetService<IVoicePackService>()));

        container.RegisterFactory<ILineSubmissionService>(c => new LineSubmissionService(
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IRemoteUrlService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<IAudioFileService>(),
            c.GetService<IDatabaseService>(),
            pluginInterface.GetPluginConfigDirectory()));

        container.RegisterFactory<IEchokrauTtsInstanceService>(c => new EchokrauTtsInstanceService(
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IRemoteUrlService>(),
            clientState,
            c.GetService<IVoicePackService>(),
            c.GetService<ITtsEngineCatalogService>(),
            Helper.Functional.TestWrapperInstall.ZipPathFor(
                pluginInterface.AssemblyLocation.FullName, pluginInterface.IsDev)));

        container.RegisterFactory<IBackendService>(c => new BackendService(
            c.GetService<IVoiceMessageQueue>(),
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAlltalkInstanceService>(),
            c.GetService<IEchokrauTtsInstanceService>(),
            c.GetService<INpcDataService>(),
            c.GetService<IAudioFileService>(),
            c.GetService<IDatabaseService>(),
            c.GetService<IAudioPlaybackService>(),
            c.GetService<IGameVoiceReferenceService>()));

        container.RegisterFactory<ITtsVoiceSyncService>(c => new TtsVoiceSyncService(
            configuration,
            c.GetService<ILogService>(),
            c.GetService<IBackendService>(),
            c.GetService<IAlltalkInstanceService>(),
            c.GetService<IEchokrauTtsInstanceService>()));

        container.RegisterFactory<IVoiceClipManagerService>(c => new VoiceClipManagerService(
            c.GetService<IDatabaseService>(),
            c.GetService<IBackendService>(),
            c.GetService<IAudioFileService>(),
            c.GetService<IAudioPlaybackService>(),
            c.GetService<INpcDataService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ILogService>(),
            configuration));

        // Register business logic services
        container.RegisterFactory<ITextProcessingService>(c => new TextProcessingService(
            c.GetService<ILogService>(),
            c.GetService<IJsonDataService>()));

        container.RegisterFactory<IGameObjectService>(c => new GameObjectService(
            clientState,
            objectTable,
            c.GetService<ILogService>(),
            c.GetService<ITextProcessingService>()));

        container.RegisterFactory<INpcDataService>(c => new NpcDataService(
            c.GetService<ILogService>(),
            c.GetService<IDatabaseService>(),
            c.GetService<IJsonDataService>()));

        container.RegisterFactory<IGoogleDriveSyncService>(c => new GoogleDriveSyncService(
            c.GetService<ILogService>(),
            configuration));

        container.RegisterFactory<IAudioFileService>(c => new AudioFileService(
            c.GetService<ILogService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<IGoogleDriveSyncService>()));



        // Single shared instance — it IS the cross-cutting dialogue state.
        container.RegisterFactory<IDialogStateService>(_ => new DialogStateService());

        container.RegisterFactory<IVoiceTestService>(c => new VoiceTestService(
            c.GetService<ILogService>(),
            c.GetService<IVolumeService>(),
            c.GetService<IBackendService>(),
            c.GetService<IAudioPlaybackService>(),
            clientState,
            c.GetService<IGameObjectService>(),
            configuration,
            c.GetService<INpcDataService>(),
            c.GetService<IDialogStateService>()));

        container.RegisterFactory<IVoiceMessageProcessor>(c => new VoiceMessageProcessor(
            c.GetService<ILogService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<ICharacterDataService>(),
            c.GetService<ILuminaService>(),
            c.GetService<IVolumeService>(),
            c.GetService<IBackendService>(),
            clientState,
            configuration,
            c.GetService<ILanguageDetectionService>(),
            c.GetService<IJsonDataService>(),
            c.GetService<INpcDataService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<IDatabaseService>(),
            c.GetService<ILodestoneService>(),
            c.GetService<IAudioFileService>(),
            c.GetService<Queue.IVoiceMessageQueue>(),
            c.GetService<IDialogStateService>(),
            c.GetService<ILineSubmissionService>(),
            c.GetService<IDialogContinuationService>()));

        container.RegisterFactory<IDialogSheetSource>(_ => new LuminaDialogSheetSource(dataManager));
        container.RegisterFactory<IDialogContinuationService>(c => new DialogContinuationService(
            c.GetService<IDialogSheetSource>(),
            c.GetService<IGameObjectService>(),
            configuration,
            c.GetService<ILogService>()));

        container.RegisterFactory<IAddonCancelService>(c => new AddonCancelService(
            c.GetService<IAudioPlaybackService>(),
            c.GetService<ILipSyncHelper>(),
            c.GetService<ILogService>()));

        container.RegisterFactory<ICommandService>(c => new CommandService(
            commandManager,
            chatGui,
            c.GetService<ILogService>(),
            configuration));

        container.RegisterFactory<ISoundHelper>(c => new SoundHelper(
            c.GetService<ILogService>(), sigScanner, gameInteropProvider));

        container.RegisterFactory<IEchokrautIpc>(c => new EchokrautIpc(pluginInterface));

        container.RegisterFactory<IChangelogService>(c => new ChangelogService(
            c.GetService<ILogService>(),
            configuration,
            clientState,
            Plugin.PluginVersion));

        container.RegisterFactory<IAddonTalkHelper>(c => new AddonTalkHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            c.GetService<IAudioPlaybackService>(),
            condition,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<ISoundHelper>(),
            c.GetService<IDialogStateService>(),
            framework));

        container.RegisterFactory<IAddonBattleTalkHelper>(c => new AddonBattleTalkHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<ISoundHelper>(),
            c.GetService<IDialogStateService>()));

        container.RegisterFactory<IAddonSelectStringHelper>(c => new AddonSelectStringHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            condition,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<IDialogStateService>()));

        container.RegisterFactory<IAddonCutSceneSelectStringHelper>(c => new AddonCutSceneSelectStringHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<IDialogStateService>()));

        container.RegisterFactory<IAddonBubbleHelper>(c => new AddonBubbleHelper(
            c.GetService<IVoiceMessageProcessor>(),
            condition,
            clientState,
            objectTable,
            sigScanner,
            gameInteropProvider,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<ILuminaService>(),
            c.GetService<ISoundHelper>()));

        container.RegisterFactory<IChatTalkHelper>(c => new ChatTalkHelper(
            c.GetService<IVoiceMessageProcessor>(),
            chatGui,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<ILuminaService>()));

        container.RegisterFactory<SelfTest.ISelfTestService>(c => new SelfTest.SelfTestService(
            SelfTest.SelfTestCatalog.Build(c, configuration, clientState, framework),
            c.GetService<ICommandService>(),
            c.GetService<ILogService>(),
            System.IO.Path.Combine(pluginInterface.GetPluginConfigDirectory(), "selftest"),
            () => clientState.ClientLanguage.ToString(),
            c.GetService<SelfTest.ISelfTestRunGate>()));

        // First-time wizard flow: the window binds to it, the E2E stage first-time drives it.
        container.RegisterFactory<IFirstTimeWizardController>(c => new FirstTimeWizardController(
            configuration,
            c.GetService<IAlltalkInstanceService>(),
            c.GetService<IEchokrauTtsInstanceService>(),
            c.GetService<IBackendService>(),
            c.GetService<IBatchModeService>()));

        // Own native windows: the window manager reports/handles them, the E2E self-reload closes them first.
        container.RegisterFactory<IOwnedWindowsService>(_ => new OwnedWindowsService());

        // One test run at a time: /ektest and the E2E run share the gate.
        container.RegisterFactory<SelfTest.ISelfTestRunGate>(_ => new SelfTest.SelfTestRunGate());

        // Unattended E2E run, dev loads only (plan docs/plans/ingame-e2e.md).
        // One frame monitor for the run service (fed every tick) and the stages that measure with it (plan › B2).
        var e2eFrames = new SelfTest.E2e.E2eFrameMonitor();
        container.RegisterFactory<SelfTest.E2e.IE2eRunService>(c => new SelfTest.E2e.E2eRunService(
            SelfTest.E2e.E2eCatalog.Build(c, configuration, pluginInterface, framework, clientState, condition, gameGui, chatGui, e2eFrames, commandManager, gameConfig),
            new SelfTest.E2e.E2eStateStore(SelfTest.E2e.E2eStateStore.DirectoryFor(pluginInterface.GetPluginConfigDirectory())),
            c.GetService<SelfTest.ISelfTestRunGate>(),
            c.GetService<ICommandService>(),
            c.GetService<ILogService>(),
            pluginInterface.IsDev,
            () => clientState.ClientLanguage.ToString(),
            frames: e2eFrames));

        return container;
    }
}
