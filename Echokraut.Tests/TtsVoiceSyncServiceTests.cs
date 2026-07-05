using System;
using System.IO;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Services;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Engine voice-sync: folder mapping, copy-on-switch semantics, switch orchestration.</summary>
public class TtsVoiceSyncServiceTests : IDisposable
{
    private readonly string _root;

    public TtsVoiceSyncServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ek_sync_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    private static TtsVoiceSyncService Build(Configuration cfg, out Mock<IBackendService> backend)
    {
        backend = new Mock<IBackendService>();
        return new TtsVoiceSyncService(cfg, new Mock<ILogService>().Object, backend.Object);
    }

    [Fact]
    public void VoicesFolderFor_MapsEachEngine()
    {
        Assert.Equal(TtsPaths.AllTalkVoices(_root),
            TtsVoiceSyncService.VoicesFolderFor(TTSBackends.Alltalk, _root));
        Assert.Equal(TtsPaths.EchokrauTtsSamples(_root),
            TtsVoiceSyncService.VoicesFolderFor(TTSBackends.EchokrauTTS, _root));
    }

    [Fact]
    public void CopyVoicesForSwitch_AllTalkToEchokrauTts_OverwritesSameNamed_KeepsExtras()
    {
        var cfg = new Configuration { TtsInstallRoot = _root };
        var src = TtsPaths.AllTalkVoices(_root);
        var dst = TtsPaths.EchokrauTtsSamples(_root);
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(src, "Female_Hyur_Iceheart.wav"), "new");
        File.WriteAllText(Path.Combine(dst, "Female_Hyur_Iceheart.wav"), "old"); // overwritten
        File.WriteAllText(Path.Combine(dst, "Custom_Voice.wav"), "keep");        // extra kept

        var svc = Build(cfg, out _);
        var copied = svc.CopyVoicesForSwitch(TTSBackends.Alltalk, TTSBackends.EchokrauTTS);

        Assert.Equal(1, copied);
        Assert.Equal("new", File.ReadAllText(Path.Combine(dst, "Female_Hyur_Iceheart.wav")));
        Assert.True(File.Exists(Path.Combine(dst, "Custom_Voice.wav")));
    }

    [Fact]
    public void CopyVoicesForSwitch_SameEngine_IsNoOp()
    {
        var cfg = new Configuration { TtsInstallRoot = _root };
        var svc = Build(cfg, out _);
        Assert.Equal(0, svc.CopyVoicesForSwitch(TTSBackends.Alltalk, TTSBackends.Alltalk));
    }

    [Fact]
    public void CopyVoicesForSwitch_MissingSource_IsNoOp()
    {
        var cfg = new Configuration { TtsInstallRoot = _root }; // no folders created
        var svc = Build(cfg, out _);
        Assert.Equal(0, svc.CopyVoicesForSwitch(TTSBackends.EchokrauTTS, TTSBackends.Alltalk));
    }

    [Fact]
    public void SwitchEngine_SameEngine_DoesNothing()
    {
        var cfg = new Configuration { TtsInstallRoot = _root, BackendSelection = TTSBackends.Alltalk };
        var svc = Build(cfg, out var backend);

        svc.SwitchEngine(TTSBackends.Alltalk);

        Assert.Equal(TTSBackends.Alltalk, cfg.BackendSelection);
        backend.Verify(b => b.CancelAll(), Times.Never);
        backend.Verify(b => b.SetBackendType(It.IsAny<TTSBackends>()), Times.Never);
    }

    [Fact]
    public void SwitchEngine_DifferentEngine_FlushesSetsAndReconnects()
    {
        var cfg = new Configuration { TtsInstallRoot = _root, BackendSelection = TTSBackends.Alltalk };
        var svc = Build(cfg, out var backend);

        svc.SwitchEngine(TTSBackends.EchokrauTTS);

        Assert.Equal(TTSBackends.EchokrauTTS, cfg.BackendSelection);
        backend.Verify(b => b.CancelAll(), Times.Once);
        backend.Verify(b => b.SetBackendType(TTSBackends.EchokrauTTS), Times.Once);
    }
}
