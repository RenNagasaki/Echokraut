using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.Checks;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public sealed class CleanupStageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-e2e-cleanup-" + Guid.NewGuid().ToString("N"));
    private readonly Configuration _config = new();
    private readonly Mock<IDatabaseService> _db = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly Mock<IGameVoiceReferenceService> _gameVoice = new();
    private readonly List<CharacterEntity> _characters = new();
    private readonly Dictionary<int, List<int>> _clipsByCharacter = new();
    private readonly List<int> _deletedClips = new();
    private readonly List<NpcMapData> _mappedNpcs = new();
    private readonly E2eState _state = E2eStateStoreTests.State();

    public CleanupStageTests()
    {
        Directory.CreateDirectory(_root);
        _state.Request = _state.Request with { InstallRoot = _root };
        _state.ConfigIsE2e = true;
        _state.Phase = E2ePhase.Stages;
        _db.Setup(d => d.GetAllCharacters()).Returns(() => _characters.ToList());
        _db.Setup(d => d.GetVoiceClipsForCharacter(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int id, int _, int _) => _clipsByCharacter.GetValueOrDefault(id, new()).Select(c => new VoiceClipEntity { Id = c }).ToList());
        _db.Setup(d => d.DeleteVoiceClip(It.IsAny<int>())).Callback((int id) => _deletedClips.Add(id));
        _db.Setup(d => d.DeleteCharacter(It.IsAny<int>())).Callback((int id) => _characters.RemoveAll(c => c.Id == id));
        _npcData.SetupGet(n => n.MappedNpcs).Returns(() => _mappedNpcs.ToList());
        _npcData.SetupGet(n => n.MappedPlayers).Returns(new List<NpcMapData>());
        _npcData.Setup(n => n.RemoveCharacter(It.IsAny<NpcMapData>())).Callback((NpcMapData d) =>
        {
            _mappedNpcs.Remove(d);
            _characters.RemoveAll(c => c.Name == d.Name);
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private Task<SelfTestResult> Run(IE2eGameSound? sound = null) =>
        new CleanupStage(_config, _db.Object, _npcData.Object, _gameVoice.Object, sound)
            .RunAsync(new E2eContext(_state, () => { }, _ => { }), CancellationToken.None);

    [Fact]
    public async Task GameVoiceVolumeLeftByAScenario_IsRestored()
    {
        var sound = new Mock<IE2eGameSound>();
        uint current = 50;
        sound.SetupGet(g => g.VoiceVolume).Returns(() => current);
        sound.Setup(g => g.SetVoiceVolumeAsync(It.IsAny<uint>())).Returns((uint v) => { current = v; return Task.CompletedTask; });
        _state.CreatedIds.Add(E2eGameSoundIds.Id(80));

        var r = await Run(sound.Object);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(80u, current);
        Assert.Empty(_state.CreatedIds);
    }

    [Fact]
    public async Task GameVoiceVolumeWithoutTheSoundService_StaysListed()
    {
        _state.CreatedIds.Add(E2eGameSoundIds.Id(80));
        var r = await Run();
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("gamecfg:SoundVoice=80", r.Detail);
    }

    private void AddCharacter(int id, string name, params int[] clips)
    {
        _characters.Add(new CharacterEntity { Id = id, Name = name });
        _clipsByCharacter[id] = clips.ToList();
    }

    [Fact]
    public void Stage_RunsAlwaysAndAfterAnAbort()
    {
        var stage = new CleanupStage(_config, _db.Object, _npcData.Object, _gameVoice.Object);
        Assert.True(stage.AlwaysRuns);
        Assert.True(stage.RunsAfterAbort);
        Assert.True(stage.IsIn(new E2eRequest("r1", "E:\\EkE2E", E2eRequest.AllEngines, null, ["preconditions"])));
    }

    [Fact]
    public async Task BeforeReset_TouchesNothing_ReportOnly()
    {
        _state.ConfigIsE2e = false;
        _state.Phase = E2ePhase.Requested;
        AddCharacter(1, PipelineCheck.SpeakerName, 5);

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("report only", r.Detail);
        _db.Verify(d => d.DeleteCharacter(It.IsAny<int>()), Times.Never);
        _db.Verify(d => d.DeleteVoiceClip(It.IsAny<int>()), Times.Never);
        _gameVoice.Verify(g => g.ClearMemory(), Times.Never);
    }

    [Fact]
    public async Task BeforeReset_WithListedLeftovers_FailsWithoutTouchingThem()
    {
        _state.ConfigIsE2e = false;
        _state.Phase = E2ePhase.ResetPending;
        _state.CreatedIds.Add("clip:7");

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("clip:7", r.Detail);
        Assert.Single(_state.CreatedIds);
        _db.Verify(d => d.DeleteVoiceClip(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RestoresTouchedOptions_AndSwitchesTheGameVoiceOptInOff()
    {
        E2eOptions.Set(_state, _config, nameof(Configuration.VoiceChat), true);
        E2eOptions.Set(_state, _config, nameof(Configuration.GameVoiceReferenceEnabled), true);

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.False(_config.VoiceChat);
        Assert.False(_config.GameVoiceReferenceEnabled);
        Assert.Empty(_state.TouchedOptions);
        _gameVoice.Verify(g => g.ClearMemory(), Times.Once);
    }

    [Fact]
    public async Task GameVoiceOptInStillOn_WithoutARecord_IsForcedOff()
    {
        _config.GameVoiceReferenceEnabled = true;

        var r = await Run();

        Assert.False(_config.GameVoiceReferenceEnabled);
        Assert.Contains("(forced)", r.Detail);
    }

    [Fact]
    public async Task DeletesCreatedIds_WavClipAndCharacter()
    {
        var wav = Path.Combine(_root, "LocalSaves", "a.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(wav)!);
        File.WriteAllText(wav, "x");
        AddCharacter(3, "Own Player", 11, 12);
        _state.CreatedIds.AddRange([$"character:Own Player", "clip:42", $"wav:{wav}"]);

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.False(File.Exists(wav));
        Assert.Equal(new[] { 42, 11, 12 }, _deletedClips);
        Assert.DoesNotContain(_characters, c => c.Name == "Own Player");
        Assert.Empty(_state.CreatedIds);
        Assert.Contains("3 created id(s) removed", r.Detail);
    }

    [Fact]
    public async Task CachedCharacter_GoesThroughTheNpcDataService()
    {
        var npc = new NpcMapData(Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc) { Name = "Some Npc" };
        _mappedNpcs.Add(npc);
        AddCharacter(4, "Some Npc");
        _state.CreatedIds.Add("character:Some Npc");

        await Run();

        _npcData.Verify(n => n.RemoveCharacter(npc), Times.Once);
        Assert.Empty(_mappedNpcs);
    }

    [Fact]
    public async Task WavOutsideTheInstallRoot_IsKept_AndFails()
    {
        var outside = Path.Combine(Path.GetTempPath(), "ek-e2e-outside-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllText(outside, "x");
        try
        {
            _state.CreatedIds.Add($"wav:{outside}");

            var r = await Run();

            Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
            Assert.True(File.Exists(outside));
            Assert.Contains($"wav:{outside}", _state.CreatedIds);
            Assert.Contains("still listed", r.Detail);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task UnknownId_StaysListed()
    {
        _state.CreatedIds.Add("thing:1");

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Equal(new[] { "thing:1" }, _state.CreatedIds);
    }

    [Fact]
    public async Task RemovesEverySelftestCharacter_WithItsClips()
    {
        AddCharacter(1, PipelineCheck.SpeakerName, 5);
        AddCharacter(2, PipelineCheck.SpeakerName);
        AddCharacter(3, "Real Npc", 9);

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("2 'Echokraut Selftest' character(s) removed", r.Detail);
        Assert.Equal(new[] { 5 }, _deletedClips);
        Assert.Equal(new[] { "Real Npc" }, _characters.Select(c => c.Name));
    }

    [Fact]
    public async Task SelftestRowThatWillNotGo_Fails()
    {
        AddCharacter(1, PipelineCheck.SpeakerName);
        _db.Setup(d => d.DeleteCharacter(It.IsAny<int>())); // delete does nothing

        var r = await Run();

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("1 'Echokraut Selftest' row(s) left", r.Detail);
    }

    [Theory]
    [InlineData("sub/a.wav", true)]
    [InlineData("../other/a.wav", false)]
    public void IsUnder_OnlyBelowTheRoot(string relative, bool expected)
    {
        Assert.Equal(expected, CleanupStage.IsUnder(Path.Combine(_root, relative), _root));
    }

    [Fact]
    public void IsUnder_SiblingWithTheSamePrefix_IsNotUnder()
    {
        Assert.False(CleanupStage.IsUnder(_root + "Other" + Path.DirectorySeparatorChar + "a.wav", _root));
        Assert.False(CleanupStage.IsUnder("relative.wav", _root));
    }
}
