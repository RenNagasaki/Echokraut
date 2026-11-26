using System;
using System.IO;
using Echokraut.Services.SelfTest;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public sealed class E2eStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ek-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly E2eStateStore _store;

    public E2eStateStoreTests() => _store = new E2eStateStore(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    internal static E2eState State(string id = "r1") => new()
    {
        Id = id,
        Request = new E2eRequest(id, @"E:\EkE2E", E2eRequest.AllEngines, "https://tts.example.net", null),
        StartedUtc = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc),
        UpdatedUtc = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void DirectoryFor_IsASiblingOfThePluginConfigFolder()
    {
        var pluginDir = Path.Combine("cfg", "pluginConfigs", "Echokraut");
        Assert.Equal(Path.Combine("cfg", "pluginConfigs", "Echokraut-e2e"), E2eStateStore.DirectoryFor(pluginDir));
        Assert.Equal(Path.Combine("cfg", "pluginConfigs", "Echokraut-e2e"),
            E2eStateStore.DirectoryFor(pluginDir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void SaveLoad_RoundTripsEverything()
    {
        var state = State();
        state.Phase = E2ePhase.ResetDone;
        state.RunningStage = "checks";
        state.StageResults.Add(new E2eStageReport("preconditions", SelfTestOutcome.Pass, "ok", 12));
        state.Marks["k"] = "v";
        state.CreatedIds.Add("clip:1");
        state.TouchedOptions["VoiceChat"] = "false";
        state.ConfigIsE2e = true;

        _store.Save(state);
        var loaded = _store.Load()!;

        Assert.Equal(E2ePhase.ResetDone, loaded.Phase);
        Assert.Equal("checks", loaded.RunningStage);
        Assert.Equal(state.StageResults, loaded.StageResults);
        Assert.Equal("v", loaded.Marks["k"]);
        Assert.Equal("clip:1", Assert.Single(loaded.CreatedIds));
        Assert.Equal("false", loaded.TouchedOptions["VoiceChat"]);
        Assert.True(loaded.ConfigIsE2e);
        Assert.Equal(E2eRequest.AllEngines, loaded.Request.Engines);
        Assert.Contains("\"reset-done\"", File.ReadAllText(_store.StatePath));
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        _store.Save(State());
        _store.Save(State());

        Assert.Equal(new[] { E2eStateStore.StateFileName }, Array.ConvertAll(Directory.GetFiles(_dir), Path.GetFileName));
    }

    [Fact]
    public void Load_MissingOrCorrupt_IsNull()
    {
        Assert.Null(_store.Load());
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.StatePath, "{ broken");
        Assert.Null(_store.Load());
    }

    [Fact]
    public void TakeRequest_ReadsAndDeletes()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_store.RequestPath, "{}");

        Assert.Equal("{}", _store.TakeRequest());
        Assert.False(_store.RequestExists);
        Assert.Null(_store.TakeRequest());
    }

    [Fact]
    public void WriteReport_UsesTheIdInTheName()
    {
        var path = _store.WriteReport("{}", "r9");
        Assert.Equal(Path.Combine(_dir, "e2e-r9.json"), path);
        Assert.True(File.Exists(path));
    }
}
