using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echotools.Logging.Enums;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>The sub-scenarios of the stage <c>scenarios</c>, against a real queue and a fake player.</summary>
public sealed class E2eScenariosTests : IDisposable
{
    private const string PlayerName = "Tataru Taru";

    private Configuration _config = LiveConfig();
    private readonly VoiceMessageQueue _queue = new();
    private readonly Mock<IVoiceMessageProcessor> _processor = new();
    private readonly Mock<IAudioPlaybackService> _playback = new();
    private readonly Mock<IFramework> _framework = new();
    private readonly Mock<IClientState> _clientState = new();
    private readonly Mock<IDatabaseService> _db = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly Mock<IDialogContinuationService> _continuation = new();
    private readonly Mock<IChatGui> _chatGui = new();
    private readonly Mock<IGameObjectService> _gameObjects = new();
    private readonly Mock<IAudioFileService> _audioFiles = new();
    private readonly CancellationTokenSource _playerCts = new();
    private readonly E2eState _state = E2eStateStoreTests.State();
    private readonly NpcMapData _npc = new(ObjectKind.None)
    {
        Name = PipelineCheck.SpeakerName, Gender = Genders.Male, Race = NpcRaces.Hyur, Language = ClientLanguage.English, voice = "Narrator",
    };
    private Func<EKEventId, string, SelfTestSpeech> _process;

    public E2eScenariosTests()
    {
        PlaybackWatch.PollInterval = TimeSpan.FromMilliseconds(1);
        EpochCancelScenario.SilenceWatch = TimeSpan.FromMilliseconds(50);
        ChatFcScenario.QueueWait = TimeSpan.FromMilliseconds(200);
        ChatFcScenario.WavWait = TimeSpan.FromMilliseconds(200);
        ChatFcScenario.GenerationRowDelay = TimeSpan.Zero;

        _clientState.SetupGet(c => c.ClientLanguage).Returns(ClientLanguage.English);
        _framework.Setup(f => f.RunOnFrameworkThread(It.IsAny<Func<Task<SelfTestSpeech>>>()))
            .Returns((Func<Task<SelfTestSpeech>> func) => func());
        _framework.Setup(f => f.RunOnFrameworkThread(It.IsAny<Action>()))
            .Returns((Action a) => { a(); return Task.CompletedTask; });
        _playback.Setup(p => p.ClearQueue(It.IsAny<TextSource>())).Callback((TextSource s) => _queue.CancelBySource(s));
        _gameObjects.SetupGet(g => g.LocalPlayerName).Returns(PlayerName);
        _npcData.SetupGet(n => n.MappedPlayers).Returns(new List<NpcMapData>());
        _process = (e, t) => new SelfTestSpeech(Enqueue(e.TextSource, t, _npc), _npc);
        _processor.Setup(p => p.ProcessSelfTestSpeechAsync(It.IsAny<EKEventId>(), PipelineCheck.SpeakerName, It.IsAny<string>()))
            .Returns((EKEventId e, string _, string t) => Task.FromResult(_process(e, t)));
    }

    public void Dispose()
    {
        _playerCts.Cancel();
        _queue.Dispose();
    }

    private E2eSelfTestSpeaker Speaker() =>
        new(_processor.Object, _framework.Object, _clientState.Object, _db.Object, _npcData.Object, Log().Object);

    private E2eContext Ctx() => new(_state, () => { }, _ => { });

    private VoiceMessage Enqueue(TextSource source, string text, NpcMapData speaker, int clipId = 0)
    {
        var message = new VoiceMessage
        {
            Source = source, Text = text, OriginalText = text, Speaker = speaker, CancelEpoch = _queue.GetEpoch(source),
            VoiceClipId = clipId,
        };
        _queue.Enqueue(message);
        return message;
    }

    /// <summary>Plays the queue in order like the backend + playback loop: generate, ready, play a few ms, complete.</summary>
    private void StartPlayer()
    {
        var ct = _playerCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (_queue.TryDequeuePendingGeneration(out var pending) && pending != null)
                {
                    _queue.MarkAsGenerating(pending.Id);
                    _queue.MarkAsReadyToPlay(pending.Id);
                }
                if (_queue.TryDequeueReadyToPlay(out var ready) && ready != null && !ready.IsTerminal && !_queue.IsObsolete(ready.Message))
                {
                    _queue.MarkAsPlaying(ready.Id);
                    await Task.Delay(20, CancellationToken.None);
                    _queue.MarkAsCompleted(ready.Id);
                }
                await Task.Delay(1, CancellationToken.None);
            }
        }, CancellationToken.None);
    }

    // ── continuation ────────────────────────────────────────────────────────────────────────────

    // The head carries a roman numeral: the pipeline speaks it as a word (measured in the game, run
    // 20261121-235532), so the judge must compare spoken text with spoken text.
    private static readonly ContinuationPair Pair = new("We should leave with Ezel", "II. before nightfall. Then we rest.", "II. before nightfall.");

    private ContinuationScenario Continuation() => new(_config, _continuation.Object, _playback.Object, Speaker());

    /// <summary>Cleaning like the pipeline: roman numerals become words.</summary>
    private static string Clean(string text) => text.Replace("II.", "the second");

    /// <summary>The continuation service the way it works: with the option on, box 1 gets the head, box 2 loses it.</summary>
    private void JoinsLikeTheService(bool join = true, bool strip = true)
    {
        _continuation.Setup(c => c.Prepare(ClientLanguage.English)).Returns(Task.CompletedTask);
        _continuation.Setup(c => c.SamplePair(ClientLanguage.English)).Returns(Pair);
        _process = (e, t) =>
        {
            Assert.Equal(TextSource.AddonTalk, e.TextSource);
            var on = _config.JoinSplitSentences;
            var spoken = !on ? t
                : t == Pair.First ? (join ? t + " " + Pair.Head : t)
                : strip ? "Then we rest." : t;
            var m = Enqueue(e.TextSource, Clean(spoken), _npc);
            m.SentenceJoined = on && join && t == Pair.First;
            return new SelfTestSpeech(m, _npc);
        };
    }

    [Fact]
    public async Task Continuation_Box1JoinedBox2Stripped_Passes_AndRestoresTheOption()
    {
        JoinsLikeTheService();

        var r = await Continuation().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.False(_config.JoinSplitSentences);
        Assert.Empty(_state.TouchedOptions);
        _playback.Verify(p => p.ClearQueue(TextSource.AddonTalk), Times.AtLeastOnce);
        _npcData.Verify(n => n.RemoveCharacter(_npc), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Continuation_NothingJoined_Fails()
    {
        JoinsLikeTheService(join: false);
        var r = await Continuation().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("Box 1", r.Detail);
    }

    [Fact]
    public async Task Continuation_Box2NotStripped_Fails()
    {
        JoinsLikeTheService(strip: false);
        var r = await Continuation().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("Box 2", r.Detail);
        Assert.False(_config.JoinSplitSentences);
    }

    [Fact]
    public async Task Continuation_NoPairInTheIndex_Fails()
    {
        _continuation.Setup(c => c.Prepare(ClientLanguage.English)).Returns(Task.CompletedTask);
        var r = await Continuation().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("no pair", r.Detail);
        Assert.False(_config.JoinSplitSentences);
    }

    [Fact]
    public void Continuation_BaselineNotQueued_Fails()
    {
        var queued = new SelfTestSpeech(new VoiceMessage { Text = Pair.First }, _npc);
        var r = ContinuationScenario.Judge(Pair, new SelfTestSpeech(null, _npc, "boom"), queued, queued, queued);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("option off", r.Detail);
    }

    // ── queue-order ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueueOrder_PlaysInSendOrder_Passes()
    {
        StartPlayer();
        var r = await new QueueOrderScenario(_config, _queue, _playback.Object, Speaker())
            .RunAsync(Ctx(), new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("1,2,3", r.Detail);
    }

    [Fact]
    public async Task QueueOrder_ALineIsCancelled_Fails()
    {
        StartPlayer();
        var sent = 0;
        _process = (e, t) =>
        {
            var m = Enqueue(e.TextSource, t, _npc);
            if (++sent == 2) _queue.MarkAsCancelled(PlaybackWatch.FindEntry(_queue, x => ReferenceEquals(x.Message, m))!.Id);
            return new SelfTestSpeech(m, _npc);
        };
        var r = await new QueueOrderScenario(_config, _queue, _playback.Object, Speaker())
            .RunAsync(Ctx(), new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("Line 2 ended Cancelled", r.Detail);
    }

    [Fact]
    public async Task QueueOrder_ALineIsNotQueued_Fails()
    {
        _process = (_, _) => new SelfTestSpeech(null, _npc);
        var r = await new QueueOrderScenario(_config, _queue, _playback.Object, Speaker()).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("Line 1 was not queued", r.Detail);
    }

    [Fact]
    public async Task QueueOrder_NoneMode_IsSkipped()
    {
        _config = NoneConfig();
        var r = await new QueueOrderScenario(_config, _queue, _playback.Object, Speaker()).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
    }

    // ── epoch-cancel ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EpochCancel_QueuedLineCancelled_IsObsoleteAndSilent_Passes()
    {
        var r = await new EpochCancelScenario(_config, _queue, _playback.Object, Speaker()).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("epoch 0 -> 1", r.Detail);
        Assert.Contains("ended Cancelled", r.Detail);
    }

    [Fact]
    public async Task EpochCancel_CancelDoesNotBumpTheEpoch_Fails()
    {
        _playback.Setup(p => p.ClearQueue(It.IsAny<TextSource>()));
        var r = await new EpochCancelScenario(_config, _queue, _playback.Object, Speaker()).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("Not obsolete", r.Detail);
    }

    [Fact]
    public async Task EpochCancel_NoneMode_IsSkipped()
    {
        _config = NoneConfig();
        var r = await new EpochCancelScenario(_config, _queue, _playback.Object, Speaker()).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
    }

    // ── mute ────────────────────────────────────────────────────────────────────────────────────

    private MuteScenario Mute() => new(_config, _npcData.Object, _playback.Object, Speaker());

    [Fact]
    public async Task Mute_MutedNotQueued_UnmutedQueuedAgain_Passes()
    {
        _process = (e, t) =>
        {
            Assert.Equal(TextSource.AddonTalk, e.TextSource);
            return new SelfTestSpeech(_npc.IsEnabled ? Enqueue(e.TextSource, t, _npc) : null, _npc);
        };

        var r = await Mute().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.True(_npc.IsEnabled);
        _npcData.Verify(n => n.SaveCharacter(_npc), Times.Exactly(2));
        _npcData.Verify(n => n.RemoveCharacter(_npc), Times.Once);
    }

    [Fact]
    public async Task Mute_MutedLineStillQueued_Fails_AndUnmutesAgain()
    {
        var r = await Mute().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("muted speaker was queued", r.Detail);
        Assert.True(_npc.IsEnabled);
    }

    [Fact]
    public async Task Mute_NoneMode_IsSkipped()
    {
        _config = NoneConfig();
        var r = await Mute().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
    }

    // ── chat-fc ─────────────────────────────────────────────────────────────────────────────────

    private readonly NpcMapData _player = new(ObjectKind.Pc) { Name = PlayerName, Language = ClientLanguage.English };

    private ChatFcScenario ChatFc() => new(_config, _chatGui.Object, _framework.Object, _gameObjects.Object, _queue,
        _playback.Object, _npcData.Object, _db.Object, _audioFiles.Object, Speaker());

    /// <summary>Like ChatTalkHelper: a printed FC line from the own name is voiced when the options are on.</summary>
    private void ChatIsVoiced()
    {
        _chatGui.Setup(c => c.Print(It.IsAny<XivChatEntry>()))
            .Callback((XivChatEntry e) =>
            {
                Assert.Equal(XivChatType.FreeCompany, e.Type);
                Assert.Equal(PlayerName, e.Name.TextValue);
                if (_config.VoiceChat && _config.VoiceChatPlayer && _config.VoiceChatFreeCompany)
                    Enqueue(TextSource.Chat, e.Message.TextValue, _player, clipId: 9);
            });
    }

    [Fact]
    public async Task ChatFc_LinePlays_Passes_RemovesItsTraces_AndRestoresTheOptions()
    {
        _config.SaveToLocal = false;
        ChatIsVoiced();
        StartPlayer();
        _db.Setup(d => d.FindCharacter(PlayerName, _player.Gender, _player.Race, (int)ClientLanguage.English))
            .Returns(new CharacterEntity { Id = 3 });
        _db.Setup(d => d.GetVoiceClipsForCharacter(3, It.IsAny<int>(), It.IsAny<int>())).Returns(new List<VoiceClipEntity>());

        var r = await ChatFc().RunAsync(Ctx(), new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("clip 9", r.Detail);
        _db.Verify(d => d.DeleteVoiceClip(9), Times.Once);
        _npcData.Verify(n => n.RemoveCharacter(_player), Times.Once);
        Assert.Empty(_state.CreatedIds);
        Assert.Empty(_state.TouchedOptions);
        Assert.False(_config.VoiceChat);
        Assert.False(_config.VoiceChatFreeCompany);
    }

    [Fact]
    public async Task ChatFc_WavIsRecordedAndDeleted()
    {
        var wav = Path.Combine(Path.GetTempPath(), $"ek-e2e-chat-{Guid.NewGuid():N}.wav");
        File.WriteAllText(wav, "x");
        _audioFiles.Setup(a => a.GetLocalAudioPath(It.IsAny<string>(), It.IsAny<VoiceMessage>())).Returns(wav);
        ChatIsVoiced();
        StartPlayer();

        var r = await ChatFc().RunAsync(Ctx(), new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("WAV saved", r.Detail);
        Assert.False(File.Exists(wav));
        Assert.Empty(_state.CreatedIds);
    }

    [Fact]
    public async Task ChatFc_OwnCharacterExistedBefore_IsKept()
    {
        _config.SaveToLocal = false;
        _npcData.SetupGet(n => n.MappedPlayers).Returns(new List<NpcMapData> { _player });
        ChatIsVoiced();
        StartPlayer();

        var r = await ChatFc().RunAsync(Ctx(), new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        _npcData.Verify(n => n.RemoveCharacter(It.IsAny<NpcMapData>()), Times.Never);
        _db.Verify(d => d.DeleteVoiceClip(9), Times.Once);
    }

    [Fact]
    public async Task ChatFc_LineNeverReachesTheQueue_Fails_AndRestoresTheOptions()
    {
        var r = await ChatFc().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("did not reach the queue", r.Detail);
        Assert.False(_config.VoiceChat);
        Assert.Empty(_state.TouchedOptions);
    }

    [Fact]
    public async Task ChatFc_NoneMode_IsSkipped()
    {
        _config = NoneConfig();
        var r = await ChatFc().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        _chatGui.Verify(c => c.Print(It.IsAny<XivChatEntry>()), Times.Never);
    }
}
