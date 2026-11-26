using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>The E2E stages in run order (plan › Stages; build step 6: preconditions, checks, chat-hook spike, backup, reset, first-time, wrapper-mixed (B8), checks:local, engine, wrapper-http (B4), game-voice + game-voice-index [opt-in] (B6), scenarios, ui (B5), remote, checks:remote, cleanup [always], restore [opt-in]).</summary>
public static class E2eCatalog
{
    public static IReadOnlyList<E2eStage> Build(ServiceContainer c, Configuration config,
        IDalamudPluginInterface pluginInterface, IFramework framework, IClientState clientState,
        ICondition condition, IGameGui gameGui, IChatGui chatGui, E2eFrameMonitor frames, ICommandManager commandManager,
        IGameConfig gameConfig)
    {
        var gameSound = new GameConfigSound(gameConfig, a => framework.RunOnFrameworkThread(a));
        var gameObjects = c.GetService<IGameObjectService>();
        // The same check instances as /ektest and Dalamud's self-test window.
        var checks = c.GetService<ISelfTestService>().Checks;
        var dll = pluginInterface.AssemblyLocation.FullName;
        var reload = new E2eSelfReload(c.GetService<IEchokrauTtsInstanceService>(), c.GetService<IAlltalkInstanceService>(), config, dll,
            windows: c.GetService<IOwnedWindowsService>(), onFramework: a => framework.RunOnFrameworkThread(a));
        SelfTestCheck? Check(string id) => checks.FirstOrDefault(k => k.Id == id);
        var health = Health(c, config);
        var gameVoiceLines = GameVoiceLines(c, framework, clientState);
        var gameVoice = new GameVoiceStage(config, c.GetService<IGameVoiceReferenceService>(), c.GetService<IGameVoiceMappingService>(),
            gameVoiceLines, health, EmbeddedAliases, pluginInterface.GetPluginConfigDirectory());
        return new List<E2eStage>
        {
            new PreconditionsStage(new E2eGameProbe(framework, clientState, condition, gameGui, gameObjects),
                config, pluginInterface.IsDev, new E2eRootFileSystem()),
            // Non-destructive, on the real config (build step 1): before the backup on purpose.
            new ChecksStage("checks", checks, [PreconditionsStage.StageId]),
            new ChatHookSpikeStage(chatGui, framework, gameObjects, config),
            new BackupStage(reload),
            new ResetStage(config, c.GetService<IDatabaseService>()),
            new FirstTimeStage(c.GetService<IFirstTimeWizardController>(), config,
                c.GetService<IEchokrauTtsInstanceService>(), c.GetService<IBackendService>(),
                c.GetService<INpcDataService>(), c.GetService<ICommandService>(),
                Check("backend"), new E2eInstallHost(),
                a => framework.RunOnFrameworkThread(a), Plugin.PluginVersion, frames: frames),
            // Only with the request's wrapperVersion (plan › B8): old wrapper → greyed engines → update → unlocked.
            MixedWrapper(c, config, framework, frames),
            new ChecksStage("checks:local", checks, [FirstTimeStage.StageId]),
            new EngineStage(c.GetService<IEchokrauTtsInstanceService>(), config,
                new E2eEngineWait(c.GetService<IEchokrauTtsInstanceService>(), c.GetService<IBackendService>(),
                    c.GetService<INpcDataService>()),
                Check("generation"), Check("pipeline"),
                SystemDriveFree, probes: Probes(c, frames, health), gameVoice: gameVoice.RunOnEngineAsync),
            // On the engine the loop left on (plan › B4): response cache, cancel refcount, VCM regenerate, repeat hit.
            new WrapperHttpStage(config, new EchokrauTtsWrapperHttp(config),
                root => new E2eInstallerWrapperLog(root), c.GetService<INpcDataService>(),
                c.GetService<IBackendService>(), c.GetService<IVoiceClipManagerService>(), c.GetService<IDatabaseService>(),
                c.GetService<ILogService>(), () => clientState.ClientLanguage, new NvidiaSmiGpuProbe()),
            // On the engine the loop left on (plan › B6): first line without index, character field, alias + export.
            gameVoice,
            // Opt-in (only when listed): the batch index, abort/resume, Whisper, voice-change suggestions.
            new GameVoiceIndexStage(config, c.GetService<IGameVoiceReferenceService>(), c.GetService<INpcDataService>(),
                gameVoiceLines, health, pluginInterface.GetPluginConfigDirectory(), frames),
            Scenarios(c, config, framework, clientState, chatGui, gameObjects, reload, health, gameSound),
            // The own windows (plan › B5); after the engine-bound stages: its changelog part reloads the plugin twice.
            Ui(c, config, framework, clientState, commandManager, reload, frames),
            // After scenarios: it stops the local engine for good.
            Remote(c, config, framework, clientState, reload, Check),
            // Every automatic check once more, on the remote [M1]: needs only the switch, not every remote part
            // (a remote without game-voice support must not hide the rest).
            new ChecksStage("checks:remote", checks, [RemoteStage.SwitchPart]),
            // Always (stage 8), also after an abort; before restore, which puts the real DB back.
            new CleanupStage(config, c.GetService<IDatabaseService>(), c.GetService<INpcDataService>(),
                c.GetService<IGameVoiceReferenceService>(), gameSound),
            // Opt-in, always last: puts the real config back through a self-reload (no game restart).
            new RestoreStage(reload, pluginInterface.GetPluginConfigDirectory()),
        };
    }

    /// <summary>Telemetry of the engine loop (plan › B2): frames, nvidia-smi, /health, underruns, install footprint.</summary>
    private static E2eTelemetryProbes Probes(ServiceContainer c, E2eFrameMonitor frames,
        Func<System.Threading.CancellationToken, Task<EchokrauTtsHealthResponse?>> health)
    {
        var playback = c.GetService<IAudioPlaybackService>();
        return new E2eTelemetryProbes(frames, new NvidiaSmiGpuProbe(), health, () => playback.UnderrunCount, new E2eInstallFootprint());
    }

    /// <summary><c>/health</c> of the configured EchokrauTTS instance (telemetry, game-voice expectations).</summary>
    private static Func<System.Threading.CancellationToken, Task<EchokrauTtsHealthResponse?>> Health(ServiceContainer c, Configuration config)
    {
        var backend = new Echokraut.Backend.EchokrauTtsBackend(config, c.GetService<ILogService>());
        var eventId = new EKEventId(0, Echotools.Logging.Enums.TextSource.None);
        return ct => backend.GetHealthAsync(eventId, ct);
    }

    private static E2eGameVoiceGeneration GameVoiceLines(ServiceContainer c, IFramework framework, IClientState clientState)
    {
        var db = c.GetService<IDatabaseService>();
        var npcData = c.GetService<INpcDataService>();
        var speaker = new E2eSelfTestSpeaker(c.GetService<IVoiceMessageProcessor>(), framework, clientState, db, npcData,
            c.GetService<ILogService>());
        return new E2eGameVoiceGeneration(c.GetService<IGameVoiceReferenceService>(), db, npcData, c.GetService<IVoiceMessageQueue>(),
            c.GetService<IAudioPlaybackService>(), speaker);
    }

    /// <summary>The embedded <c>Resources/GameVoiceAliases.json</c>: the format the repo export must match.</summary>
    private static string? EmbeddedAliases()
    {
        using var stream = typeof(E2eCatalog).Assembly.GetManifestResourceStream(GameVoiceMappingService.AliasResource);
        if (stream == null) return null;
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Free bytes on the Windows system drive (uv's package cache), null when unknown.</summary>
    private static long? SystemDriveFree()
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.Environment.SystemDirectory);
            return string.IsNullOrEmpty(root) ? null : new System.IO.DriveInfo(root).AvailableFreeSpace;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static RemoteStage Remote(ServiceContainer c, Configuration config, IFramework framework,
        IClientState clientState, E2eSelfReload reload, Func<string, SelfTestCheck?> check)
    {
        var engine = c.GetService<IEchokrauTtsInstanceService>();
        var backend = c.GetService<IBackendService>();
        var npcData = c.GetService<INpcDataService>();
        var gameVoice = c.GetService<IGameVoiceReferenceService>();
        var generation = GameVoiceLines(c, framework, clientState);
        return new RemoteStage(backend, engine, gameVoice, config, reload.StopEnginesAsync,
            new E2eEngineWait(engine, backend, npcData),
            [check("backend"), check("generation"), check("pipeline")], check("game-voice"), generation.RunAsync);
    }

    private static MixedWrapperStage MixedWrapper(ServiceContainer c, Configuration config, IFramework framework, E2eFrameMonitor frames)
    {
        var engine = c.GetService<IEchokrauTtsInstanceService>();
        return new MixedWrapperStage(engine, config,
            new E2eUiDriver(c.GetService<IOwnedWindowsService>(), a => framework.RunOnFrameworkThread(a)),
            new E2eEngineWait(engine, c.GetService<IBackendService>(), c.GetService<INpcDataService>()),
            root => E2eInstallerLog.ReadLines(E2eInstallerLog.PathFor(root)), frames);
    }

    private static UiStage Ui(ServiceContainer c, Configuration config, IFramework framework, IClientState clientState,
        ICommandManager commandManager, E2eSelfReload reload, E2eFrameMonitor frames)
    {
        Task OnFramework(Action a) => framework.RunOnFrameworkThread(a);
        async Task<bool> Process(string command)
        {
            var handled = false;
            await framework.RunOnFrameworkThread(() => handled = commandManager.ProcessCommand(command)).ConfigureAwait(false);
            return handled;
        }
        var engine = c.GetService<IEchokrauTtsInstanceService>();
        return new UiStage(new E2eUiDriver(c.GetService<IOwnedWindowsService>(), OnFramework), Process,
            () => commandManager.Commands.Keys.ToList(), config, c.GetService<IFirstTimeWizardController>(),
            c.GetService<IChangelogService>(), engine, reload,
            new E2eEngineWait(engine, c.GetService<IBackendService>(), c.GetService<INpcDataService>()),
            OnFramework, () => clientState.ClientLanguage, Plugin.PluginVersion, frames);
    }

    private static ScenariosStage Scenarios(ServiceContainer c, Configuration config, IFramework framework,
        IClientState clientState, IChatGui chatGui, IGameObjectService gameObjects, E2eSelfReload reload,
        Func<System.Threading.CancellationToken, Task<EchokrauTtsHealthResponse?>> health, IE2eGameSound gameSound)
    {
        var queue = c.GetService<IVoiceMessageQueue>();
        var playback = c.GetService<IAudioPlaybackService>();
        var npcData = c.GetService<INpcDataService>();
        var db = c.GetService<IDatabaseService>();
        var backend = c.GetService<IBackendService>();
        var engine = c.GetService<IEchokrauTtsInstanceService>();
        var engineWait = new E2eEngineWait(engine, backend, npcData);
        var speaker = new E2eSelfTestSpeaker(c.GetService<IVoiceMessageProcessor>(), framework, clientState, db, npcData,
            c.GetService<ILogService>());
        var chat = new E2eChatLines(config, chatGui, framework, gameObjects, queue, npcData, db, c.GetService<IAudioFileService>());
        return new ScenariosStage(
        [
            new ContinuationScenario(config, c.GetService<IDialogContinuationService>(), playback, speaker),
            new QueueOrderScenario(config, queue, playback, speaker),
            new EpochCancelScenario(config, queue, playback, speaker),
            new MuteScenario(config, npcData, playback, speaker),
            // B7 (plan › Build plan): the rest of the R5 + section 5-8 items that need no character control.
            new PhoneticsScenario(config, npcData, playback, speaker),
            new VoiceReassignScenario(config, backend, npcData, playback, speaker),
            new ReloadVoicesScenario(config, backend, npcData),
            // Reloads the plugin in the middle: the stage restarts and keeps the parts above.
            new NarratorScenario(config, npcData, playback, speaker, reload, engineWait),
            new ChatFcScenario(config, chat, queue, playback, speaker),
            new ChatChannelsScenario(config, chat, playback, speaker),
            new LocalSaveScenario(config, chat, queue, playback, speaker),
            new VolumeScenario(config, gameSound, queue, playback, speaker),
            new BubbleScenario(config, queue),
            new HarvestScenario(c.GetService<IDialogHarvestService>(), db, () => clientState.ClientLanguage),
            // Last: it stops the engine (and starts it again for the stages after).
            new AllTalkOffScenario(config, c.GetService<ITtsVoiceSyncService>(), engine, engineWait, reload.Ports,
                E2eSelfReload.IsPortOpenAsync, new NvidiaSmiGpuProbe(), health),
        ], config);
    }
}
