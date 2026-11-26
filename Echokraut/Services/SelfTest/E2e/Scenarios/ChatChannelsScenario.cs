using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Echokraut.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Scenarios;

/// <summary>
/// Chat per channel (test plan section 5): for every channel a line of the own character is printed with the
/// channel's option on (it must be queued) and off (it must not be queued within <see cref="AbsentWait"/>).
/// The lines are cancelled at once — playing is covered by <c>chat-fc</c>. "Only nearby players audible" needs
/// real players and stays manual.
/// </summary>
public sealed class ChatChannelsScenario : E2eScenario
{
    internal static TimeSpan AbsentWait { get; set; } = TimeSpan.FromSeconds(4);

    internal static readonly IReadOnlyList<(XivChatType Type, string Option)> Channels =
    [
        (XivChatType.Say, nameof(Configuration.VoiceChatSay)),
        (XivChatType.Shout, nameof(Configuration.VoiceChatShout)),
        (XivChatType.Yell, nameof(Configuration.VoiceChatYell)),
        (XivChatType.TellIncoming, nameof(Configuration.VoiceChatTell)),
        (XivChatType.Party, nameof(Configuration.VoiceChatParty)),
        (XivChatType.Alliance, nameof(Configuration.VoiceChatAlliance)),
        (XivChatType.Ls1, nameof(Configuration.VoiceChatLinkshell)),
        (XivChatType.CrossLinkShell1, nameof(Configuration.VoiceChatCrossLinkshell)),
        (XivChatType.FreeCompany, nameof(Configuration.VoiceChatFreeCompany)),
        (XivChatType.NoviceNetwork, nameof(Configuration.VoiceChatNoviceNetwork)),
    ];

    private readonly Configuration _config;
    private readonly E2eChatLines _lines;
    private readonly IAudioPlaybackService _playback;
    private readonly E2eSelfTestSpeaker _speaker;

    public ChatChannelsScenario(Configuration config, E2eChatLines lines, IAudioPlaybackService playback, E2eSelfTestSpeaker speaker)
    {
        _config = config;
        _lines = lines;
        _playback = playback;
        _speaker = speaker;
    }

    public override string Id => "chat-channels";
    public override TimeSpan Timeout => TimeSpan.FromMinutes(1) + Channels.Count * (E2eChatLines.QueueWait + AbsentWait);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (_lines.Unusable() is { } unusable) return unusable;
        var name = _lines.OwnName!;
        var options = new List<string> { nameof(Configuration.VoiceChat), nameof(Configuration.VoiceChatPlayer) };
        foreach (var (_, option) in Channels) options.Add(option);
        E2eOptions.Set(ctx.State, _config, nameof(Configuration.VoiceChat), true);
        E2eOptions.Set(ctx.State, _config, nameof(Configuration.VoiceChatPlayer), true);
        foreach (var option in options[2..]) E2eOptions.Set(ctx.State, _config, option, false);
        ctx.Save();

        var newCharacter = _lines.IsNewCharacter(name);
        var created = new List<VoiceMessage>();
        var problems = new List<string>();
        try
        {
            foreach (var (type, option) in Channels)
            {
                ct.ThrowIfCancellationRequested();
                var text = $"{_speaker.TestText} {type}";
                E2eOptions.Set(ctx.State, _config, option, true);
                var since = DateTime.UtcNow;
                await _lines.PrintAsync(type, name, text).ConfigureAwait(false);
                var entry = await _lines.WaitForEntryAsync(text, since, E2eChatLines.QueueWait, ct).ConfigureAwait(false);
                _playback.ClearQueue(TextSource.Chat);
                if (entry == null) problems.Add($"{type} on: not queued");
                else
                {
                    created.Add(entry.Message);
                    _lines.Record(ctx, entry.Message, newCharacter && created.Count == 1, name);
                }

                E2eOptions.Set(ctx.State, _config, option, false);
                since = DateTime.UtcNow;
                await _lines.PrintAsync(type, name, text).ConfigureAwait(false);
                var absent = await _lines.WaitForEntryAsync(text, since, AbsentWait, ct).ConfigureAwait(false);
                _playback.ClearQueue(TextSource.Chat);
                if (absent != null)
                {
                    created.Add(absent.Message);
                    _lines.Record(ctx, absent.Message, false, name);
                    problems.Add($"{type} off: queued anyway");
                }
            }
        }
        finally
        {
            _playback.ClearQueue(TextSource.Chat);
            E2eOptions.Restore(ctx.State, _config, options);
            for (var i = created.Count - 1; i >= 0; i--)
                _lines.RemoveTraces(ctx, created[i], null, newCharacter && i == 0, name);
            ctx.Save();
        }

        return problems.Count == 0
            ? SelfTestResult.Pass($"{Channels.Count} channels: each queued with its option on and not queued with it off.")
            : SelfTestResult.Fail(string.Join("; ", problems) + ".");
    }
}
