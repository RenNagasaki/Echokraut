using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Localization;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;

namespace Echokraut.Services.SelfTest.Checks;

/// <summary>Shared for every check that needs a live TTS engine: None mode ("audio files only") skips them.</summary>
internal static class LiveGeneration
{
    public static SelfTestResult? SkipIfNone(Configuration config) =>
        config.HasLiveGeneration ? null : SelfTestResult.Skip("No live TTS engine (\"audio files only\" mode).");

    /// <summary>The voice a test line is spoken with: the default voice, else the first enabled one.</summary>
    public static EchokrautVoice? PickTestVoice(INpcDataService npcData)
    {
        var voices = npcData.GetEchokrautVoices().Where(v => v.IsEnabled).ToList();
        return voices.FirstOrDefault(v => v.IsDefault) ?? voices.FirstOrDefault();
    }

    public static EKEventId NewEventId(ILogService log, string method)
    {
        var id = log.Start(method, TextSource.VoiceTest);
        return new EKEventId(id.Id, id.TextSource);
    }
}

/// <summary>The active engine answers its health check.</summary>
public sealed class BackendCheck : SelfTestCheck
{
    private readonly Configuration _config;
    private readonly IBackendService _backend;
    private readonly ILogService _log;

    public BackendCheck(Configuration config, IBackendService backend, ILogService log)
    {
        _config = config;
        _backend = backend;
        _log = log;
    }

    public override string Id => "backend";
    public override string Name => Loc.S("TTS backend reachable");

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        if (!_backend.IsBackendAvailable()) return SelfTestResult.Fail($"{_config.BackendSelection} is not configured.");

        _backend.InvalidateReachabilityCache();
        if (!await _backend.IsBackendReachableAsync().ConfigureAwait(false))
            return SelfTestResult.Fail($"{_config.BackendSelection} ({_config.ActiveInstanceType}) does not answer.");

        var eventId = LiveGeneration.NewEventId(_log, nameof(BackendCheck));
        var ready = await _backend.CheckReady(eventId).ConfigureAwait(false);
        _log.End(nameof(BackendCheck), eventId);
        return SelfTestResult.Pass($"{_config.BackendSelection} ({_config.ActiveInstanceType}): {ready}");
    }
}

/// <summary>Voices are mapped from the engine and at least one is enabled.</summary>
public sealed class VoicesCheck : SelfTestCheck
{
    private readonly Configuration _config;
    private readonly INpcDataService _npcData;

    public VoicesCheck(Configuration config, INpcDataService npcData)
    {
        _config = config;
        _npcData = npcData;
    }

    public override string Id => "voices";
    public override string Name => Loc.S("Voices available");

    public override Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return Task.FromResult(skip);
        var voices = _npcData.GetEchokrautVoices();
        var enabled = voices.Count(v => v.IsEnabled);
        var result = enabled == 0
            ? SelfTestResult.Fail($"{voices.Count} voice(s) known, none enabled (voice pack installed? engine connected?).")
            : SelfTestResult.Pass($"{enabled} of {voices.Count} voice(s) enabled, default: {voices.FirstOrDefault(v => v.IsDefault)?.VoiceName ?? "none"}.");
        return Task.FromResult(result);
    }
}

/// <summary>
/// The engine produces audio for a test sentence. Talks to the backend directly (no queue, no
/// playback, nothing stored), so a failure here points at the engine and not at the pipeline.
/// </summary>
public sealed class GenerationCheck : SelfTestCheck
{
    private readonly Configuration _config;
    private readonly IBackendService _backend;
    private readonly INpcDataService _npcData;
    private readonly IClientState _clientState;
    private readonly ILogService _log;

    public GenerationCheck(Configuration config, IBackendService backend, INpcDataService npcData,
        IClientState clientState, ILogService log)
    {
        _config = config;
        _backend = backend;
        _npcData = npcData;
        _clientState = clientState;
        _log = log;
    }

    public override string Id => "generation";
    public override string Name => Loc.S("Speech generation");
    // A cold local engine loads its model on the first request.
    public override TimeSpan Timeout => TimeSpan.FromSeconds(120);

    public override async Task<SelfTestResult> RunAsync(CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        var voice = LiveGeneration.PickTestVoice(_npcData);
        if (voice == null) return SelfTestResult.Fail("No enabled voice to generate with.");

        var eventId = LiveGeneration.NewEventId(_log, nameof(GenerationCheck));
        var text = Constants.TestMessage(_clientState.ClientLanguage);
        var message = new VoiceMessage
        {
            Source = TextSource.VoiceTest,
            Speaker = new NpcMapData(ObjectKind.None) { Name = voice.VoiceName, Voices = _npcData.GetEchokrautVoices(), Voice = voice },
            Text = text,
            OriginalText = text,
            Language = _clientState.ClientLanguage,
            EventId = eventId,
            NoCache = true, // measure the engine, not the wrapper's response cache
        };

        var started = Environment.TickCount64;
        try
        {
            if (!await _backend.GenerateVoice(message).ConfigureAwait(false) || message.Stream == null)
                return SelfTestResult.Fail($"The engine returned no audio for voice {voice.VoiceName} (see log).");

            // The stream may still be filling (streaming engines): the first bytes prove it works.
            var buffer = new byte[16 * 1024];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await message.Stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            var ms = Environment.TickCount64 - started;
            return read == 0
                ? SelfTestResult.Fail($"Voice {voice.VoiceName}: the audio stream was empty.")
                : SelfTestResult.Pass($"Voice {voice.VoiceName}: first {read} bytes after {ms} ms.");
        }
        finally
        {
            message.Stream?.Dispose();
            _log.End(nameof(GenerationCheck), eventId);
        }
    }
}
