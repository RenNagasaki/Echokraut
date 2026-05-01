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
        IDalamudPluginInterface pluginInterface)
    {
        var container = new ServiceContainer();

        // Register core services
        container.RegisterFactory<ILogService>(c => new LogService(pluginLog));

        container.RegisterFactory<IDatabaseService>(c => new DatabaseService(
            c.GetService<ILogService>(),
            pluginInterface.GetPluginConfigDirectory(),
            configuration));

        container.RegisterFactory<IRemoteUrlService>(c => new RemoteUrlService(
            c.GetService<ILogService>()));

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
            c.GetService<INpcDataService>()));

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
            c.GetService<IRemoteUrlService>()));

        container.RegisterFactory<IBackendService>(c => new BackendService(
            c.GetService<IVoiceMessageQueue>(),
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAlltalkInstanceService>(),
            c.GetService<INpcDataService>(),
            c.GetService<IAudioFileService>(),
            c.GetService<IDatabaseService>(),
            c.GetService<IAudioPlaybackService>()));

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



        container.RegisterFactory<IVoiceTestService>(c => new VoiceTestService(
            c.GetService<ILogService>(),
            c.GetService<IVolumeService>(),
            c.GetService<IBackendService>(),
            c.GetService<IAudioPlaybackService>(),
            clientState,
            c.GetService<IGameObjectService>(),
            configuration,
            c.GetService<INpcDataService>()));

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
            c.GetService<ILodestoneService>()));

        container.RegisterFactory<IAddonCancelService>(c => new AddonCancelService(
            c.GetService<IAudioPlaybackService>(),
            c.GetService<ILipSyncHelper>(),
            c.GetService<ILogService>()));

        container.RegisterFactory<ICommandService>(c => new CommandService(
            commandManager,
            chatGui,
            condition,
            c.GetService<ILogService>(),
            c.GetService<ICharacterDataService>(),
            c.GetService<ILuminaService>(),
            configuration,
            c.GetService<IAudioFileService>(),
            c.GetService<IGameObjectService>()));

        container.RegisterFactory<ISoundHelper>(c => new SoundHelper(
            c.GetService<ILogService>(), sigScanner, gameInteropProvider));

        container.RegisterFactory<IEchokrautIpc>(c => new EchokrautIpc(pluginInterface));

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
            c.GetService<ISoundHelper>()));

        container.RegisterFactory<IAddonBattleTalkHelper>(c => new AddonBattleTalkHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>(),
            c.GetService<ISoundHelper>()));

        container.RegisterFactory<IAddonSelectStringHelper>(c => new AddonSelectStringHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            condition,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>()));

        container.RegisterFactory<IAddonCutSceneSelectStringHelper>(c => new AddonCutSceneSelectStringHelper(
            c.GetService<IVoiceMessageProcessor>(),
            addonLifecycle,
            c.GetService<ILogService>(),
            configuration,
            c.GetService<IAddonCancelService>(),
            c.GetService<IGameObjectService>(),
            c.GetService<ITextProcessingService>()));

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

        return container;
    }
}
