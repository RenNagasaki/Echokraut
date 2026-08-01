using Echokraut.DataClasses;
using Echokraut.Enums;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Engine-aware active-instance accessor, EchokrauTTS config defaults, and the
/// <c>TtsInstallRoot</c> / <c>BackendSelection</c> migrations. Fresh installs default to
/// EchokrauTTS; the active-instance accessor must mirror whichever engine is selected.
/// </summary>
public class ConfigurationActiveEngineTests
{
    [Fact]
    public void DefaultSelection_IsEchokrauTts_ForFreshInstall()
    {
        var cfg = new Configuration();
        Assert.Equal(TTSBackends.EchokrauTTS, cfg.BackendSelection);
    }

    [Fact]
    public void ActiveInstanceType_MirrorsSelectedEngine()
    {
        var cfg = new Configuration { BackendSelection = TTSBackends.Alltalk };

        cfg.Alltalk.InstanceType = AlltalkInstanceType.Local;
        Assert.Equal(AlltalkInstanceType.Local, cfg.ActiveInstanceType);
        Assert.True(cfg.HasLiveGeneration);

        cfg.Alltalk.InstanceType = AlltalkInstanceType.None;
        Assert.Equal(AlltalkInstanceType.None, cfg.ActiveInstanceType);
        Assert.False(cfg.HasLiveGeneration);
    }

    [Fact]
    public void EchokrauTtsSelected_AccessorFollowsEchokrauTts_NotAllTalk()
    {
        var cfg = new Configuration { BackendSelection = TTSBackends.EchokrauTTS };
        cfg.Alltalk.InstanceType = AlltalkInstanceType.Local;   // must be ignored
        cfg.EchokrauTts.InstanceType = AlltalkInstanceType.Remote;

        Assert.Equal(AlltalkInstanceType.Remote, cfg.ActiveInstanceType);
        Assert.True(cfg.HasLiveGeneration);

        cfg.EchokrauTts.InstanceType = AlltalkInstanceType.None;
        Assert.Equal(AlltalkInstanceType.None, cfg.ActiveInstanceType);
        Assert.False(cfg.HasLiveGeneration); // even though AllTalk is Local
    }

    [Fact]
    public void EchokrauTtsData_Defaults()
    {
        var d = new EchokrauTtsData();
        Assert.Equal("http://127.0.0.1:8765", d.BaseUrl);
        Assert.Equal("/tts", d.TtsPath);
        Assert.Equal("/samples", d.SamplesPath);
        Assert.Equal("/health", d.HealthPath);
        Assert.Equal(AlltalkInstanceType.None, d.InstanceType);
        Assert.False(d.LocalInstall);
        Assert.True(d.AutoStartLocalInstance);
        Assert.False(d.HasLiveGeneration);
        Assert.Equal(EchokrauTtsEngine.XTTS, d.TtsBackend); // default: better quality
        Assert.Equal("xtts", d.TtsBackendArg);
        Assert.False(d.XttsFp16); // default off
        Assert.Equal("false", d.XttsFp16Arg);
    }

    [Theory]
    [InlineData(EchokrauTtsEngine.XTTS, "xtts")]
    [InlineData(EchokrauTtsEngine.F5, "f5")]
    public void TtsBackendArg_IsLowerCasedEngineName(EchokrauTtsEngine engine, string expectedArg)
    {
        var d = new EchokrauTtsData { TtsBackend = engine };
        Assert.Equal(expectedArg, d.TtsBackendArg);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void XttsFp16Arg_IsLowerCaseBoolString(bool enabled, string expectedArg)
    {
        var d = new EchokrauTtsData { XttsFp16 = enabled };
        Assert.Equal(expectedArg, d.XttsFp16Arg);
    }

    // ── BackendSelection default-flip migration ──────────────────────────────

    [Fact]
    public void BackendMigration_ExistingInstall_PinnedToAllTalk()
    {
        // Simulates an old config: field absent in JSON → deserialized as the new EchokrauTTS
        // default, Version still 0, FirstTime already completed.
        var cfg = new Configuration { BackendSelection = TTSBackends.EchokrauTTS, FirstTime = false, Version = 0 };
        cfg.MigrateBackendSelectionForExistingInstalls();

        Assert.Equal(TTSBackends.Alltalk, cfg.BackendSelection);
        Assert.Equal(1, cfg.Version);
    }

    [Fact]
    public void BackendMigration_FreshInstall_KeepsEchokrauTts()
    {
        var cfg = new Configuration { FirstTime = true, Version = 0 };
        cfg.MigrateBackendSelectionForExistingInstalls();

        Assert.Equal(TTSBackends.EchokrauTTS, cfg.BackendSelection);
        Assert.Equal(1, cfg.Version);
    }

    [Fact]
    public void BackendMigration_AlreadyMigrated_RespectsUserChoice()
    {
        // A returning user who deliberately switched to EchokrauTTS after the migration ran.
        var cfg = new Configuration { BackendSelection = TTSBackends.EchokrauTTS, FirstTime = false, Version = 1 };
        cfg.MigrateBackendSelectionForExistingInstalls();

        Assert.Equal(TTSBackends.EchokrauTTS, cfg.BackendSelection); // not forced back to AllTalk
        Assert.Equal(1, cfg.Version);
    }

    [Fact]
    public void BackendMigration_IsIdempotent()
    {
        var cfg = new Configuration { BackendSelection = TTSBackends.EchokrauTTS, FirstTime = false, Version = 0 };
        cfg.MigrateBackendSelectionForExistingInstalls();
        cfg.MigrateBackendSelectionForExistingInstalls(); // second run must not change anything
        Assert.Equal(TTSBackends.Alltalk, cfg.BackendSelection);
        Assert.Equal(1, cfg.Version);
    }

    // ── TtsInstallRoot migration ─────────────────────────────────────────────

    [Fact]
    public void Migrate_CopiesCustomLegacyPath_WhenRootStillDefault()
    {
        var cfg = new Configuration();
        cfg.Alltalk.LocalInstallPath = @"D:\my-tts";
        // TtsInstallRoot is still at default here.
        cfg.MigrateTtsInstallRoot();
        Assert.Equal(@"D:\my-tts", cfg.TtsInstallRoot);
    }

    [Fact]
    public void Migrate_DoesNotOverwriteCustomRoot()
    {
        var cfg = new Configuration { TtsInstallRoot = @"E:\already-set" };
        cfg.Alltalk.LocalInstallPath = @"D:\legacy";
        cfg.MigrateTtsInstallRoot();
        Assert.Equal(@"E:\already-set", cfg.TtsInstallRoot);
    }

    [Fact]
    public void Migrate_DefaultLegacy_LeavesDefaultRoot()
    {
        var cfg = new Configuration(); // both at default
        cfg.MigrateTtsInstallRoot();
        Assert.Equal(Configuration.DefaultTtsInstallRoot, cfg.TtsInstallRoot);
    }

    [Fact]
    public void Migrate_EmptyLegacyPath_FallsBackToDefault()
    {
        var cfg = new Configuration { TtsInstallRoot = "" };
        cfg.Alltalk.LocalInstallPath = "";
        cfg.MigrateTtsInstallRoot();
        Assert.Equal(Configuration.DefaultTtsInstallRoot, cfg.TtsInstallRoot);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var cfg = new Configuration();
        cfg.Alltalk.LocalInstallPath = @"D:\my-tts";
        cfg.MigrateTtsInstallRoot();
        cfg.MigrateTtsInstallRoot(); // second run must not change anything
        Assert.Equal(@"D:\my-tts", cfg.TtsInstallRoot);
    }
}
