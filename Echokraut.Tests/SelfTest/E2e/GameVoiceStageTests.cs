using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.DataClasses;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public sealed class GameVoiceStageTests : IDisposable
{
    private static readonly GameVoiceSampleNpc Yshtola = new("Y'shtola", "yshtola", "Y'shtola", 300);
    private static readonly GameVoiceSampleNpc Thancred = new("Thancred", "thancred", "Thancred Waters", 200);
    private static readonly GameVoiceSampleNpc Urianger = new("Urianger", "urianger", "Urianger Augurelt", 150);

    private readonly GameVoiceLineRig _rig = new();
    private readonly Mock<IGameVoiceMappingService> _mapping = new();
    private readonly List<GameVoiceAliasEntry> _localAliases = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-gv-stage-" + Guid.NewGuid().ToString("N"));
    private readonly Configuration _config = new() { BackendSelection = TTSBackends.EchokrauTTS, SaveToLocal = true };
    private EchokrauTtsHealthResponse? _health = new() { inline_reference = true };
    private GameVoiceAvailability _availability = GameVoiceAvailability.Available;
    private int _ranks;

    public GameVoiceStageTests()
    {
        Directory.CreateDirectory(_root);
        _config.LocalSaveLocation = Path.Combine(_root, "saves");
        _rig.GameVoice.Setup(g => g.RefreshAvailabilityAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _availability);
        _rig.GameVoice.Setup(g => g.SampleVoicedNpcsAsync(ClientLanguage.English, 1, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Yshtola });
        _rig.GameVoice.Setup(g => g.SampleVoicedNpcsAsync(ClientLanguage.English, 2, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Thancred, Urianger });
        _rig.GameVoice.Setup(g => g.RankAsync(It.IsAny<GameVoiceSpeaker>(), It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .Callback(() => _ranks++).ReturnsAsync(3);
        _mapping.SetupGet(m => m.LocalAliases).Returns(() => _localAliases.ToList());
        _mapping.Setup(m => m.SetLocalAlias(It.IsAny<GameVoiceAliasEntry>())).Callback((GameVoiceAliasEntry e) =>
        {
            _localAliases.RemoveAll(a => a.ShortName == e.ShortName);
            _localAliases.Add(e);
        });
        _mapping.Setup(m => m.RemoveLocalAlias(It.IsAny<string>())).Callback((string t) => _localAliases.RemoveAll(a => a.ShortName == t));
        _mapping.Setup(m => m.ExportAliasesForRepo(It.IsAny<string>())).Returns((string path) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var file = new GameVoiceAliasFile { Version = 1, Aliases = _localAliases.OrderBy(a => a.ShortName).ToList() };
            File.WriteAllText(path, JsonSerializer.Serialize(file));
            return file.Aliases.Count;
        });
        // A plugin that does it right: override "-" / disabled alias → voice pack, otherwise game voice.
        _rig.GeneratedWith = name =>
        {
            var key = GameVoiceReferenceService.OverrideKey(ClientLanguage.English, name);
            if (_config.GameVoiceNpcOverrides.TryGetValue(key, out var v) && v.Length == 0) return ReferenceSource.VoicePack;
            if (_localAliases.Any(a => a.Disabled && VoiceExtractKey.Normalize(name).StartsWith(a.ShortName, StringComparison.Ordinal)))
                return ReferenceSource.VoicePack;
            return _availability == GameVoiceAvailability.Available ? ReferenceSource.GameVoice : ReferenceSource.VoicePack;
        };
    }

    public void Dispose()
    {
        _rig.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private GameVoiceStage Stage(string? embedded = """{ "version": 1, "aliases": [] }""") => new(_config, _rig.GameVoice.Object, _mapping.Object,
        _rig.Generation(), _ => Task.FromResult(_health), () => embedded, pluginConfigDir: null,
        rankWait: TimeSpan.FromMilliseconds(200), poll: TimeSpan.FromMilliseconds(5),
        watchRoots: _ => [("<install>", _root)]);

    private SelfTestOutcome? Part(string id) => _rig.State.ResultOf(id)?.Outcome;

    private void RaiseRankingWhenSpoken(params string[] tokens)
    {
        // Only the first line of an NPC without index ranks it (the live quick stage + Whisper).
        var inner = _rig.GeneratedWith;
        var ranked = new HashSet<string>();
        _rig.GeneratedWith = name =>
        {
            var token = VoiceExtractKey.Normalize(name);
            if (tokens.Contains(token) && ranked.Add(token))
                _rig.GameVoice.Raise(g => g.PairRanked += null,
                    new GameVoiceRankTiming(ClientLanguage.English, token, "", 120, 2400, 3, true));
            return inner(name);
        };
    }

    // ── per engine ──

    [Fact]
    public async Task Engine_InlineReference_SpeaksWithTheGameVoice_RanksOnce_AndSwitchesTheOptInOffAgain()
    {
        var stage = Stage();
        var first = await stage.RunOnEngineAsync(_rig.Ctx(), "qwen-0.6b", GameVoiceLineRig.Deadline());
        var second = await stage.RunOnEngineAsync(_rig.Ctx(), "pocket", GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Pass, first.Outcome);
        Assert.Equal(SelfTestOutcome.Pass, second.Outcome);
        Assert.Contains("GameVoice", second.Detail);
        Assert.Equal(1, _ranks);
        Assert.Equal(new[] { "Y'shtola", "Y'shtola" }, _rig.Sent);
        Assert.False(_config.GameVoiceReferenceEnabled);
        Assert.Empty(_rig.State.TouchedOptions);
    }

    [Fact]
    public async Task Engine_WithoutInlineReference_MustBeUnsupported_AndSpeakWithTheVoicePack()
    {
        _health = new EchokrauTtsHealthResponse { inline_reference = false };
        _availability = GameVoiceAvailability.ServerUnsupported;

        var r = await Stage().RunOnEngineAsync(_rig.Ctx(), "moss", GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("inline_reference false -> ServerUnsupported", r.Detail);
    }

    [Fact]
    public async Task Engine_WithoutInlineReference_ButAvailable_Fails()
    {
        _health = new EchokrauTtsHealthResponse { inline_reference = null };

        var r = await Stage().RunOnEngineAsync(_rig.Ctx(), "moss", GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("expected ServerUnsupported", r.Detail);
        Assert.Empty(_rig.Sent);
    }

    [Fact]
    public async Task Engine_UnsupportedButTheLineCarriesTheGameVoice_Fails()
    {
        _health = new EchokrauTtsHealthResponse { inline_reference = false };
        _availability = GameVoiceAvailability.ServerUnsupported;
        _rig.GeneratedWith = _ => ReferenceSource.GameVoice;

        var r = await Stage().RunOnEngineAsync(_rig.Ctx(), "moss", GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("spoken with GameVoice, expected VoicePack", r.Detail);
    }

    [Fact]
    public async Task Engine_AudioFileWrittenDuringThePart_Fails_WithTheLabelledPath()
    {
        var inner = _rig.GeneratedWith;
        _rig.GeneratedWith = name =>
        {
            File.WriteAllBytes(Path.Combine(_root, "ref.wav"), [1, 2, 3]);
            return inner(name);
        };

        var r = await Stage().RunOnEngineAsync(_rig.Ctx(), "f5", GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains(Path.Combine("<install>", "ref.wav").Replace(Path.DirectorySeparatorChar, '\\'), r.Detail.Replace('/', '\\'));
    }

    // ── stage ──

    [Fact]
    public async Task Stage_OnAnEngineWithoutGameVoice_SkipsEveryPart()
    {
        _availability = GameVoiceAvailability.ServerUnsupported;

        var r = await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, Part(GameVoiceStage.FirstLinePart));
        Assert.Equal(SelfTestOutcome.Skipped, Part(GameVoiceStage.AliasPart));
        Assert.Empty(_rig.Sent);
    }

    [Fact]
    public async Task Stage_RealConfig_IsSkipped()
    {
        _rig.State.ConfigIsE2e = false;
        var r = await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        _rig.GameVoice.Verify(g => g.RefreshAvailabilityAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stage_AllParts_Pass_AndPutEverythingBack()
    {
        RaiseRankingWhenSpoken("thancred", "urianger");
        _config.GameVoiceNpcOverrides["EN|someone"] = "x";

        var r = await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Contains("ranking quick 120 ms, total 2400 ms (Whisper)", _rig.State.ResultOf(GameVoiceStage.FirstLinePart)!.Detail);
        // first-line 2, character field 3 (Thancred), alias 2 (Urianger)
        Assert.Equal(new[] { "Thancred", "Urianger", "Thancred", "Thancred", "Thancred", "Urianger", "Urianger" }, _rig.Sent);
        Assert.Equal(new Dictionary<string, string> { ["EN|someone"] = "x" }, _config.GameVoiceNpcOverrides);
        Assert.Empty(_localAliases);
        Assert.False(File.Exists(Path.Combine(_config.LocalSaveLocation, "harvest", GameVoiceReferenceService.AliasRepoExportFile)));
        Assert.False(_config.GameVoiceReferenceEnabled);
        Assert.Empty(_rig.State.TouchedOptions);
    }

    [Fact]
    public async Task FirstLine_WithoutARankingTiming_Fails()
    {
        var r = await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());
        Assert.Equal(SelfTestOutcome.Fail, Part(GameVoiceStage.FirstLinePart));
        Assert.Contains("no ranking of thancred finished", _rig.State.ResultOf(GameVoiceStage.FirstLinePart)!.Detail);
        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
    }

    [Fact]
    public async Task CharacterField_IgnoredByThePlugin_Fails()
    {
        RaiseRankingWhenSpoken("thancred", "urianger");
        var correct = _rig.GeneratedWith;
        _rig.GeneratedWith = name =>
        {
            var source = correct(name); // keeps the first line's ranking
            return name == "Thancred" ? ReferenceSource.GameVoice : source;
        };

        await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, Part(GameVoiceStage.CharacterFieldPart));
        Assert.Contains("field '-': spoken with GameVoice, expected VoicePack", _rig.State.ResultOf(GameVoiceStage.CharacterFieldPart)!.Detail);
        Assert.Empty(_config.GameVoiceNpcOverrides);
    }

    [Fact]
    public async Task Alias_ThatRanksAgain_Fails()
    {
        RaiseRankingWhenSpoken("thancred", "urianger");
        var inner = _rig.GeneratedWith;
        var urianger = 0;
        _rig.GeneratedWith = name =>
        {
            // The first-line already timed Urianger; any later line that ranks again is a re-rank.
            if (name == "Urianger" && ++urianger > 1)
                _rig.GameVoice.Raise(g => g.PairRanked += null, new GameVoiceRankTiming(ClientLanguage.English, "urianger", "", 1, 2, 3, true));
            return inner(name);
        };

        await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, Part(GameVoiceStage.AliasPart));
        Assert.Contains("alias change ranked again: urianger", _rig.State.ResultOf(GameVoiceStage.AliasPart)!.Detail);
    }

    [Fact]
    public async Task Alias_ExportMissingAResourceToken_Fails_AndAnEarlierLocalEntryComesBack()
    {
        RaiseRankingWhenSpoken("thancred", "urianger");
        var earlier = new GameVoiceAliasEntry { ShortName = "urianger", NpcName = "Urianger Augurelt", Comment = "mine" };
        _localAliases.Add(earlier);

        await Stage("""{ "version": 1, "aliases": [ { "ShortName": "vritra", "NpcName": "Vrtra" } ] }""")
            .RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, Part(GameVoiceStage.AliasPart));
        Assert.Contains("resource token(s) missing from the export: vritra", _rig.State.ResultOf(GameVoiceStage.AliasPart)!.Detail);
        var back = Assert.Single(_localAliases);
        Assert.Equal((earlier.ShortName, earlier.NpcName, earlier.Comment, earlier.Disabled), (back.ShortName, back.NpcName, back.Comment, back.Disabled));
    }

    [Fact]
    public void EnginePartId_IsNotAnEnginePart() =>
        Assert.Equal("game-voice:engine-qwen-0.6b", GameVoiceStage.EnginePartId(" Qwen-0.6B "));
}
