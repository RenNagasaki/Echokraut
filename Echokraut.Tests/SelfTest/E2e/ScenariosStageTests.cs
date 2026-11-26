using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Echokraut.Services.SelfTest.E2e.Scenarios;
using Echokraut.Services.SelfTest.E2e.Stages;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class ScenariosStageTests
{
    private readonly Configuration _config = new() { AutoAdvanceTextAfterSpeechCompleted = true };
    private readonly E2eState _state = E2eStateStoreTests.State();

    public ScenariosStageTests() => _state.ConfigIsE2e = true;

    private sealed class FakeScenario : E2eScenario
    {
        private readonly Func<E2eContext, Task<SelfTestResult>> _run;
        public FakeScenario(string id, Func<E2eContext, Task<SelfTestResult>> run) { Id = id; _run = run; }
        public FakeScenario(string id, SelfTestResult result) : this(id, _ => Task.FromResult(result)) { }
        public override string Id { get; }
        public override TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public int Runs;
        public override Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct) { Runs++; return _run(ctx); }
    }

    private E2eContext Ctx() => new(_state, () => { }, _ => { });

    [Fact]
    public async Task RealConfig_IsSkipped_WithoutRunningAScenario()
    {
        _state.ConfigIsE2e = false;
        var a = new FakeScenario("a", SelfTestResult.Pass(""));
        var r = await new ScenariosStage([a], _config).RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
        Assert.Equal(0, a.Runs);
    }

    [Fact]
    public void Needs_ChecksLocal_AsTheStaticDependency()
    {
        Assert.Equal(new[] { "checks:local" }, new ScenariosStage([], _config).Needs);
    }

    [Fact]
    public async Task EachScenario_IsReportedOnItsOwn_OneFailFailsTheStage()
    {
        var seenAutoAdvance = new List<bool>();
        var a = new FakeScenario("a", _ => { seenAutoAdvance.Add(_config.AutoAdvanceTextAfterSpeechCompleted); return Task.FromResult(SelfTestResult.Pass("fine")); });
        var b = new FakeScenario("b", SelfTestResult.Fail("broken"));
        var c = new FakeScenario("c", SelfTestResult.Skip("n/a"));

        var r = await new ScenariosStage([a, b, c], _config).RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.Contains("scenarios:b", r.Detail);
        Assert.Equal(new[] { "scenarios:a", "scenarios:b", "scenarios:c" }, _state.StageResults.Select(s => s.Id));
        Assert.Equal("fine", _state.ResultOf("scenarios:a")!.Detail);
        Assert.Equal(new[] { false }, seenAutoAdvance);
        Assert.True(_config.AutoAdvanceTextAfterSpeechCompleted);
        Assert.Empty(_state.TouchedOptions);
    }

    [Fact]
    public async Task RestartedStage_KeepsThePartsItAlreadyReported()
    {
        _state.StageResults.Add(new E2eStageReport("scenarios:a", SelfTestOutcome.Pass, "earlier", 1));
        var a = new FakeScenario("a", SelfTestResult.Fail("must not run"));
        var b = new FakeScenario("b", SelfTestResult.Pass(""));

        var r = await new ScenariosStage([a, b], _config).RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Pass, r.Outcome);
        Assert.Equal(0, a.Runs);
        Assert.Equal(1, b.Runs);
        Assert.Contains("2 passed", r.Detail);
    }

    [Fact]
    public async Task OptionLeftByAThrowingScenario_IsRestoredBeforeTheNext()
    {
        bool? seen = null;
        var a = new FakeScenario("a", ctx =>
        {
            E2eOptions.Set(ctx.State, _config, nameof(Configuration.VoiceChat), true);
            throw new InvalidOperationException("boom");
        });
        var b = new FakeScenario("b", _ => { seen = _config.VoiceChat; return Task.FromResult(SelfTestResult.Pass("")); });

        var r = await new ScenariosStage([a, b], _config).RunAsync(Ctx(), CancellationToken.None);

        Assert.Equal(SelfTestOutcome.Fail, r.Outcome);
        Assert.False(seen);
        Assert.Equal(SelfTestOutcome.Fail, _state.ResultOf("scenarios:a")!.Outcome);
    }

    [Fact]
    public async Task OptionsLeftFromAnUnloadedRun_AreRestoredAtStart()
    {
        _config.JoinSplitSentences = true; // in memory after the reload: what the crashed scenario set
        _state.TouchedOptions[nameof(Configuration.JoinSplitSentences)] = "false";
        bool? seen = null;
        var a = new FakeScenario("a", _ => { seen = _config.JoinSplitSentences; return Task.FromResult(SelfTestResult.Pass("")); });

        await new ScenariosStage([a], _config).RunAsync(Ctx(), CancellationToken.None);

        Assert.False(seen);
    }

    [Fact]
    public async Task OnlySkippedParts_SkipTheStage()
    {
        var r = await new ScenariosStage([new FakeScenario("a", SelfTestResult.Skip(""))], _config)
            .RunAsync(Ctx(), CancellationToken.None);
        Assert.Equal(SelfTestOutcome.Skipped, r.Outcome);
    }
}

public class E2eOptionsTests
{
    [Fact]
    public void Set_RecordsTheOldValueOnce_RestorePutsItBack()
    {
        var state = E2eStateStoreTests.State();
        var config = new Configuration { VoiceChat = false, LocalSaveLocation = @"C:\a" };

        E2eOptions.Set(state, config, nameof(Configuration.VoiceChat), true);
        E2eOptions.Set(state, config, nameof(Configuration.VoiceChat), true);
        E2eOptions.Set(state, config, nameof(Configuration.LocalSaveLocation), @"E:\b");
        Assert.True(config.VoiceChat);
        Assert.Equal("false", state.TouchedOptions[nameof(Configuration.VoiceChat)]);

        var restored = E2eOptions.Restore(state, config, [nameof(Configuration.VoiceChat)]);
        Assert.Equal(new[] { nameof(Configuration.VoiceChat) }, restored);
        Assert.False(config.VoiceChat);
        Assert.Equal(@"E:\b", config.LocalSaveLocation);

        E2eOptions.Restore(state, config);
        Assert.Equal(@"C:\a", config.LocalSaveLocation);
        Assert.Empty(state.TouchedOptions);
    }

    [Fact]
    public void UnknownOption_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            E2eOptions.Set(E2eStateStoreTests.State(), new Configuration(), "NoSuchOption", true));
    }
}
