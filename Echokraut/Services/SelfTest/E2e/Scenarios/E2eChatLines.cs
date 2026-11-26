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
/// Chat lines of the own character printed into the local chat log (<see cref="IChatGui.Print(XivChatEntry)"/>,
/// never sent to the server) and followed into the queue — shared by <c>chat-fc</c>, <c>chat-channels</c> and
/// <c>local-save</c>. These are real lines (not self-test speech): they create the own character, a clip row
/// and, with local saving, a WAV. <see cref="Record"/> lists them in <see cref="E2eState.CreatedIds"/> before
/// anything else happens, <see cref="RemoveTraces"/> removes them again.
/// </summary>
public sealed class E2eChatLines
{
    internal static TimeSpan QueueWait { get; set; } = TimeSpan.FromSeconds(30);
    internal static TimeSpan WavWait { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>The generation row is written right after the WAV; deleting the clip before it would orphan it.</summary>
    internal static TimeSpan GenerationRowDelay { get; set; } = TimeSpan.FromSeconds(2);

    private readonly Configuration _config;
    private readonly IChatGui _chatGui;
    private readonly IFramework _framework;
    private readonly IGameObjectService _gameObjects;
    private readonly IVoiceMessageQueue _queue;
    private readonly INpcDataService _npcData;
    private readonly IDatabaseService _db;
    private readonly IAudioFileService _audioFiles;

    public E2eChatLines(Configuration config, IChatGui chatGui, IFramework framework, IGameObjectService gameObjects,
        IVoiceMessageQueue queue, INpcDataService npcData, IDatabaseService db, IAudioFileService audioFiles)
    {
        _config = config;
        _chatGui = chatGui;
        _framework = framework;
        _gameObjects = gameObjects;
        _queue = queue;
        _npcData = npcData;
        _db = db;
        _audioFiles = audioFiles;
    }

    public string? OwnName => _gameObjects.LocalPlayerName;

    public bool IsNewCharacter(string name) => !_npcData.MappedPlayers.Any(p => p.Name == name);

    /// <summary>Why chat cannot be tested here, else null.</summary>
    public SelfTestResult? Unusable()
    {
        if (LiveGeneration.SkipIfNone(_config) is { } skip) return skip;
        if (!_config.Enabled) return SelfTestResult.Skip("Echokraut is switched off (/ekt).");
        return string.IsNullOrEmpty(OwnName) ? SelfTestResult.Fail("Own character name unknown (not logged in?).") : null;
    }

    public Task PrintAsync(XivChatType type, string name, string text) =>
        _framework.RunOnFrameworkThread(() => _chatGui.Print(new XivChatEntry { Type = type, Name = name, Message = text }));

    /// <summary>The chat entry with <paramref name="text"/> queued at or after <paramref name="since"/> (real chat may arrive at the same time).</summary>
    public VoiceMessageEntry? Find(string text, DateTime since)
    {
        var wanted = SentenceContinuation.Normalize(text);
        return PlaybackWatch.FindEntry(_queue, e => e.Message.Source == TextSource.Chat && e.QueuedAt >= since
            && SentenceContinuation.Normalize(e.Message.OriginalText) == wanted);
    }

    public async Task<VoiceMessageEntry?> WaitForEntryAsync(string text, DateTime since, TimeSpan wait, CancellationToken ct)
    {
        var until = DateTime.UtcNow + wait;
        while (DateTime.UtcNow < until)
        {
            if (Find(text, since) is { } entry) return entry;
            await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
        }
        return Find(text, since);
    }

    /// <summary>Lists the character (if new) and the clip row before anything can go wrong.</summary>
    public void Record(E2eContext ctx, VoiceMessage message, bool newCharacter, string name)
    {
        if (newCharacter) AddOnce(ctx, $"character:{name}");
        if (message.VoiceClipId > 0 && !message.LoadedLocally) AddOnce(ctx, $"clip:{message.VoiceClipId}");
        ctx.Save();
    }

    private static void AddOnce(E2eContext ctx, string id)
    {
        if (!ctx.State.CreatedIds.Contains(id)) ctx.State.CreatedIds.Add(id);
    }

    /// <summary>The WAV path when local saving wrote one for <paramref name="message"/> within <see cref="WavWait"/>, listed as created.</summary>
    public async Task<string?> WaitForWavAsync(E2eContext ctx, VoiceMessage message, CancellationToken ct)
    {
        if (!_config.SaveToLocal || message.LoadedLocally) return null;
        var path = _audioFiles.GetLocalAudioPath(_config.LocalSaveLocation, message);
        var until = DateTime.UtcNow + WavWait;
        while (!File.Exists(path) && DateTime.UtcNow < until)
            await Task.Delay(PlaybackWatch.PollInterval, ct).ConfigureAwait(false);
        if (!File.Exists(path)) return null;
        ctx.State.CreatedIds.Add($"wav:{path}");
        ctx.Save();
        await Task.Delay(GenerationRowDelay, ct).ConfigureAwait(false);
        return path;
    }

    /// <summary>WAV, clip row, then the character (only when this scenario created it); an id whose removal failed stays listed.</summary>
    public void RemoveTraces(E2eContext ctx, VoiceMessage? message, string? wav, bool newCharacter, string name)
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
}
