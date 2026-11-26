using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Stages;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class WrapperHttpStageTests
{
    private static readonly WrapperHttpTimings Fast = new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5),
        TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    private readonly Configuration _config = new() { BackendSelection = TTSBackends.EchokrauTTS };
    private readonly E2eState _state = E2eStateStoreTests.State();
    private readonly FakeWrapper _wrapper = new();
    private readonly Mock<INpcDataService> _npcData = new();
    private readonly Mock<IBackendService> _backend = new();
    private readonly Mock<IVoiceClipManagerService> _vcm = new();
    private readonly Mock<IDatabaseService> _db = new();
    private readonly EchokrautVoice _voice = new() { BackendVoice = "Narrator.wav", voiceName = "Narrator", IsEnabled = true, IsDefault = true };

    public WrapperHttpStageTests()
    {
        _state.ConfigIsE2e = true;
        _config.EchokrauTts.InstanceType = AlltalkInstanceType.Local;
        _npcData.Setup(n => n.GetEchokrautVoices()).Returns(new List<EchokrautVoice> { _voice });
        // The plugin's normal path: the same wrapper, no no_cache.
        _backend.Setup(b => b.GenerateVoice(It.IsAny<VoiceMessage>())).Returns<VoiceMessage>(async m =>
        {
            using var call = (await _wrapper.StartTtsAsync(_voice.BackendVoice, m.Text, m.NoCache, CancellationToken.None))!;
            await call.ReadToEndAsync(CancellationToken.None);
            m.Stream = new MemoryStream(((FakeWrapper.Call)call).Content);
            return true;
        });
        _db.Setup(d => d.UpsertCharacter(It.IsAny<CharacterEntity>())).Returns<CharacterEntity>(c => { c.Id = 5; return c; });
        _db.Setup(d => d.LogOrUpdateVoiceClip(It.IsAny<VoiceClipEntity>())).Returns<VoiceClipEntity>(c => { c.Id = 9; return c; });
        _vcm.Setup(v => v.GetAudioPath(It.IsAny<VoiceClipEntity>())).Returns(@"E:\EkE2E\saves\clip9.wav");
        _vcm.Setup(v => v.DeleteAudioForVoiceClip(It.IsAny<VoiceClipEntity>())).Returns(true);
        _vcm.Setup(v => v.BuildVoiceMessage(It.IsAny<VoiceClipEntity>())).Returns<VoiceClipEntity>(c => new VoiceMessage
        {
            Text = c.CleanedText,
            Speaker = new NpcMapData(ObjectKind.EventNpc) { Name = "Echokraut Selftest", Race = NpcRaces.Hyur, Gender = Genders.Male },
        });
        // The VCM button's call: always no_cache.
        _vcm.Setup(v => v.GenerateForVoiceClip(It.IsAny<VoiceClipEntity>())).Returns<VoiceClipEntity>(async c =>
        {
            using var call = (await _wrapper.StartTtsAsync(_voice.BackendVoice, c.CleanedText, _wrapper.VcmSendsNoCache, CancellationToken.None))!;
            await call.ReadToEndAsync(CancellationToken.None);
            return true;
        });
    }

    private WrapperHttpStage Stage(IE2eGpuProbe? gpu = null) => new(_config, _wrapper, _ => _wrapper.Log, _npcData.Object,
        _backend.Object, _vcm.Object, _db.Object, SelfTestFixtures.Log().Object,
        () => ClientLanguage.German, gpu, TimeSpan.FromMilliseconds(5), Fast);

    private E2eContext Ctx() => new(_state, () => { }, _ => { });

    [Fact]
    public async Task WorkingWrapper_PassesEveryPart_AndLeavesNoTraces()
    {
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(new[] { "wrapper-http:cache", "wrapper-http:cancel", "wrapper-http:vcm-regenerate", "wrapper-http:repeat-hit" },
            _state.StageResults.Select(s => s.Id));
        Assert.All(_state.StageResults, s => Assert.Equal(SelfTestOutcome.Pass, s.Outcome));
        Assert.Empty(_state.CreatedIds);
        _db.Verify(d => d.DeleteVoiceClip(9), Times.Once);
        Assert.Contains("hits 2, shared 1, misses 2, bypassed 1", _state.ResultOf("wrapper-http:cache")!.Detail);
    }

    [Fact]
    public async Task CacheThatNeverHits_FailsCacheAndRepeat()
    {
        _wrapper.CacheWorks = false;

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("repeat: X-Cache miss, expected hit", _state.ResultOf("wrapper-http:cache")!.Detail);
        var repeat = _state.ResultOf("wrapper-http:repeat-hit")!;
        Assert.Equal(SelfTestOutcome.Fail, repeat.Outcome);
        Assert.Contains("2 tts request start line(s) for one text twice, expected 1", repeat.Detail);
    }

    [Fact]
    public async Task VcmWithoutNoCache_FailsTheRegeneratePart()
    {
        _wrapper.VcmSendsNoCache = false;

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        var vcm = _state.ResultOf("wrapper-http:vcm-regenerate")!;
        Assert.Equal(SelfTestOutcome.Fail, vcm.Outcome);
        Assert.Contains("1 tts request start line(s) for two regenerations, expected 2", vcm.Detail);
        Assert.Empty(_state.CreatedIds);
    }

    [Fact]
    public async Task CancelOfOneClientStoppingTheGeneration_FailsTheCancelPart()
    {
        _wrapper.RefcountBroken = true;

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        var cancel = _state.ResultOf("wrapper-http:cancel")!;
        Assert.Equal(SelfTestOutcome.Fail, cancel.Outcome);
        Assert.Contains("generation cancelled after one of two cancels, expected done (ran to the end)", cancel.Detail);
    }

    [Fact]
    public async Task CancelledRunStoredInTheCache_FailsTheCancelPart()
    {
        _wrapper.StoresCancelled = true;

        await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Contains("same text after the cancel: X-Cache hit, expected miss", _state.ResultOf("wrapper-http:cancel")!.Detail);
    }

    [Fact]
    public async Task RealConfig_IsSkipped_WithoutAnyCall()
    {
        _state.ConfigIsE2e = false;
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Equal(0, _wrapper.TtsCalls);
    }

    [Fact]
    public async Task WrapperWithoutResponseCache_IsSkipped()
    {
        _wrapper.HasCacheInHealth = false;
        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Contains("before 0.0.2.0", r.Detail);
        Assert.Equal(0, _wrapper.TtsCalls);
    }

    [Fact]
    public async Task RestartedStage_KeepsThePartsItAlreadyReported()
    {
        _state.StageResults.Add(new E2eStageReport("wrapper-http:cache", SelfTestOutcome.Pass, "earlier", 1));
        _state.StageResults.Add(new E2eStageReport("wrapper-http:cancel", SelfTestOutcome.Pass, "earlier", 1));

        var r = await Stage().RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal("earlier", _state.ResultOf("wrapper-http:cache")!.Detail);
        Assert.DoesNotContain(_wrapper.Texts, t => t.Contains(" cache ") || t.Contains(" cancel-"));
    }

    [Fact]
    public void UnmetNeed_WithEngineInTheRun_RequiresTheLastPassedEngineActive()
    {
        var stage = Stage();
        _state.StageResults.Add(new E2eStageReport(FirstTimeStage.StageId, SelfTestOutcome.Pass, "", 1));
        _state.StageResults.Add(new E2eStageReport("engine:xtts", SelfTestOutcome.Pass, "", 1));
        Assert.Equal("needs engine:xtts active again (switch back failed)", stage.UnmetNeed(_state));
        _state.Marks[EngineStage.ActiveEngineMark] = "xtts";
        Assert.Null(stage.UnmetNeed(_state));
    }

    [Fact]
    public void Texts_CarryTheRunId_SoAnEarlierRunsEntryNeverAnswers()
    {
        var a = Stage().Text(Ctx(), "cache");
        _state.Request = _state.Request with { Id = "r2" };
        var b = Stage().Text(Ctx(), "cache");
        Assert.NotEqual(a, b);
        Assert.Equal(3, Stage().Text(Ctx(), "cancel", 3).Split(" cancel ").Length - 1);
    }

    [Fact]
    public void JudgeCacheDelta_NamesEveryCounterThatMovedOtherwise()
    {
        Assert.Null(WrapperHttpStage.JudgeCacheDelta(new E2eCacheStats(true, 0, 0, 2, 1, 2, 1), hits: 2, shared: 1, misses: 2, bypassed: 1));
        Assert.Equal("/health response_cache: hits +1 (expected +2), bypassed +0 (expected +1)",
            WrapperHttpStage.JudgeCacheDelta(new E2eCacheStats(true, 0, 0, 1, 1, 2, 0), hits: 2, shared: 1, misses: 2, bypassed: 1));
    }

    [Theory]
    [InlineData(900, 5, 1, 0)]
    [InlineData(900, 400, 1, 1)]   // second slower than the limit
    [InlineData(100, 120, 1, 0)]   // both fast (short line on a fast engine): no slower-than claim
    [InlineData(900, 5, 2, 1)]     // two generations for one text
    public void JudgeRepeat_WantsOneGenerationAndAnImmediateSecondAnswer(long first, long second, int starts, int problems)
    {
        Assert.Equal(problems, WrapperHttpStage.JudgeRepeat(first, second, starts).Count());
    }

    [Fact]
    public void JudgeGpuDrop_OnlyJudgesCudaWithEnoughLoad()
    {
        var busy = new E2eGpuStats(3, 9000, 32000, 60, 90);
        var idle = new E2eGpuStats(3, 6000, 32000, 5, 8);
        Assert.Null(WrapperHttpStage.JudgeGpuDrop("cuda", busy, idle, out var note));
        Assert.Contains("60 % (max 90) while generating -> 5 %", note);
        Assert.Equal("GPU load did not drop after both cancels", WrapperHttpStage.JudgeGpuDrop("cuda", busy, busy, out _));
        Assert.Null(WrapperHttpStage.JudgeGpuDrop("cpu", busy, busy, out note));
        Assert.Contains("not judged", note);
        Assert.Null(WrapperHttpStage.JudgeGpuDrop("cuda", idle, idle, out note));
        Assert.Contains("too little load", note);
        Assert.Null(WrapperHttpStage.JudgeGpuDrop("cuda", null, idle, out note));
        Assert.Equal("GPU not sampled", note);
    }

    [Fact]
    public async Task GpuThatKeepsWorkingAfterTheCancel_FailsTheCancelPartOnCuda()
    {
        _wrapper.Device = "cuda";
        var gpu = new FixedGpuProbe(new E2eGpuSample(8000, 32000, 70));

        await Stage(gpu).RunAsync(Ctx(), CancellationToken.None);

        Assert.Contains("GPU load did not drop", _state.ResultOf("wrapper-http:cancel")!.Detail);
    }

    // ------------------------------------------------------------------ fakes

    private sealed class FixedGpuProbe : IE2eGpuProbe
    {
        private readonly E2eGpuSample _sample;
        public FixedGpuProbe(E2eGpuSample sample) => _sample = sample;
        public Task<E2eGpuSample?> SampleAsync(CancellationToken ct) => Task.FromResult<E2eGpuSample?>(_sample);
    }

    internal sealed class FakeLog : IE2eWrapperLog
    {
        private readonly List<string> _lines = new();
        public void Add(string line) { lock (_lines) _lines.Add(line); }
        public long Mark() { lock (_lines) return _lines.Count; }
        public IReadOnlyList<string> Since(long mark) { lock (_lines) return _lines.Skip((int)mark).ToList(); }
    }

    /// <summary>
    /// The wrapper's response cache and refcount in memory (echokrautts <c>response_cache.py</c>): texts with
    /// " cancel-" generate until a client leaves (the rest then get it to the end) or all are gone; every other
    /// generation finishes when its first client reads.
    /// Switches break single rules.
    /// </summary>
    private sealed class FakeWrapper : IE2eWrapperHttp
    {
        public readonly FakeLog Log = new();
        public bool CacheWorks = true, RefcountBroken, StoresCancelled, HasCacheInHealth = true, VcmSendsNoCache = true;
        public string Device = "cpu";
        public int TtsCalls;
        public readonly List<string> Texts = new();
        private int _hits, _shared, _misses, _bypassed, _jobs;
        private readonly Dictionary<string, byte[]> _stored = new();
        private readonly Dictionary<string, Gen> _running = new();
        private readonly Dictionary<string, Gen> _byId = new();
        private readonly object _lock = new();

        internal sealed class Gen
        {
            public string Id = "", Key = "";
            public byte[] Content = [];
            public int Subscribers;
            public string State = "running";
            public bool Long;
            public readonly TaskCompletionSource Ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource ClientLeft = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<E2eWrapperHealth?> HealthAsync(CancellationToken ct)
        {
            lock (_lock)
                return Task.FromResult<E2eWrapperHealth?>(new E2eWrapperHealth(Device, "xtts",
                    HasCacheInHealth ? new E2eCacheStats(true, _stored.Count, _running.Count, _hits, _shared, _misses, _bypassed) : null));
        }

        public Task<IE2eTtsCall?> StartTtsAsync(string sample, string text, bool noCache, CancellationToken ct)
        {
            lock (_lock)
            {
                TtsCalls++;
                Texts.Add(text);
                var key = sample + "|" + text;
                var client = $"c{++_jobs}";
                string how;
                Gen gen;
                if (!noCache && CacheWorks && _stored.TryGetValue(key, out var pcm))
                {
                    _hits++;
                    how = "hit";
                    gen = new Gen { Id = $"g{++_jobs}", Key = key, Content = pcm, State = "done" };
                    gen.Ended.SetResult();
                    Log.Add($"{{\"event\":\"log\",\"message\":\"tts cache hit: job={client} generation={gen.Id}\"}}");
                }
                else if (!noCache && _running.TryGetValue(key, out var run) && run.State == "running")
                {
                    _shared++;
                    how = "shared";
                    gen = run;
                    Log.Add($"{{\"event\":\"log\",\"message\":\"tts cache shared: job={client} generation={gen.Id}\"}}");
                }
                else
                {
                    if (noCache) _bypassed++; else _misses++;
                    how = noCache ? "bypass" : "miss";
                    gen = new Gen { Id = $"g{++_jobs}", Key = key, Content = Encoding.UTF8.GetBytes(key + "#" + _jobs), Long = text.Contains(" cancel-") };
                    _running[key] = gen;
                    _byId[gen.Id] = gen;
                    Log.Add($"{{\"event\":\"log\",\"message\":\"tts request start: job={gen.Id} sample={sample} chars={text.Length}\"}}");
                }
                gen.Subscribers++;
                var call = new Call(this, gen, how, client);
                _byId[client] = gen;
                _clients[client] = call;
                return Task.FromResult<IE2eTtsCall?>(call);
            }
        }

        private readonly Dictionary<string, Call> _clients = new();

        private void Finish(Gen gen, string state)
        {
            if (gen.State != "running") return;
            gen.State = state;
            _running.Remove(gen.Key);
            if (state == "done" || StoresCancelled) _stored[gen.Key] = gen.Content;
            if (state == "done") Log.Add($"{{\"event\":\"log\",\"message\":\"tts request done: job={gen.Id} generated=1.00s\"}}");
            gen.Ended.TrySetResult();
        }

        private void Detach(Gen gen)
        {
            gen.Subscribers--;
            if (gen.Subscribers <= 0 || RefcountBroken) Finish(gen, "cancelled");
            else gen.ClientLeft.TrySetResult(); // the remaining clients keep it running
        }

        public Task<bool> CancelAsync(string jobId, CancellationToken ct)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(jobId, out var call)) return Task.FromResult(false);
                call.Cancel();
                return Task.FromResult(true);
            }
        }

        public Task<string?> JobStateAsync(string jobId, CancellationToken ct)
        {
            lock (_lock) return Task.FromResult(_byId.TryGetValue(jobId, out var g) ? g.State : null);
        }

        public Task<string?> PageAsync(CancellationToken ct) =>
            Task.FromResult<string?>("<script>fetch('/tts', {body: JSON.stringify({no_cache: true})})</script>");

        internal sealed class Call : IE2eTtsCall
        {
            private readonly FakeWrapper _w;
            private readonly Gen _gen;
            private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool _left;

            internal Call(FakeWrapper w, Gen gen, string cache, string jobId)
            {
                _w = w;
                _gen = gen;
                Cache = cache;
                JobId = jobId;
            }

            public string Cache { get; }
            public string JobId { get; }
            public long BytesRead { get; private set; }
            public long? FirstByteMs => BytesRead > 0 ? 0 : null;
            public byte[] Content => _gen.Content;

            public void Cancel()
            {
                if (_left) return;
                _left = true;
                BytesRead = _gen.Content.Length / 2; // left halfway
                _w.Detach(_gen);
                _cancelled.TrySetResult();
            }

            public async Task<string> ReadToEndAsync(CancellationToken ct)
            {
                BytesRead = _gen.Content.Length;
                if (!_gen.Long) lock (_w._lock) _w.Finish(_gen, "done");
                else
                {
                    // A long run ends with the client's own cancel, the end of the generation, or — once another
                    // client left and this one stayed a moment — by running to the end.
                    var left = _gen.ClientLeft.Task.ContinueWith(_ => Task.Delay(100)).Unwrap();
                    await Task.WhenAny(_cancelled.Task, _gen.Ended.Task, left).ConfigureAwait(false);
                    lock (_w._lock) if (!_left) _w.Finish(_gen, "done");
                }
                return Convert.ToHexString(_gen.Content);
            }

            public void Dispose() { }
        }
    }
}
