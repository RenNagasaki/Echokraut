using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// One sub-scenario of the stage <c>scenarios</c> (plan › Stages row 6). Reported on its own as
/// <c>scenarios:&lt;Id&gt;</c>. A scenario restores the options it touched and removes what it created
/// itself; the stage restores any option left over as a safety net.
/// </summary>
public abstract class E2eScenario
{
    public abstract string Id { get; }
    public virtual TimeSpan Timeout => TimeSpan.FromMinutes(3);
    public abstract Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct);
}

/// <summary>
/// Sends a line of our own through the real pipeline as self-test speech (never saved, never reported)
/// with the throwaway speaker of the pipeline check, started on the framework thread like the addon
/// helpers do (the pipeline reads the object table).
/// </summary>
public sealed class E2eSelfTestSpeaker
{
    private readonly IVoiceMessageProcessor _processor;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IDatabaseService _db;
    private readonly INpcDataService _npcData;
    private readonly ILogService _log;

    public E2eSelfTestSpeaker(IVoiceMessageProcessor processor, IFramework framework, IClientState clientState,
        IDatabaseService db, INpcDataService npcData, ILogService log)
    {
        _processor = processor;
        _framework = framework;
        _clientState = clientState;
        _db = db;
        _npcData = npcData;
        _log = log;
    }

    public ClientLanguage Language => _clientState.ClientLanguage;

    /// <summary>The localized test sentence of the client language.</summary>
    public string TestText => DataClasses.Constants.TestMessage(Language);

    /// <param name="source">VoiceTest for plain lines; AddonTalk for the Talk-only rules (continuation, NPC mute).</param>
    /// <param name="speakerName">Null = the throwaway speaker; a real NPC name only for the game-voice part of <c>remote</c>.</param>
    public async Task<SelfTestSpeech> SendAsync(TextSource source, string text, string? speakerName = null)
    {
        var started = _log.Start(nameof(E2eSelfTestSpeaker), source);
        var eventId = new EKEventId(started.Id, started.TextSource);
        return await _framework.RunOnFrameworkThread(
            () => _processor.ProcessSelfTestSpeechAsync(eventId, speakerName ?? PipelineCheck.SpeakerName, text)).ConfigureAwait(false);
    }

    /// <summary>Deletes the throwaway character and its clip rows (never a real character).</summary>
    public void RemoveTraces(SelfTestSpeech? speech) =>
        SelfTestTraces.RemoveCharacter(_db, _npcData, speech?.Speaker, PipelineCheck.SpeakerName);
}

internal static class SelfTestSpeechExtensions
{
    /// <summary>Why a line was not queued, for a fail detail: the exception, else a rule (logged by the pipeline).</summary>
    public static string WhyNotQueued(this SelfTestSpeech speech) => speech.Error ?? "stopped by a rule, see log";
}
