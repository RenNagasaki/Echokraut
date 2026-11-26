using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.Checks;

/// <summary>Option "join split sentences": the index over the game's dialogue sheets builds and is not empty.</summary>
public sealed class ContinuationCheck : SelfTestCheck
{
    private readonly Configuration _config;
    private readonly IDialogContinuationService _continuation;
    private readonly IClientState _clientState;

    public ContinuationCheck(Configuration config, IDialogContinuationService continuation, IClientState clientState)
    {
        _config = config;
        _continuation = continuation;
        _clientState = clientState;
    }

    public override string Id => "sentence-continuation";
    public override string Name => Loc.S("Sentence continuation index");
    public override TimeSpan Timeout => TimeSpan.FromSeconds(90);

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        // Off: building it would only cost memory the user did not ask for.
        if (!_config.JoinSplitSentences) return SelfTestResult.Skip("Option \"join split sentences\" is off.");
        var language = _clientState.ClientLanguage;
        await _continuation.Prepare(language).WaitAsync(ct).ConfigureAwait(false);
        return _continuation.JoinableBoxCount(language) switch
        {
            null => SelfTestResult.Fail("Index was not built."),
            0 => SelfTestResult.Fail("Index is empty (dialogue sheets unreadable, see log)."),
            var n => SelfTestResult.Pass($"{n} box(es) end mid-sentence and can be joined ({language})."),
        };
    }
}

/// <summary>
/// Game-voice reference (opt-in only): the server supports inline references and one voiced clip
/// of the client language decodes in memory. Without the opt-in no game audio is touched.
/// </summary>
public sealed class GameVoiceCheck : SelfTestCheck
{
    private readonly Configuration _config;
    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly IClientState _clientState;

    public GameVoiceCheck(Configuration config, IGameVoiceReferenceService gameVoice, IClientState clientState)
    {
        _config = config;
        _gameVoice = gameVoice;
        _clientState = clientState;
    }

    public override string Id => "game-voice";
    public override string Name => Loc.S("Game-voice reference");
    // Building the text index scans every cutscene sheet.
    public override TimeSpan Timeout => TimeSpan.FromSeconds(120);

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        if (!_config.GameVoiceReferenceEnabled) return SelfTestResult.Skip("Not enabled (opt-in in the Backend tab).");
        if (_config.BackendSelection != TTSBackends.EchokrauTTS) return SelfTestResult.Skip("Only with EchokrauTTS.");

        var availability = await _gameVoice.RefreshAvailabilityAsync(new EKEventId(0, TextSource.None), ct).ConfigureAwait(false);
        if (availability != GameVoiceAvailability.Available)
            return SelfTestResult.Fail($"Availability {availability} (server without inline_reference or not reachable).");

        var check = await _gameVoice.SelfCheckAsync(_clientState.ClientLanguage, ct).ConfigureAwait(false);
        if (check.Lines == 0) return SelfTestResult.Fail("The text index has no voiced lines.");
        if (check.DecodedClip == null)
            return SelfTestResult.Fail($"{check.Lines} voiced lines indexed, but none of the newest {Services.GameVoiceReferenceService.SelfCheckDecodeAttempts} clips decodes.");
        return SelfTestResult.Pass($"{check.Tokens} speaker tokens, {check.Lines} voiced lines, clip {check.DecodedClip} decodes ({check.DecodedSeconds:0.0} s).");
    }
}
