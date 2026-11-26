using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.DataClasses;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public sealed class GameVoiceIndexStageTests : IDisposable
{
    private readonly GameVoiceLineRig _rig = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-gv-index-" + Guid.NewGuid().ToString("N"));
    private readonly Configuration _config = new() { BackendSelection = TTSBackends.EchokrauTTS };
    private readonly HashSet<string> _current = new();
    private readonly List<GameVoiceBatchOptions> _batches = new();
    private EchokrauTtsHealthResponse _health = new() { inline_reference = true, asr_check = true, speaker_embedding = true };
    private const int Pairs = 40;

    public GameVoiceIndexStageTests()
    {
        Directory.CreateDirectory(_root);
        _rig.State.Request = _rig.State.Request with { Stages = [GameVoiceIndexStage.StageId] };
        _rig.GameVoice.Setup(g => g.RefreshAvailabilityAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GameVoiceAvailability.Available);
        _rig.GameVoice.Setup(g => g.CountOutdatedTokensAsync(It.IsAny<ClientLanguage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Pairs - _current.Count);
        // A resumable batch over 40 pairs: skips current ones, reports each ranked pair, stops on the token.
        _rig.GameVoice.Setup(g => g.BuildIndexAsync(It.IsAny<ClientLanguage>(), It.IsAny<GameVoiceBatchOptions>(),
                It.IsAny<IProgress<GameVoiceBatchProgress>>(), It.IsAny<CancellationToken>()))
            .Returns((ClientLanguage _, GameVoiceBatchOptions o, IProgress<GameVoiceBatchProgress> p, CancellationToken ct) =>
            {
                _batches.Add(o);
                var work = Enumerable.Range(0, Pairs).Select(i => $"t{i:00}").Where(t => !_current.Contains(t)).ToList();
                var upToDate = Pairs - work.Count;
                var ranked = 0;
                foreach (var token in work)
                {
                    if (ct.IsCancellationRequested) return Task.FromResult(Result(ranked, upToDate, cancelled: true));
                    _current.Add(token);
                    ranked++;
                    _rig.GameVoice.Raise(g => g.PairRanked += null, new GameVoiceRankTiming(ClientLanguage.English, token, "", 5, 9, 3, o.WithAsr));
                    p.Report(new GameVoiceBatchProgress(ranked, work.Count, token, TimeSpan.FromSeconds(ranked)));
                }
                var report = Path.Combine(_root, "game_voice_unmatched_en.json");
                File.WriteAllText(report, """{ "Version": 1, "Language": "EN", "Unmatched": [], "Ambiguous": [] }""");
                var splits = Path.Combine(_root, "splits.json");
                File.WriteAllText(splits, """{ "Version": 2, "Splits": [] }""");
                return Task.FromResult(Result(ranked, upToDate, cancelled: false) with
                    { ReportPath = report, SuggestionsPath = o.DetectVoiceChanges ? splits : null });
            });
        _rig.GameVoice.Setup(g => g.SampleVoicedNpcsAsync(ClientLanguage.English, 20, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new GameVoiceSampleNpc("Y'shtola", "yshtola", "Y'shtola", 300), new GameVoiceSampleNpc("Lyse", "lyse", "Lyse Hext", 90) });
        _rig.State.Marks[GameVoiceStage.EngineNpcMark] = "Y'shtola";
    }

    private static GameVoiceBatchResult Result(int ranked, int upToDate, bool cancelled) =>
        new(Pairs, ranked, upToDate, ranked, 0, 0, null, null, TimeSpan.FromSeconds(ranked), cancelled);

    public void Dispose()
    {
        _rig.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private GameVoiceIndexStage Stage() => new(_config, _rig.GameVoice.Object, _rig.NpcData.Object, _rig.Generation(),
        _ => Task.FromResult<EchokrauTtsHealthResponse?>(_health), pluginConfigDir: null, watchRoots: _ => [("<install>", _root)]);

    private E2eStageReport Part(string id) => _rig.State.ResultOf(id)!;

    [Fact]
    public void OnlyWhenListed()
    {
        Assert.False(Stage().IsIn(_rig.State.Request with { Stages = null }));
        Assert.True(Stage().IsIn(_rig.State.Request));
    }

    [Fact]
    public async Task EveryPart_Passes_AndTheLastLineIsAnNpcTheRunNeverMet()
    {
        var r = await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Part(GameVoiceIndexStage.SplitsPart).Outcome); // EN client: no measured boundaries to expect
        Assert.Equal(SelfTestOutcome.Pass, Part(GameVoiceIndexStage.AbortResumePart).Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Part(GameVoiceIndexStage.WhisperPart).Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Part(GameVoiceIndexStage.FullPart).Outcome);
        Assert.Equal(SelfTestOutcome.Pass, Part(GameVoiceIndexStage.UnseenNpcPart).Outcome);
        Assert.Equal(new[] { "Lyse" }, _rig.Sent);
        // abort 1 + abort 2 fast, whisper, full fast with detection
        Assert.Equal(new[] { false, false, true, false }, _batches.Select(b => b.WithAsr));
        Assert.True(_batches[3].DetectVoiceChanges);
        Assert.False(_config.GameVoiceReferenceEnabled);
    }

    [Fact]
    public async Task AbortResume_SecondRunCountsTheFirstRunsPairs()
    {
        await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());
        var detail = Part(GameVoiceIndexStage.AbortResumePart).Detail;
        Assert.Contains("run 1: 5 ranked (5 with reference), 0 current", detail);
        Assert.Contains("run 2: 5 ranked (5 with reference), 5 current", detail);
    }

    [Fact]
    public void JudgeAbortResume_NoResume_NotCancelled_OrNotRun_AreNamed()
    {
        Assert.Equal(new[] { "resume: run 2 counted 0 pair(s) current, expected >= 5" },
            GameVoiceIndexStage.JudgeAbortResume(Result(5, 0, true), Result(5, 0, true), 5));
        Assert.Equal(new[] { "run 1 not cancelled (40 ranked)" },
            GameVoiceIndexStage.JudgeAbortResume(Result(40, 0, false), Result(5, 40, true), 5));
        var notRun = Result(0, 0, false) with { NotRunReason = "already running" };
        Assert.Contains("run 2 did not run: already running", GameVoiceIndexStage.JudgeAbortResume(Result(5, 0, true), notRun, 5));
    }

    [Fact]
    public async Task Whisper_RankingWithoutIt_Fails()
    {
        _rig.GameVoice.Setup(g => g.BuildIndexAsync(It.IsAny<ClientLanguage>(), It.Is<GameVoiceBatchOptions>(o => o.WithAsr),
                It.IsAny<IProgress<GameVoiceBatchProgress>>(), It.IsAny<CancellationToken>()))
            .Returns((ClientLanguage _, GameVoiceBatchOptions _, IProgress<GameVoiceBatchProgress> p, CancellationToken _) =>
            {
                _rig.GameVoice.Raise(g => g.PairRanked += null, new GameVoiceRankTiming(ClientLanguage.English, "t39", "", 5, 9, 3, false));
                return Task.FromResult(Result(1, 0, true));
            });

        await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, Part(GameVoiceIndexStage.WhisperPart).Outcome);
        Assert.Contains("ranked without Whisper: t39", Part(GameVoiceIndexStage.WhisperPart).Detail);
    }

    [Fact]
    public async Task WithoutSpeakerEncoder_SplitsAreSkipped_AndTheBatchDoesNotDetect()
    {
        _health = new EchokrauTtsHealthResponse { inline_reference = true, asr_check = false, speaker_embedding = false };

        var r = await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, Part(GameVoiceIndexStage.WhisperPart).Outcome);
        Assert.Equal(SelfTestOutcome.Skipped, Part(GameVoiceIndexStage.SplitsPart).Outcome);
        Assert.Contains("--engines xtts", Part(GameVoiceIndexStage.SplitsPart).Detail);
        Assert.False(_batches.Last().DetectVoiceChanges);
    }

    [Fact]
    public async Task UnseenNpc_RankedLive_Fails()
    {
        _rig.GeneratedWith = name =>
        {
            _rig.GameVoice.Raise(g => g.PairRanked += null, new GameVoiceRankTiming(ClientLanguage.English, "lyse", "", 5, 9, 3, true));
            return ReferenceSource.GameVoice;
        };
        _health = new EchokrauTtsHealthResponse { inline_reference = true, asr_check = true, speaker_embedding = false };

        await Stage().RunAsync(_rig.Ctx(), GameVoiceLineRig.Deadline());

        Assert.Equal(SelfTestOutcome.Fail, Part(GameVoiceIndexStage.UnseenNpcPart).Outcome);
        Assert.Contains("ranked live although the batch indexed it: lyse", Part(GameVoiceIndexStage.UnseenNpcPart).Detail);
    }

    [Theory]
    [InlineData(10, 40, 100, 0, ", ETA 00:05:00")]
    [InlineData(2, 92, 260, 200, ", ETA 00:45:00")] // second phase: only its own 60 s count
    [InlineData(40, 40, 100, 0, "")]
    [InlineData(0, 40, 0, 0, "")]
    public void Eta_FromTheMeanPairTimeOfThePhase(int done, int total, int elapsedSeconds, int phaseStartSeconds, string expected) =>
        Assert.Equal(expected, GameVoiceIndexStage.Eta(new GameVoiceBatchProgress(done, total, "t", TimeSpan.FromSeconds(elapsedSeconds)),
            TimeSpan.FromSeconds(phaseStartSeconds)));
}
