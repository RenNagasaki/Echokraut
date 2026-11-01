using Xunit;

namespace EchokrautLocalInstaller.Tests;

public class EchokrauTtsServeArgsTests
{
    private static readonly string[] WrapperArgs =
        { "echokrautts", @"C:\LocalTTS", "https://x/EchokrauTTS.zip", "False", "8765", "en", "280", "xtts", "false" };

    [Fact]
    public void FromWrapperMode_ReadsPositionalArguments()
    {
        var serve = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs);

        Assert.Equal(@"C:\LocalTTS", serve.InstallRoot);
        Assert.Equal("8765", serve.Port);
        Assert.Equal("280", serve.ParentPid);
        Assert.False(serve.PreserveUserData);
    }

    [Fact]
    public void FromWrapperMode_UpdateModePreservesUserData()
    {
        var args = (string[])WrapperArgs.Clone();
        args[0] = EchokrauTtsServeArgs.UpdateMode;

        Assert.True(EchokrauTtsServeArgs.FromWrapperMode(args).PreserveUserData);
    }

    [Fact]
    public void FromWrapperMode_MissingOptionalArgsFallBack()
    {
        var serve = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs[..7]);

        Assert.Equal(Constants.DEFAULTTTSBACKEND, serve.TtsBackend);
        Assert.Equal(Constants.DEFAULTXTTSFP16, serve.XttsFp16);
    }

    [Fact]
    public void BootstrapArguments_WithParentPid()
    {
        var args = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs).BootstrapArguments(includeParentPid: true);

        Assert.Equal(new[]
        {
            "--host", "127.0.0.1", "--port", "8765", "--language", "en", "--parent-pid", "280",
            "--tts-backend", "xtts", "--xtts-fp16", "false",
        }, args);
    }

    [Fact]
    public void BootstrapArguments_WithoutParentPidUnderWine()
    {
        // The Wine PID means nothing to the native wrapper; its watchdog would shut down immediately.
        var args = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs).BootstrapArguments(includeParentPid: false);

        Assert.DoesNotContain("--parent-pid", args);
        Assert.DoesNotContain("280", args);
        Assert.Contains("--tts-backend", args);
    }
}
