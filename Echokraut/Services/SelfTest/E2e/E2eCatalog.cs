using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.E2e;

/// <summary>The E2E stages in run order (plan › Stages; build step 6: preconditions, checks, chat-hook spike, backup, reset, first-time, checks:local, engine, scenarios, remote, checks:remote, cleanup [always], restore [opt-in]).</summary>
public static class E2eCatalog
{
    public static IReadOnlyList<E2eStage> Build(ServiceContainer c, Configuration config,
        IDalamudPluginInterface pluginInterface, IFramework framework, IClientState clientState,
        ICondition condition, IGameGui gameGui, IChatGui chatGui)
    {
        var gameObjects = c.GetService<IGameObjectService>();
        // The same check instances as /ektest and Dalamud's self-test window.
        var checks = c.GetService<ISelfTestService>().Checks;
        var dll = pluginInterface.AssemblyLocation.FullName;
        var reload = new E2eSelfReload(c.GetService<IEchokrauTtsInstanceService>(), c.GetService<IAlltalkInstanceService>(), config, dll,
            windows: c.GetService<IOwnedWindowsService>(), onFramework: a => framework.RunOnFrameworkThread(a));
        SelfTestCheck? Check(string id) => checks.FirstOrDefault(k => k.Id == id);
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
                a => framework.RunOnFrameworkThread(a), Plugin.PluginVersion),
            new ChecksStage("checks:local", checks, [FirstTimeStage.StageId]),
            new EngineStage(c.GetService<IEchokrauTtsInstanceService>(), config,
                new E2eEngineWait(c.GetService<IEchokrauTtsInstanceService>(), c.GetService<IBackendService>(),
                    c.GetService<INpcDataService>()),
                Check("generation"), Check("pipeline"),
                SystemDriveFree),
            Scenarios(c, config, framework, clientState, chatGui, gameObjects),
            // After scenarios: it stops the local engine for good.
            Remote(c, config, framework, clientState, reload, Check),
            // Every automatic check once more, on the remote [M1]: needs only the switch, not every remote part
            // (a remote without game-voice support must not hide the rest).
            new ChecksStage("checks:remote", checks, [RemoteStage.SwitchPart]),
            // Always (stage 8), also after an abort; before restore, which puts the real DB back.
            new CleanupStage(config, c.GetService<IDatabaseService>(), c.GetService<INpcDataService>(),
                c.GetService<IGameVoiceReferenceService>()),
            // Opt-in, always last: puts the real config back through a self-reload (no game restart).
            new RestoreStage(reload, pluginInterface.GetPluginConfigDirectory()),
        };
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
        var db = c.GetService<IDatabaseService>();
        var gameVoice = c.GetService<IGameVoiceReferenceService>();
        var speaker = new E2eSelfTestSpeaker(c.GetService<IVoiceMessageProcessor>(), framework, clientState, db, npcData,
            c.GetService<ILogService>());
        var generation = new E2eGameVoiceGeneration(gameVoice, db, npcData, c.GetService<IVoiceMessageQueue>(),
            c.GetService<IAudioPlaybackService>(), speaker);
        return new RemoteStage(backend, engine, gameVoice, config, reload.StopEnginesAsync,
            new E2eEngineWait(engine, backend, npcData),
            [check("backend"), check("generation"), check("pipeline")], check("game-voice"), generation.RunAsync);
    }

    private static ScenariosStage Scenarios(ServiceContainer c, Configuration config, IFramework framework,
        IClientState clientState, IChatGui chatGui, IGameObjectService gameObjects)
    {
        var queue = c.GetService<IVoiceMessageQueue>();
        var playback = c.GetService<IAudioPlaybackService>();
        var npcData = c.GetService<INpcDataService>();
        var db = c.GetService<IDatabaseService>();
        var speaker = new E2eSelfTestSpeaker(c.GetService<IVoiceMessageProcessor>(), framework, clientState, db, npcData,
            c.GetService<ILogService>());
        return new ScenariosStage(
        [
            new ContinuationScenario(config, c.GetService<IDialogContinuationService>(), playback, speaker),
            new QueueOrderScenario(config, queue, playback, speaker),
            new EpochCancelScenario(config, queue, playback, speaker),
            new MuteScenario(config, npcData, playback, speaker),
            new ChatFcScenario(config, chatGui, framework, gameObjects, queue, playback, npcData, db,
                c.GetService<IAudioFileService>(), speaker),
        ], config);
    }
}
