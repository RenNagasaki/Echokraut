using System;
using System.IO;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

/// <summary>The "no audio file on disk" watch, on a private temp folder (snapshot mode: the watcher's events arrive on their own thread).</summary>
public sealed class E2eAudioFileWatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-watch-" + Guid.NewGuid().ToString("N"));

    public E2eAudioFileWatchTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "samples"));
        File.WriteAllBytes(Path.Combine(_root, "samples", "Narrator.wav"), [1, 2, 3]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private E2eAudioFileWatch Start() => E2eAudioFileWatch.Start([("<install>", _root), ("<missing>", Path.Combine(_root, "nope"))], watch: false);

    private static string Unix(string s) => s.Replace('\\', '/');

    [Fact]
    public void NothingWritten_Passes_AndCountsTheExistingAudio()
    {
        var watch = Start();
        File.WriteAllText(Path.Combine(_root, "log.txt"), "not audio");

        var (problems, detail) = watch.Stop();

        Assert.Empty(problems);
        Assert.Equal("no audio file written (1 root(s), 1 audio file(s) before)", detail);
    }

    [Fact]
    public void NewWav_InASubfolder_Fails_WithTheLabelledPath()
    {
        var watch = Start();
        Directory.CreateDirectory(Path.Combine(_root, "tmp"));
        File.WriteAllBytes(Path.Combine(_root, "tmp", "ref.WAV"), [9]);

        var problem = Assert.Single(watch.Stop().Problems);

        Assert.Equal("audio file(s) written during the game-voice part: <install>/tmp/ref.WAV", Unix(problem));
    }

    [Fact]
    public void ExistingWav_Rewritten_Fails()
    {
        var watch = Start();
        File.WriteAllBytes(Path.Combine(_root, "samples", "Narrator.wav"), [1, 2, 3, 4]);

        Assert.Equal("audio file(s) written during the game-voice part: <install>/samples/Narrator.wav", Unix(Assert.Single(watch.Stop().Problems)));
    }

    [Fact]
    public void TransientFile_SeenByTheWatcher_FailsEvenAfterItWasDeleted()
    {
        var watch = Start();
        watch.Record(Path.Combine(_root, "tmpab12.wav"));
        watch.Record(Path.Combine(_root, "model.safetensors"));

        Assert.Equal("audio file(s) written during the game-voice part: <install>/tmpab12.wav", Unix(Assert.Single(watch.Stop().Problems)));
    }

    [Theory]
    [InlineData("a.ogg", true)]
    [InlineData("a.scd", true)]
    [InlineData("a.Flac", true)]
    [InlineData("a.json", false)]
    [InlineData("wav", false)]
    public void IsAudio_ByExtension(string name, bool expected) => Assert.Equal(expected, E2eAudioFileWatch.IsAudio(name));

    [Fact]
    public void RunRoots_WatchesTheParentOfThePluginConfigDir()
    {
        var config = Path.Combine(_root, "pluginConfigs", "Echokraut");
        var roots = E2eAudioFileWatch.RunRoots(@"E:\EkE2E", config);
        Assert.Contains(("<pluginConfigs>", Path.Combine(_root, "pluginConfigs")), roots);
        Assert.Contains(("<install>", @"E:\EkE2E"), roots);
    }
}
