using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Helper.Functional;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Chat through the real pipeline (plan › Chat scenario): an FC line with the own name is printed into
/// the local chat log (<see cref="IChatGui.Print(XivChatEntry)"/>, never sent to the server) and must reach
/// the queue as <see cref="TextSource.Chat"/> and play. What it creates — the own character (if new),
/// the clip row, the WAV — goes into <see cref="E2eState.CreatedIds"/> first and is removed at the end;
/// an id stays listed only when its removal did not happen (crash), for the cleanup stage.
/// </summary>
public sealed class ChatFcScenario : E2eScenario
{
    internal static TimeSpan QueueWait { get; set; } = TimeSpan.FromSeconds(30);
    internal static TimeSpan WavWait { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>The generation row is written right after the WAV; deleting the clip before it would orphan it.</summary>
    internal static TimeSpan GenerationRowDelay { get; set; } = TimeSpan.FromSeconds(2);

    internal static readonly string[] Options =
        [nameof(Configuration.VoiceChat), nameof(Configuration.VoiceChatPlayer), nameof(Configuration.VoiceChatFreeCompany)];

    private readonly Configuration _config;
    private readonly IChatGui _chatGui;
    private readonly IFramework _framework;
    private readonly IGameObjectService _gameObjects;
    private readonly IVoiceMessageQueue _queue;
    private readonly IAudioPlaybackService _playback;
    private readonly INpcDataService _npcData;
    private readonly IDatabaseService _db;
    private readonly IAudioFileService _audioFiles;
    private readonly E2eSelfTestSpeaker _speaker;

    public ChatFcScenario(Configuration config, IChatGui chatGui, IFramework framework, IGameObjectService gameObjects,
        IVoiceMessageQueue queue, IAudioPlaybackService playback, INpcDataService npcData, IDatabaseService db,
        IAudioFileService audioFiles, E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _chatGui = chatGui;
        _framework = framework;
        _gameObjects = gameObjects;
        _queue = queue;
        _playback = playback;
        _npcData = npcData;
        _db = db;
        _audioFiles = audioFiles;
        _speaker = speaker;
    }

    public override string Id => "chat-fc";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(3);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        if (!_config.Enabled) return SelfTestResult.Skip("Echokraut is switched off (/ekt).");
        var name = _gameObjects.LocalPlayerName;
        if (string.IsNullOrEmpty(name)) return SelfTestResult.Fail("Own character name unknown (not logged in?).");

        foreach (var option in Options) E2eOptions.Set(ctx.State, _config, option, true);
        ctx.Save();
        VoiceMessage? message = null;
        string? wav = null;
        var newCharacter = !_npcData.MappedPlayers.Any(p => p.Name == name);
        try
        {
            var text = _speaker.TestText;
            var since = DateTime.UtcNow;
            await _framework.RunOnFrameworkThread(() => _chatGui.Print(new XivChatEntry
            {
                Type = XivChatType.FreeCompany,
                Name = name,
                Message = text,
            })).ConfigureAwait(false);

            // Real FC chat may arrive at the same time: matched by text and source.
            var wanted = SentenceContinuation.Normalize(text);
            var entry = await WaitForEntryAsync(e => e.Message.Source == TextSource.Chat && e.QueuedAt >= since
                && SentenceContinuation.Normalize(e.Message.OriginalText) == wanted, ct).ConfigureAwait(false);
            if (entry == null)
                return SelfTestResult.Fail($"The printed FC line did not reach the queue within {QueueWait.TotalSeconds:0} s (see log).");
            message = entry.Message;
            Record(ctx, message, newCharacter, name);

            if (await PlaybackWatch.WaitPlayedAsync(_queue, entry, "chat line", ct).ConfigureAwait(false) is { } notPlayed)
                return SelfTestResult.Fail(notPlayed);

            var saved = "no WAV (loaded from cache)";
            if (_config.SaveToLocal && !message.LoadedLocally)
            {
                var path = _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
                if (await WaitForFileAsync(path, ct).ConfigureAwait(false))
                {
                    wav = path;
                    ctx.State.CreatedIds.Add($"wav:{path}");
                    ctx.Save();
                    await Task.Delay(GenerationRowDelay, ct).ConfigureAwait(false);
                    saved = "WAV saved";
                }
                else saved = $"no WAV within {WavWait.TotalSeconds:0} s";
            }
            return SelfTestResult.Pass($"Chat line by {name} played, clip {message.VoiceClipId}, {saved}.");
        }
        finally
        {
            if (message != null && ct.IsCancellationRequested) _playback.StopPlaying(message);
            E2eOptions.Restore(ctx.State, _config, Options);
            RemoveTraces(ctx, message, wav, newCharacter, name);
            ctx.Save();
        }
    }

    private static void Record(E2eContext ctx, VoiceMessage message, bool newCharacter, string name)
    {
        if (newCharacter) ctx.State.CreatedIds.Add($"character:{name}");
        if (message.VoiceClipId > 0 && !message.LoadedLocally) ctx.State.CreatedIds.Add($"clip:{message.VoiceClipId}");
        ctx.Save();
    }

    private void RemoveTraces(E2eContext ctx, VoiceMessage? message, string? wav, bool newCharacter, string name)
    {
        if (wav != null)
        {
            try
            {
                File.Delete(wav);
                ctx.State.CreatedIds.Remove($"wav:{wav}");
            }
            catch (IOException) { /* stays listed for the cleanup stage */ }
            catch (UnauthorizedAccessException) { /* same */ }
        }
        if (message == null) return;
        if (message.VoiceClipId > 0 && ctx.State.CreatedIds.Remove($"clip:{message.VoiceClipId}"))
            _db.DeleteVoiceClip(message.VoiceClipId);
        if (newCharacter && ctx.State.CreatedIds.Remove($"character:{name}"))
            SelfTestTraces.RemoveCharacter(_db, _npcData, message.Speaker, name);
    }

    private async Task<VoiceMessageEntry?> WaitForEntryAsync(Func<VoiceMessageEntry, bool> match, CancellationToken ct)
    {
        var until = DateTime.UtcNow + QueueWait;
        while (DateTime.UtcNow < until)
        {
            if (PlaybackWatch.FindEntry(_queue, match) is { } entry) return entry;
            await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
        }
        return null;
    }

    private static async Task<bool> WaitForFileAsync(string path, CancellationToken ct)
    {
        var until = DateTime.UtcNow + WavWait;
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(path)) return true;
            await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
        }
        return File.Exists(path);
    }
}
