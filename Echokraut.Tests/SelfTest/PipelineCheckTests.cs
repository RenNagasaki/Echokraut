using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.Queue;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echotools.Logging.Enums;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest;

public class PipelineCheckTests
{
    private Configuration _config = LiveConfig();
    private readonly Mock<IVoiceMessageProcessor> _processor = new();
    private readonly Mock<IVoiceMessageQueue> _queue = new();
    private readonly Mock<IAudioPlaybackService> _playback = new();
    private readonly Mock<IDatabaseService> _db = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly Mock<IClientState> _clientState = new();
    private readonly Mock<IFramework> _framework = new();
    private bool _onFrameworkThread;

    private readonly NpcMapData _speaker = new(ObjectKind.None)
    {
        Name = PipelineCheck.SpeakerName, Gender = Genders.Male, Race = NpcRaces.Hyur, Language = ClientLanguage.English, voice = "Narrator",
    };

    public PipelineCheckTests()
    {
        PlaybackWatch.PollInterval = TimeSpan.FromMilliseconds(1);
        _clientState.SetupGet(c => c.ClientLanguage).Returns(ClientLanguage.English);
        _framework.Setup(f => f.RunOnFrameworkThread(It.IsAny<Func<Task<SelfTestSpeech>>>()))
            .Returns((Func<Task<SelfTestSpeech>> func) =>
            {
                _onFrameworkThread = true;
                try { return func(); }
                finally { _onFrameworkThread = false; }
            });
        _db.Setup(d => d.FindCharacter(_speaker.Name, _speaker.Gender, _speaker.Race, (int)ClientLanguage.English))
            .Returns(new CharacterEntity { Id = 42 });
        _db.Setup(d => d.GetVoiceClipsForCharacter(42, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new List<VoiceClipEntity> { new() { Id = 5 }, new() { Id = 6 } });
    }

    private PipelineCheck Create() => new(_config, _processor.Object, _queue.Object, _playback.Object, _db.Object,
        _npcData.Object, _clientState.Object, _framework.Object, Log().Object);

    /// <summary>The processor answers only when called inside the framework-thread callback (like the object table).</summary>
    private void Processes(VoiceMessage? queued, NpcMapData? speaker, string? error = null) =>
        _processor.Setup(p => p.ProcessSelfTestSpeechAsync(It.IsAny<EKEventId>(), PipelineCheck.SpeakerName, Constants.TESTMESSAGEEN))
            .Returns(() => _onFrameworkThread
                ? Task.FromResult(new SelfTestSpeech(queued, speaker, error))
                : throw new InvalidOperationException("Not on main thread!"));

    /// <summary>The queue reports the message playing for <paramref name="polls"/> polls, then nothing.</summary>
    private void PlaysFor(VoiceMessage message, int polls)
    {
        var entry = new VoiceMessageEntry(message) { StartedPlayingAt = DateTime.UtcNow };
        var left = polls;
        _queue.Setup(q => q.GetCurrentlyPlaying()).Returns(() => left-- > 0 ? entry : null);
    }

    [Fact]
    public async Task NoneMode_IsSkipped()
    {
        _config = NoneConfig();
        var result = await Create().RunAsync(CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task PluginSwitchedOff_IsSkipped()
    {
        _config.Enabled = false;
        var result = await Create().RunAsync(CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, result.Outcome);
    }

    [Fact]
    public async Task PlayedAndLogged_Passes_AndRemovesItsTraces()
    {
        var message = new VoiceMessage { Speaker = _speaker, VoiceClipId = 5, Text = "x" };
        Processes(message, _speaker);
        PlaysFor(message, 3);

        var result = await Create().RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        Assert.Contains("clip 5", result.Detail);
        _db.Verify(d => d.DeleteVoiceClip(5), Times.Once);
        _db.Verify(d => d.DeleteVoiceClip(6), Times.Once);
        _npcData.Verify(n => n.RemoveCharacter(_speaker), Times.Once);
    }

    [Fact]
    public async Task Played_ButNotLogged_Fails()
    {
        var message = new VoiceMessage { Speaker = _speaker, VoiceClipId = 0, Text = "x" };
        Processes(message, _speaker);
        PlaysFor(message, 1);

        var result = await Create().RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("database", result.Detail);
    }

    [Fact]
    public async Task StoppedBeforeTheBackend_Fails_AndStillRemovesTheCharacter()
    {
        Processes(null, _speaker);

        var result = await Create().RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("before the backend", result.Detail);
        _npcData.Verify(n => n.RemoveCharacter(_speaker), Times.Once);
    }

    [Fact]
    public async Task Pipeline_IsStartedOnTheFrameworkThread()
    {
        var message = new VoiceMessage { Speaker = _speaker, VoiceClipId = 5, Text = "x" };
        Processes(message, _speaker);
        PlaysFor(message, 1);

        var result = await Create().RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        _framework.Verify(f => f.RunOnFrameworkThread(It.IsAny<Func<Task<SelfTestSpeech>>>()), Times.Once);
    }

    [Fact]
    public async Task PipelineThrew_FailsWithTheError_NotWithAGuess()
    {
        Processes(null, _speaker, "InvalidOperationException: Not on main thread!");

        var result = await Create().RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        Assert.Contains("Not on main thread!", result.Detail);
        Assert.DoesNotContain("muted", result.Detail);
        _npcData.Verify(n => n.RemoveCharacter(_speaker), Times.Once);
    }

    [Fact]
    public async Task ResolvedToAnotherCharacter_IsNeverDeleted()
    {
        var real = new NpcMapData(ObjectKind.EventNpc) { Name = "Y'shtola", Language = ClientLanguage.English };
        Processes(null, real);

        await Create().RunAsync(CancellationToken.None);

        _npcData.Verify(n => n.RemoveCharacter(It.IsAny<NpcMapData>()), Times.Never);
        _db.Verify(d => d.DeleteVoiceClip(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CancelledWhilePlaying_StopsThePlayback()
    {
        var message = new VoiceMessage { Speaker = _speaker, VoiceClipId = 5, Text = "x" };
        Processes(message, _speaker);
        PlaysFor(message, int.MaxValue);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().RunAsync(cts.Token));

        _playback.Verify(p => p.StopPlaying(message), Times.Once);
        _npcData.Verify(n => n.RemoveCharacter(_speaker), Times.Once);
    }
}
