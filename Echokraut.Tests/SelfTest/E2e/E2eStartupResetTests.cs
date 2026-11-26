using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>Phase B of backup + reset, against a temp <c>pluginConfigs</c> folder.</summary>
public sealed class E2eStartupResetTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 22, 10, 5, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-e2e-reset-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _info = new();
    private readonly List<string> _warn = new();

    private string ConfigFile => Path.Combine(_root, "Echokraut.json");
    private string ConfigDir => Path.Combine(_root, "Echokraut");
    private E2eStateStore Store => new(E2eStateStore.DirectoryFor(ConfigDir));

    public E2eStartupResetTests()
    {
        Directory.CreateDirectory(Path.Combine(ConfigDir, "selftest"));
        Directory.CreateDirectory(Path.Combine(ConfigDir, "sub"));
        File.WriteAllText(ConfigFile, "{\"FirstTime\":false}");
        File.WriteAllText(Path.Combine(ConfigDir, "echokraut.db"), "db");
        File.WriteAllText(Path.Combine(ConfigDir, "echokraut.db-wal"), "wal");
        File.WriteAllText(Path.Combine(ConfigDir, "sub", "a.txt"), "a");
        File.WriteAllText(Path.Combine(ConfigDir, "selftest", "selftest-latest.json"), "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp */ }
    }

    private E2eState Pending(E2ePhase phase = E2ePhase.ResetPending, string? requestedBy = "r1")
    {
        var s = E2eStateStoreTests.State();
        s.Phase = phase;
        s.UpdatedUtc = Now.AddMinutes(-1);
        if (requestedBy != null) s.Marks[E2eStartupReset.ResetRequestedMark] = requestedBy;
        Store.Save(s);
        return s;
    }

    private E2eStartupReset.Outcome Run(bool isDev = true, Action<string, string>? copy = null) =>
        E2eStartupReset.RunIfPending(isDev, ConfigFile, ConfigDir, _info.Add, _warn.Add, () => Now, copy);

    private string[] RealFiles() => new[] { ConfigFile, Path.Combine(ConfigDir, "echokraut.db"),
        Path.Combine(ConfigDir, "echokraut.db-wal"), Path.Combine(ConfigDir, "sub", "a.txt") };

    [Fact]
    public void Pending_BacksUpWipesAndKeepsSelftest()
    {
        Pending();

        Assert.Equal(E2eStartupReset.Outcome.ResetDone, Run());

        var state = Store.Load()!;
        Assert.Equal(E2ePhase.ResetDone, state.Phase);
        Assert.True(state.ConfigIsE2e);
        Assert.Equal("4", state.Marks[E2eStartupReset.BackupFilesMark]);
        Assert.False(File.Exists(ConfigFile));
        Assert.Equal(new[] { "selftest" }, Directory.EnumerateFileSystemEntries(ConfigDir).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(ConfigDir, "selftest", "selftest-latest.json")));

        var backup = state.BackupDir!;
        Assert.StartsWith(Path.Combine(_root, "Echokraut-e2e", "backup-20261122T100500Z"), backup);
        Assert.Equal("a", File.ReadAllText(Path.Combine(backup, "Echokraut", "sub", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(backup, "Echokraut", "selftest")));
        Assert.True(File.Exists(Path.Combine(backup, E2eStartupReset.RestoreScriptName)));

        var manifest = E2eStartupReset.ReadManifest(backup)!;
        Assert.Equal("r1", manifest.Id);
        Assert.Equal(4, manifest.Files.Count);
        foreach (var f in manifest.Files)
            Assert.Equal(E2eStartupReset.Sha256(Path.Combine(backup, f.Path)), f.Sha256);
        Assert.Contains(manifest.Files, f => f.Path == "Echokraut.json");
        Assert.Contains("EKTEST r1 backup progress", _info[0]);
    }

    [Fact]
    public void HashMismatch_NoWipe_BackToRequested()
    {
        Pending();

        var outcome = Run(copy: (s, d) => File.WriteAllText(d, "corrupt"));

        Assert.Equal(E2eStartupReset.Outcome.BackupFailed, outcome);
        foreach (var f in RealFiles()) Assert.True(File.Exists(f), f);
        var state = Store.Load()!;
        Assert.Equal(E2ePhase.Requested, state.Phase);
        Assert.Contains("SHA-256", state.Marks[E2eStartupReset.BackupErrorMark]);
        Assert.False(state.Marks.ContainsKey(E2eStartupReset.ResetRequestedMark));
        Assert.False(state.ConfigIsE2e);
    }

    [Fact]
    public void WipeStarted_ResumesWithoutASecondBackup()
    {
        var s = Pending(E2ePhase.WipeStarted);
        s.BackupDir = Path.Combine(_root, "Echokraut-e2e", "backup-old");
        Store.Save(s);

        Assert.Equal(E2eStartupReset.Outcome.ResetDone, Run());

        Assert.False(File.Exists(ConfigFile));
        Assert.False(Directory.Exists(Path.Combine(_root, "Echokraut-e2e")) &&
                     Directory.EnumerateDirectories(Path.Combine(_root, "Echokraut-e2e"), "backup-2*").Any());
        Assert.Equal(s.BackupDir, Store.Load()!.BackupDir);
    }

    [Theory]
    [InlineData(false, "r1")]   // installed plugin
    [InlineData(true, "other")] // reset requested by another run
    [InlineData(true, null)]    // no request mark at all
    public void NotDevOrIdMismatch_NoFileTouched(bool isDev, string? requestedBy)
    {
        Pending(requestedBy: requestedBy);

        var outcome = Run(isDev);

        Assert.NotEqual(E2eStartupReset.Outcome.ResetDone, outcome);
        foreach (var f in RealFiles()) Assert.True(File.Exists(f), f);
        Assert.Equal(E2ePhase.ResetPending, Store.Load()!.Phase);
        Assert.Empty(Directory.EnumerateDirectories(Store.Directory));
    }

    [Fact]
    public void StaleState_NoFileTouched()
    {
        var s = Pending();
        s.UpdatedUtc = Now - E2eRunService.StaleAfter - TimeSpan.FromMinutes(1);
        Store.Save(s);

        Assert.Equal(E2eStartupReset.Outcome.Refused, Run());
        foreach (var f in RealFiles()) Assert.True(File.Exists(f), f);
    }

    [Theory]
    [InlineData(E2ePhase.Requested)]
    [InlineData(E2ePhase.ResetDone)]
    [InlineData(E2ePhase.Stages)]
    [InlineData(E2ePhase.RestoreDone)]
    [InlineData(E2ePhase.Finished)]
    public void OtherPhases_Nothing(E2ePhase phase)
    {
        Pending(phase);
        Assert.Equal(E2eStartupReset.Outcome.Nothing, Run());
        foreach (var f in RealFiles()) Assert.True(File.Exists(f), f);
    }

    [Fact]
    public void NoState_Nothing() => Assert.Equal(E2eStartupReset.Outcome.Nothing, Run());

    // --- restore (same ctor slot, other direction) --------------------------------------------

    /// <summary>Real files → reset (backup + wipe) → an E2E config on disk → restore requested.</summary>
    private E2eState ResetThenRequestRestore()
    {
        Pending();
        Assert.Equal(E2eStartupReset.Outcome.ResetDone, Run());
        File.WriteAllText(ConfigFile, "{\"FirstTime\":true}");
        File.WriteAllText(Path.Combine(ConfigDir, "echokraut.db"), "e2e-db");
        File.WriteAllText(Path.Combine(ConfigDir, "echokraut.db-shm"), "e2e-shm");
        var s = Store.Load()!;
        s.Phase = E2ePhase.RestorePending;
        s.Marks[E2eStartupReset.RestoreRequestedMark] = s.Id;
        s.UpdatedUtc = Now.AddMinutes(-1);
        Store.Save(s);
        _info.Clear();
        return s;
    }

    [Fact]
    public void Restore_PutsTheBackupBack_KeepsSelftest_ClearsConfigIsE2e()
    {
        ResetThenRequestRestore();

        Assert.Equal(E2eStartupReset.Outcome.RestoreDone, Run());

        Assert.Equal("{\"FirstTime\":false}", File.ReadAllText(ConfigFile));
        Assert.Equal("db", File.ReadAllText(Path.Combine(ConfigDir, "echokraut.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(ConfigDir, "echokraut.db-wal")));
        Assert.Equal("a", File.ReadAllText(Path.Combine(ConfigDir, "sub", "a.txt")));
        Assert.False(File.Exists(Path.Combine(ConfigDir, "echokraut.db-shm")));
        Assert.True(File.Exists(Path.Combine(ConfigDir, "selftest", "selftest-latest.json")));
        var state = Store.Load()!;
        Assert.Equal(E2ePhase.RestoreDone, state.Phase);
        Assert.False(state.ConfigIsE2e);
        Assert.Equal("4", state.Marks[E2eStartupReset.RestoreFilesMark]);
        Assert.Contains("EKTEST r1 restore progress 4 files restored", _info[0]);
    }

    [Fact]
    public void Restore_BackupFileChanged_TouchesNothing()
    {
        var s = ResetThenRequestRestore();
        File.WriteAllText(Path.Combine(s.BackupDir!, "Echokraut", "sub", "a.txt"), "tampered");

        Assert.Equal(E2eStartupReset.Outcome.RestoreFailed, Run());

        Assert.Equal("{\"FirstTime\":true}", File.ReadAllText(ConfigFile)); // E2E config untouched
        var state = Store.Load()!;
        Assert.Equal(E2ePhase.Stages, state.Phase);
        Assert.True(state.ConfigIsE2e);
        Assert.Contains("SHA-256", state.Marks[E2eStartupReset.RestoreErrorMark]);
        Assert.False(state.Marks.ContainsKey(E2eStartupReset.RestoreRequestedMark));
    }

    [Fact]
    public void Restore_CopyFailsAfterTheWipe_StaysStarted_NextLoadResumes()
    {
        ResetThenRequestRestore();

        Assert.Equal(E2eStartupReset.Outcome.RestoreFailed, Run(copy: (_, d) => File.WriteAllText(d, "corrupt")));
        var state = Store.Load()!;
        Assert.Equal(E2ePhase.RestoreStarted, state.Phase);
        Assert.True(state.ConfigIsE2e);

        Assert.Equal(E2eStartupReset.Outcome.RestoreDone, Run());
        Assert.Equal("{\"FirstTime\":false}", File.ReadAllText(ConfigFile));
        Assert.False(Store.Load()!.Marks.ContainsKey(E2eStartupReset.RestoreErrorMark));
    }

    [Fact]
    public void Restore_WithoutItsRequestMark_NoFileTouched()
    {
        var s = ResetThenRequestRestore();
        s.Marks.Remove(E2eStartupReset.RestoreRequestedMark);
        Store.Save(s);

        Assert.Equal(E2eStartupReset.Outcome.Refused, Run());
        Assert.Equal("{\"FirstTime\":true}", File.ReadAllText(ConfigFile));
    }

    [Fact]
    public void VerifiedBackup_ManifestPathLeavingPluginConfigs_Throws()
    {
        var backup = Path.Combine(_root, "Echokraut-e2e", "backup-x");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, E2eStartupReset.ManifestFileName),
            "{\"id\":\"r1\",\"createdUtc\":\"2026-11-22T10:00:00Z\",\"files\":[{\"path\":\"../evil.txt\",\"sha256\":\"00\",\"size\":1}]}");

        var ex = Assert.Throws<IOException>(() => E2eStartupReset.VerifiedBackup(backup, ConfigDir));
        Assert.Contains("leaves", ex.Message);
    }

    [Fact]
    public void RestoreScript_IsAsciiAndGuardsTheGame()
    {
        var script = E2eRestoreScript.Build("r1");

        Assert.All(script, c => Assert.True(c < 128, $"non-ASCII {(int)c}"));
        Assert.DoesNotContain("&&", script);
        Assert.DoesNotContain("??", script);
        foreach (var p in new[] { "ffxiv_dx11", "XIVLauncher", "Dalamud.Injector", "'selftest'", "Get-FileHash", "state.json" })
            Assert.Contains(p, script);
        Assert.Contains("\r\n", script);
        Assert.Equal(Encoding.ASCII.GetByteCount(script), script.Length);
    }
}
