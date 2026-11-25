using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest;

/// <summary>ContinuationCheck, GameVoiceCheck (FeatureChecks.cs).</summary>
public class FeatureChecksTests
{
    private readonly Mock<IClientState> _clientState = new();
    private readonly Mock<IDialogContinuationService> _continuation = new();
    private readonly Mock<IGameVoiceReferenceService> _gameVoice = new();

    public FeatureChecksTests()
    {
        _clientState.SetupGet(c => c.ClientLanguage).Returns(ClientLanguage.English);
        _continuation.Setup(c => c.Prepare(ClientLanguage.English)).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Continuation_OptionOff_SkipsWithoutBuilding()
    {
        var config = LiveConfig();
        config.JoinSplitSentences = false;

        var result = await new ContinuationCheck(config, _continuation.Object, _clientState.Object).RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Skipped, result.Outcome);
        _continuation.Verify(c => c.Prepare(It.IsAny<ClientLanguage>()), Times.Never);
    }

    [Theory]
    [InlineData(null, SelfTestOutcome.Fail)]
    [InlineData(0, SelfTestOutcome.Fail)]
    [InlineData(812, SelfTestOutcome.Pass)]
    public async Task Continuation_JudgesTheIndexSize(int? count, SelfTestOutcome expected)
    {
        var config = LiveConfig();
        config.JoinSplitSentences = true;
        _continuation.Setup(c => c.JoinableBoxCount(ClientLanguage.English)).Returns(count);

        var result = await new ContinuationCheck(config, _continuation.Object, _clientState.Object).RunAsync(CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    private Task<SelfTestResult> GameVoice(bool enabled = true, TTSBackends backend = TTSBackends.EchokrauTTS)
    {
        var config = LiveConfig();
        config.GameVoiceReferenceEnabled = enabled;
        config.BackendSelection = backend;
        return new GameVoiceCheck(config, _gameVoice.Object, _clientState.Object).RunAsync(CancellationToken.None);
    }

    private void Available(GameVoiceAvailability availability) =>
        _gameVoice.Setup(g => g.RefreshAvailabilityAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(availability);

    [Fact]
    public async Task GameVoice_WithoutOptIn_TouchesNothing()
    {
        Assert.Equal(SelfTestOutcome.Skipped, (await GameVoice(enabled: false)).Outcome);
        _gameVoice.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GameVoice_OnAllTalk_IsSkipped()
    {
        Assert.Equal(SelfTestOutcome.Skipped, (await GameVoice(backend: TTSBackends.Alltalk)).Outcome);
        _gameVoice.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GameVoice_ServerWithoutSupport_FailsBeforeDecoding()
    {
        Available(GameVoiceAvailability.ServerUnsupported);

        var result = await GameVoice();

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        _gameVoice.Verify(g => g.SelfCheckAsync(It.IsAny<ClientLanguage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, null, SelfTestOutcome.Fail)]
    [InlineData(500, null, SelfTestOutcome.Fail)]
    [InlineData(500, "cut/ffxiv/sound/voicem/voiceman_06006/vo_voiceman_06006_000010_m", SelfTestOutcome.Pass)]
    public async Task GameVoice_JudgesTheSelfCheck(int lines, string? clip, SelfTestOutcome expected)
    {
        Available(GameVoiceAvailability.Available);
        _gameVoice.Setup(g => g.SelfCheckAsync(ClientLanguage.English, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameVoiceSelfCheck(40, lines, clip, 3.2));

        Assert.Equal(expected, (await GameVoice()).Outcome);
    }
}
