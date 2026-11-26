using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echotools.Logging.DataClasses;
using Moq;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>
/// Game-voice lines against a real <see cref="VoiceMessageQueue"/> and a fake player: every line sent under a name is
/// queued, "generated" with <see cref="GeneratedWith"/> (what <c>BackendService.GenerateVoice</c> sets) and played.
/// </summary>
internal sealed class GameVoiceLineRig : IDisposable
{
    public readonly VoiceMessageQueue Queue = new();
    public readonly Mock<IGameVoiceReferenceService> GameVoice = new();
    public readonly Mock<IVoiceMessageProcessor> Processor = new();
    public readonly Mock<IAudioPlaybackService> Playback = new();
    public readonly Mock<IFramework> Framework = new();
    public readonly Mock<IClientState> ClientState = new();
    public readonly Mock<IDatabaseService> Db = new();
    public readonly Mock<INpcDataService> NpcData = new();
    public readonly E2eState State = E2eStateStoreTests.State();
    public readonly List<string> Sent = new();
    public readonly List<NpcMapData> Speakers = new();
    private readonly CancellationTokenSource _playerCts = new();
    private int _nextClip = 42;

    /// <summary>Reference source of the line spoken under a name.</summary>
    public Func<string, ReferenceSource> GeneratedWith { get; set; } = _ => ReferenceSource.GameVoice;

    public GameVoiceLineRig()
    {
        PlaybackWatch.PollInterval = TimeSpan.FromMilliseconds(1);
        State.ConfigIsE2e = true;
        ClientState.SetupGet(c => c.ClientLanguage).Returns(ClientLanguage.English);
        Framework.Setup(f => f.RunOnFrameworkThread(It.IsAny<Func<Task<SelfTestSpeech>>>()))
            .Returns((Func<Task<SelfTestSpeech>> func) => func());
        NpcData.SetupGet(n => n.MappedNpcs).Returns(new List<NpcMapData>());
        Processor.Setup(p => p.ProcessSelfTestSpeechAsync(It.IsAny<EKEventId>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((EKEventId e, string name, string text) =>
            {
                lock (Sent) Sent.Add(name);
                var speaker = new NpcMapData(ObjectKind.None)
                {
                    Name = name, Gender = Genders.Female, Race = NpcRaces.Miqote, Language = ClientLanguage.English, voice = "Npc1",
                };
                lock (Speakers) Speakers.Add(speaker);
                var message = new VoiceMessage
                {
                    Source = e.TextSource, Text = text, OriginalText = text, Speaker = speaker,
                    VoiceClipId = Interlocked.Increment(ref _nextClip) - 1,
                    CancelEpoch = Queue.GetEpoch(e.TextSource), IsSelfTest = true,
                };
                Queue.Enqueue(message);
                return Task.FromResult(new SelfTestSpeech(message, speaker));
            });
        StartPlayer();
    }

    public void Dispose()
    {
        _playerCts.Cancel();
        Queue.Dispose();
    }

    private void StartPlayer()
    {
        var ct = _playerCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                if (Queue.TryDequeuePendingGeneration(out var pending) && pending != null)
                {
                    pending.Message.ReferenceSource = GeneratedWith(pending.Message.Speaker.Name);
                    Queue.MarkAsGenerating(pending.Id);
                    Queue.MarkAsReadyToPlay(pending.Id);
                }
                if (Queue.TryDequeueReadyToPlay(out var ready) && ready != null && !ready.IsTerminal)
                {
                    Queue.MarkAsPlaying(ready.Id);
                    await Task.Delay(5, CancellationToken.None);
                    Queue.MarkAsCompleted(ready.Id);
                }
                await Task.Delay(1, CancellationToken.None);
            }
        }, CancellationToken.None);
    }

    public E2eGameVoiceGeneration Generation() => new(GameVoice.Object, Db.Object, NpcData.Object, Queue, Playback.Object,
        new E2eSelfTestSpeaker(Processor.Object, Framework.Object, ClientState.Object, Db.Object, NpcData.Object, Log().Object));

    public E2eContext Ctx() => new(State, () => { }, _ => { });

    /// <summary>A deadline for the test (the player and the waits poll).</summary>
    public static CancellationToken Deadline() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;
}
