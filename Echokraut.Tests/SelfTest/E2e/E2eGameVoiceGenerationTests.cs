using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echotools.Logging.DataClasses;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>The game-voice line of the stage <c>remote</c> and the opt-in helpers, against a real queue and a fake player.</summary>
public sealed class E2eGameVoiceGenerationTests : IDisposable
{
    private const string Npc = "Y'shtola";
    private readonly GameVoiceLineRig _rig = new();

    public E2eGameVoiceGenerationTests()
    {
        _rig.GameVoice.Setup(g => g.SampleVoicedNpcsAsync(ClientLanguage.English, 1, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new GameVoiceSampleNpc(Npc, "yshtola", "Y'shtola", 40) });
        _rig.GameVoice.Setup(g => g.RankAsync(It.Is<GameVoiceSpeaker>(s => s.Name == Npc), It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    public void Dispose() => _rig.Dispose();

    private Task<SelfTestResult> Run() => _rig.Generation().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

    [Fact]
    public async Task SpokenWithTheGameVoice_Passes_UnderTheNpcsOwnName_AndRemovesItsTraces()
    {
        var r = await Run();

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(new[] { Npc }, _rig.Sent);
        _rig.Db.Verify(d => d.DeleteVoiceClip(42), Times.Once);
        _rig.NpcData.Verify(n => n.RemoveCharacter(_rig.Speakers.Single()), Times.Once);
        Assert.Empty(_rig.State.CreatedIds);
    }

    [Fact]
    public async Task SpokenWithTheVoicePack_Fails()
    {
        _rig.GeneratedWith = _ => ReferenceSource.VoicePack;
        var r = await Run();
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("VoicePack", r.Detail);
        _rig.Db.Verify(d => d.DeleteVoiceClip(42), Times.Once);
    }

    [Fact]
    public async Task NoVoicedNpc_Fails_WithoutSendingALine()
    {
        _rig.GameVoice.Setup(g => g.SampleVoicedNpcsAsync(It.IsAny<ClientLanguage>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GameVoiceSampleNpc>());
        var r = await Run();
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Empty(_rig.Sent);
    }

    [Fact]
    public async Task NothingRanked_Fails_WithoutSendingALine()
    {
        _rig.GameVoice.Setup(g => g.RankAsync(It.IsAny<GameVoiceSpeaker>(), It.IsAny<EKEventId>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var r = await Run();
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Empty(_rig.Sent);
    }

    [Fact]
    public async Task KnownCharacter_IsKept()
    {
        var known = new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.None) { Name = Npc };
        _rig.NpcData.SetupGet(n => n.MappedNpcs).Returns(new List<NpcMapData> { known });
        await Run();
        _rig.NpcData.Verify(n => n.RemoveCharacter(It.IsAny<NpcMapData>()), Times.Never);
        _rig.Db.Verify(d => d.DeleteVoiceClip(42), Times.Once);
    }

    [Fact]
    public async Task SpeakAsync_ReportsTheSourceAndTheTimeToFirstAudio()
    {
        _rig.GeneratedWith = _ => ReferenceSource.VoicePack;
        var line = await _rig.Generation().SpeakAsync(_rig.Ctx(), Npc, GameVoiceLineRig.Deadline());

        Assert.True(line.Played);
        Assert.Equal(ReferenceSource.VoicePack, line.Source);
        Assert.Equal(42, line.ClipId);
        Assert.True(line.FirstAudioMs >= 0);
    }

    [Fact]
    public void OptIn_SetsTheCurrentConsent_AndRestoreTurnsEverythingBack()
    {
        var config = new Configuration { GameVoiceRankOnConversationStart = false };
        var ctx = _rig.Ctx();

        E2eGameVoiceGeneration.EnableOptIn(ctx, config, rankOnFirstLine: true);
        Assert.True(config.GameVoiceReferenceEnabled);
        Assert.Equal(GameVoiceConsent.CurrentVersion, config.GameVoiceConsentVersion);
        Assert.True(config.GameVoiceRankOnConversationStart);

        E2eGameVoiceGeneration.RestoreOptIn(ctx, config, _rig.GameVoice.Object);
        Assert.False(config.GameVoiceReferenceEnabled);
        Assert.False(config.GameVoiceRankOnConversationStart);
        Assert.Empty(_rig.State.TouchedOptions);
        _rig.GameVoice.Verify(g => g.ClearMemory(), Times.Once);
    }

    [Theory]
    [InlineData(ReferenceSource.GameVoice, ReferenceSource.GameVoice, null)]
    [InlineData(ReferenceSource.VoicePack, ReferenceSource.GameVoice, "x: spoken with VoicePack, expected GameVoice")]
    public void Mismatch_NamesWhatWasSpoken(ReferenceSource spoken, ReferenceSource expected, string? message) =>
        Assert.Equal(message, E2eGameVoiceGeneration.Mismatch(new E2eGameVoiceLine(spoken, 1, 10, null), expected, "x"));

    [Fact]
    public void Mismatch_LineThatDidNotPlay_GivesItsError() =>
        Assert.Equal("x: gone", E2eGameVoiceGeneration.Mismatch(new E2eGameVoiceLine(null, 0, null, "gone"), ReferenceSource.VoicePack, "x"));
}
