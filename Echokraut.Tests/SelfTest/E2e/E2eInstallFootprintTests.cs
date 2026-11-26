using System;
using System.IO;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eInstallFootprintTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ek-footprint-" + Guid.NewGuid().ToString("N"));
    private readonly E2eInstallFootprint _fp = new();

    private string Wrapper => Path.Combine(_root, "echokrautts");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void MissingRoot_IsUnknown()
    {
        Assert.Null(_fp.SizeBytes(_root));
        Assert.Empty(_fp.Venvs(_root));
        Assert.Null(_fp.F5InClassicVenv(_root));
    }

    [Fact]
    public void Size_SumsEveryFile()
    {
        Directory.CreateDirectory(Path.Combine(Wrapper, "models"));
        File.WriteAllBytes(Path.Combine(Wrapper, "a.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(Wrapper, "models", "b.bin"), new byte[32]);
        Assert.Equal(42, _fp.SizeBytes(_root));
    }

    [Fact]
    public void Venvs_And_F5_WindowsLayout()
    {
        Directory.CreateDirectory(Path.Combine(Wrapper, ".venv", "Lib", "site-packages", "TTS"));
        Directory.CreateDirectory(Path.Combine(Wrapper, ".venv-qwen"));
        Directory.CreateDirectory(Path.Combine(Wrapper, "src"));
        Assert.Equal(new[] { ".venv", ".venv-qwen" }, _fp.Venvs(_root));
        Assert.False(_fp.F5InClassicVenv(_root));

        Directory.CreateDirectory(Path.Combine(Wrapper, ".venv", "Lib", "site-packages", "f5_tts"));
        Assert.True(_fp.F5InClassicVenv(_root));
    }

    [Fact]
    public void F5_PosixLayout()
    {
        Directory.CreateDirectory(Path.Combine(Wrapper, ".venv", "lib", "python3.11", "site-packages", "f5_tts"));
        Assert.True(_fp.F5InClassicVenv(_root));
    }
}
