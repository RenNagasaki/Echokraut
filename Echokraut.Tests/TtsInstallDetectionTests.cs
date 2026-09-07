using System;
using System.IO;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Per-engine install detection via on-disk markers.</summary>
public class TtsInstallDetectionTests : IDisposable
{
    private readonly string _root;

    public TtsInstallDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ek_detect_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AllTalk_DetectedOnlyWhenScriptPresent()
    {
        Assert.False(TtsInstallDetection.IsAllTalkInstalled(_root));
        var dir = TtsPaths.AllTalkRoot(_root);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "script.py"), "");
        Assert.True(TtsInstallDetection.IsAllTalkInstalled(_root));
    }

    [Fact]
    public void EchokrauTts_DetectedOnlyWhenModelMarkerPresent()
    {
        Assert.False(TtsInstallDetection.IsEchokrauTtsInstalled(_root));
        var state = Path.Combine(TtsPaths.EchokrauTtsRoot(_root), ".state");
        Directory.CreateDirectory(state);
        File.WriteAllText(Path.Combine(state, "model.done"), "");
        Assert.True(TtsInstallDetection.IsEchokrauTtsInstalled(_root));
    }

    [Fact]
    public void Detection_IndependentBetweenEngines()
    {
        Directory.CreateDirectory(TtsPaths.AllTalkRoot(_root));
        File.WriteAllText(Path.Combine(TtsPaths.AllTalkRoot(_root), "script.py"), "");
        Assert.True(TtsInstallDetection.IsAllTalkInstalled(_root));
        Assert.False(TtsInstallDetection.IsEchokrauTtsInstalled(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Detection_EmptyRoot_IsFalse(string? root)
    {
        Assert.False(TtsInstallDetection.IsAllTalkInstalled(root!));
        Assert.False(TtsInstallDetection.IsEchokrauTtsInstalled(root!));
    }
}
