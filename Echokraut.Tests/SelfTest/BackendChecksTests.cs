using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Echokraut.DataClasses;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Moq;
using Xunit;
using static Echokraut.Tests.SelfTest.SelfTestFixtures;

namespace Echokraut.Tests.SelfTest;

/// <summary>BackendCheck, VoicesCheck, GenerationCheck (BackendChecks.cs).</summary>
public class BackendChecksTests
{
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly Mock<IClientState> _clientState = new();
    private readonly List<EchokrautVoice> _voices = new();

    public BackendChecksTests()
    {
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(_voices);
        _clientState.SetupGet(c => c.ClientLanguage).Returns(ClientLanguage.English);
    }

    [Fact]
    public async Task AllThree_SkipInNoneMode()
    {
        var none = NoneConfig();
        var log = Log().Object;
        Assert.Equal(SelfTestOutcome.Skipped, (await new BackendCheck(none, _backend.Object, log).RunAsync(CancellationToken.None)).Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, (await new VoicesCheck(none, _npcData.Object).RunAsync(CancellationToken.None)).Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, (await new GenerationCheck(none, _backend.Object, _npcData.Object, _clientState.Object, log).RunAsync(CancellationToken.None)).Outcome);
        _backend.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Backend_Unreachable_Fails_WithAFreshProbe()
    {
        _backend.Setup(b => b.IsBackendAvailable()).Returns(true);
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(false);

        var result = await new BackendCheck(LiveConfig(), _backend.Object, Log().Object).RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
        _backend.Verify(b => b.InvalidateReachabilityCache(), Times.Once);
    }

    [Fact]
    public async Task Backend_Reachable_PassesWithTheReadyAnswer()
    {
        _backend.Setup(b => b.IsBackendAvailable()).Returns(true);
        _backend.Setup(b => b.IsBackendReachableAsync()).ReturnsAsync(true);
        _backend.Setup(b => b.CheckReady(It.IsAny<EKEventId>())).ReturnsAsync("Ready");

        var result = await new BackendCheck(LiveConfig(), _backend.Object, Log().Object).RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        Assert.EndsWith("Ready", result.Detail);
    }

    [Fact]
    public async Task Voices_NoneEnabled_Fails()
    {
        _voices.Add(new EchokrautVoice { VoiceName = "A", IsEnabled = false });
        var result = await new VoicesCheck(LiveConfig(), _npcData.Object).RunAsync(CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
    }

    [Fact]
    public async Task Voices_Enabled_PassNamingTheDefault()
    {
        _voices.Add(new EchokrautVoice { VoiceName = "A" });
        _voices.Add(new EchokrautVoice { VoiceName = "Narrator", IsDefault = true });
        var result = await new VoicesCheck(LiveConfig(), _npcData.Object).RunAsync(CancellationToken.None);
        Assert.Equal(SelfTestResult.Pass("2 of 2 voice(s) enabled, default: Narrator."), result);
    }

    [Fact]
    public void PickTestVoice_PrefersTheEnabledDefault()
    {
        _voices.Add(new EchokrautVoice { VoiceName = "A" });
        _voices.Add(new EchokrautVoice { VoiceName = "Off", IsDefault = true, IsEnabled = false });
        _voices.Add(new EchokrautVoice { VoiceName = "Narrator", IsDefault = true });
        Assert.Equal("Narrator", LiveGeneration.PickTestVoice(_npcData.Object)!.VoiceName);
    }

    [Fact]
    public async Task Generation_AudioArrives_Passes_AndDisposesTheStream()
    {
        _voices.Add(new EchokrautVoice { VoiceName = "Narrator", IsDefault = true });
        var stream = new TrackingStream(new byte[100]);
        VoiceMessage? sent = null;
        _backend.Setup(b => b.GenerateVoice(It.IsAny<VoiceMessage>()))
            .Callback((VoiceMessage m) => { sent = m; m.Stream = stream; })
            .ReturnsAsync(true);

        var result = await new GenerationCheck(LiveConfig(), _backend.Object, _npcData.Object, _clientState.Object, Log().Object)
            .RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, result.Outcome);
        Assert.Contains("100 bytes", result.Detail);
        Assert.True(stream.Disposed);
        Assert.True(sent!.NoCache);
        Assert.Equal(Constants.TESTMESSAGEEN, sent.Text);
    }

    [Fact]
    public async Task Generation_NoAudio_Fails()
    {
        _voices.Add(new EchokrautVoice { VoiceName = "Narrator" });
        _backend.Setup(b => b.GenerateVoice(It.IsAny<VoiceMessage>())).ReturnsAsync(false);

        var result = await new GenerationCheck(LiveConfig(), _backend.Object, _npcData.Object, _clientState.Object, Log().Object)
            .RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
    }

    [Fact]
    public async Task Generation_EmptyStream_Fails()
    {
        _voices.Add(new EchokrautVoice { VoiceName = "Narrator" });
        _backend.Setup(b => b.GenerateVoice(It.IsAny<VoiceMessage>()))
            .Callback((VoiceMessage m) => m.Stream = new MemoryStream())
            .ReturnsAsync(true);

        var result = await new GenerationCheck(LiveConfig(), _backend.Object, _npcData.Object, _clientState.Object, Log().Object)
            .RunAsync(CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, result.Outcome);
    }

    private sealed class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
