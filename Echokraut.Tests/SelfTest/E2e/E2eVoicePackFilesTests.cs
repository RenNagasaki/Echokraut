using System;
using System.IO;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eVoicePackFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ek-pack-" + Guid.NewGuid().ToString("N"));

    public E2eVoicePackFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void CompletePack_Passes()
    {
        Touch("LICENSE.txt");
        Touch("ATTRIBUTION.csv");
        Touch("Male_All_NPC001.wav");
        Touch("Male_All_NPC001.txt");
        Touch(Path.Combine("sub", "Narrator.wav"));
        Touch(Path.Combine("sub", "Narrator.txt"));
        var (problems, detail) = E2eVoicePackFiles.Check(_dir);
        Assert.Empty(problems);
        Assert.Contains("2 .wav, 2 with .txt", detail);
    }

    [Fact]
    public void MissingFiles_AreNamed()
    {
        Touch("Male_All_NPC001.wav");
        var (problems, _) = E2eVoicePackFiles.Check(_dir);
        Assert.Contains("LICENSE.txt missing", problems);
        Assert.Contains("ATTRIBUTION.csv missing", problems);
        Assert.Contains(problems, p => p.StartsWith("1 .wav without .txt: Male_All_NPC001.wav"));
    }

    [Fact]
    public void NoFolder_OrNoWav_Fails()
    {
        Assert.Contains("samples folder missing", E2eVoicePackFiles.Check(Path.Combine(_dir, "nope")).Problems);
        Touch("LICENSE.txt");
        Touch("ATTRIBUTION.csv");
        Assert.Contains("no .wav in the samples folder", E2eVoicePackFiles.Check(_dir).Problems);
    }
}
