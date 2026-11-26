using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest;

/// <summary>The list of self-test checks, in run order. New checks go here.</summary>
public static class SelfTestCatalog
{
    public static IReadOnlyList<SelfTestCheck> Build(ServiceContainer c, Configuration config,
        IClientState clientState, IFramework framework)
    {
        var log = c.GetService<ILogService>();
        var queue = c.GetService<IVoiceMessageQueue>();
        var npcData = c.GetService<INpcDataService>();
        var backend = c.GetService<IBackendService>();

        return new List<SelfTestCheck>
        {
            new PlayerCheck(clientState, framework, c.GetService<IGameObjectService>()),
            new GameDataCheck(clientState, c.GetService<IDialogSheetSource>(), c.GetService<IGameVoiceDataSource>()),
            new DatabaseCheck(c.GetService<IDatabaseService>()),
            new StoryProgressCheck(c.GetService<IStoryProgressService>()),
            new BackendCheck(config, backend, log),
            new VoicesCheck(config, npcData),
            new GenerationCheck(config, backend, npcData, clientState, log),
            new PipelineCheck(config, c.GetService<IVoiceMessageProcessor>(), queue,
                c.GetService<IAudioPlaybackService>(), c.GetService<IDatabaseService>(), npcData, clientState, framework, log),
            new ContinuationCheck(config, c.GetService<IDialogContinuationService>(), clientState),
            new GameVoiceCheck(config, c.GetService<IGameVoiceReferenceService>(), clientState),

            new InteractivePlaybackCheck(queue, "talk", TextSource.AddonTalk,
                "Dialogue (interactive)", "Talk to any NPC and let one line play.",
                () => InteractiveSkip.Talk(config)),
            new InteractivePlaybackCheck(queue, "bubble", TextSource.AddonBubble,
                "Speech bubble (interactive)", "Stand near NPCs that show speech bubbles (e.g. in a city) and wait.",
                () => InteractiveSkip.Bubble(config)),
            new InteractivePlaybackCheck(queue, "chat", TextSource.Chat,
                "Chat (interactive)", "Write a line in /say.",
                () => InteractiveSkip.Chat(config)),
        };
    }
}

/// <summary>Why an interactive check cannot pass in the current configuration (null = it can).</summary>
internal static class InteractiveSkip
{
    public static string? Talk(Configuration config) =>
        !config.Enabled ? "Echokraut is switched off (/ekt)."
        : !config.VoiceDialogue ? "Dialogue voicing is off (/ekttalk)."
        : null;

    public static string? Bubble(Configuration config) =>
        !config.Enabled ? "Echokraut is switched off (/ekt)."
        : !config.VoiceBubble ? "Bubble voicing is off (/ektbubble)."
        : null;

    public static string? Chat(Configuration config) =>
        !config.Enabled ? "Echokraut is switched off (/ekt)."
        : !config.VoiceChat || !config.VoiceChatPlayer || !config.VoiceChatSay
            ? "Needs chat voicing with own messages and /say switched on (Chat tab)."
            : null;
}
