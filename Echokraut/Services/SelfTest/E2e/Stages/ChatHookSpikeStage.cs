using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Spike of build step 1 (plan › Chat scenario): does an entry printed with <see cref="IChatGui.Print(XivChatEntry)"/>
/// as FreeCompany pass <see cref="IChatGui.ChatMessage"/> — the hook <c>ChatTalkHelper</c> listens on?
/// The entry goes into the local chat log only, never to the server. Fail means decision D3: the
/// chat scenario hands the entry straight to the chat processing.
/// </summary>
public sealed class ChatHookSpikeStage : E2eStage
{
    public const string StageId = "chat-hook";
    internal static readonly TimeSpan ArrivalWait = TimeSpan.FromSeconds(5);

    private readonly IChatGui _chatGui;
    private readonly IFramework _framework;
    private readonly IGameObjectService _gameObjects;
    private readonly Configuration _config;

    public ChatHookSpikeStage(IChatGui chatGui, IFramework framework, IGameObjectService gameObjects, Configuration config)
    {
        _chatGui = chatGui;
        _framework = framework;
        _gameObjects = gameObjects;
        _config = config;
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [PreconditionsStage.StageId];
    public override TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        // The line must never be voiced: only a hook check, no pipeline. With FC voicing on, Echo (which
        // ChatTalkHelper never voices) goes through the same Print -> ChatMessage path.
        var type = ProbeType(_config);

        // Not "EKTEST …": the watcher greps that prefix, and a chat line must never look like a result.
        var marker = $"Echokraut E2E chat probe {Guid.NewGuid():N}";
        var arrived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(IHandleableChatMessage msg)
        {
            if (msg.Message.TextValue.Contains(marker, StringComparison.Ordinal))
                arrived.TrySetResult($"{msg.LogKind}, sender '{msg.Sender.TextValue}'");
        }

        _chatGui.ChatMessage += Handler;
        try
        {
            await _framework.RunOnFrameworkThread(() => _chatGui.Print(new XivChatEntry
            {
                Type = type,
                Name = _gameObjects.LocalPlayerName ?? "",
                Message = marker,
            })).ConfigureAwait(false);

            var done = await Task.WhenAny(arrived.Task, Task.Delay(ArrivalWait, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return done == arrived.Task
                ? SelfTestResult.Pass($"A printed {type} entry passes ChatMessage ({arrived.Task.Result}).")
                : SelfTestResult.Fail($"A printed {type} entry did not pass ChatMessage within {ArrivalWait.TotalSeconds:0} s - chat-fc needs the D3 fallback.");
        }
        finally
        {
            _chatGui.ChatMessage -= Handler;
        }
    }

    /// <summary>FreeCompany when that is not voiced, else Echo (never voiced).</summary>
    internal static XivChatType ProbeType(Configuration config) =>
        config.Enabled && config.VoiceChat && config.VoiceChatFreeCompany ? XivChatType.Echo : XivChatType.FreeCompany;
}
