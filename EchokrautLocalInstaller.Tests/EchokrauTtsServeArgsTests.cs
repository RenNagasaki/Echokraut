using System.Linq;
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

    [Fact]
    public void QwenOptions_AreForwardedWhenSent()
    {
        var args = WrapperArgs.Concat(new[] { "0.7" }).ToArray();
        var list = EchokrauTtsServeArgs.FromWrapperMode(args).BootstrapArguments(includeParentPid: true);

        Assert.Equal(new[] { "--qwen-temperature", "0.7" }, list.Skip(list.Count - 2));
        // The speaker-embedding-only option was removed (2026-11-15); never forward it again.
        Assert.DoesNotContain("--qwen-xvec-only", list);
    }

    [Fact]
    public void QwenOptions_AreOmittedForAnOlderPlugin()
    {
        // An older plugin sends nine arguments: the wrapper must keep its own Qwen defaults.
        var list = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs).BootstrapArguments(includeParentPid: true);

        Assert.DoesNotContain("--qwen-temperature", list);
    }

    [Fact]
    public void QwenOptions_ComeFromTheCustomDataTailToo()
    {
        string[] args =
        {
            "installcustomdataek", @"C:\LocalTTS", "", "", "False", "True", "8765", "de", "280",
            "qwen-0.6b", "false", "0.9",
        };
        var serve = EchokrauTtsServeArgs.FromCustomDataMode(args);

        Assert.Equal("qwen-0.6b", serve.TtsBackend);
        Assert.Equal("0.9", serve.QwenTemperature);
    }

    [Fact]
    public void PocketDecodeSteps_IsForwardedWhenSent()
    {
        var args = WrapperArgs.Concat(new[] { "0.9", "8" }).ToArray();
        var list = EchokrauTtsServeArgs.FromWrapperMode(args).BootstrapArguments(includeParentPid: false);

        Assert.Equal(new[] { "--pocket-decode-steps", "8" }, list.Skip(list.Count - 2));
        Assert.DoesNotContain("--pocket-decode-steps",
            EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs).BootstrapArguments(includeParentPid: false));
    }

    [Fact]
    public void PocketLayers_IsForwardedWhenSent_InBothModes()
    {
        var wrapper = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs.Concat(new[] { "0.9", "4", "6" }).ToArray());
        var list = wrapper.BootstrapArguments(includeParentPid: false);
        Assert.Equal(new[] { "--pocket-decode-steps", "4", "--pocket-layers", "6" }, list.Skip(list.Count - 4));
        Assert.Equal("6", wrapper.PocketLayers);
        Assert.DoesNotContain("--pocket-layers",
            EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs).BootstrapArguments(includeParentPid: false));

        string[] custom =
        {
            "installcustomdataek", @"C:\LocalTTS", "", "", "False", "True", "8765", "de", "280",
            "pocket", "false", "0.9", "8", "24",
        };
        var serve = EchokrauTtsServeArgs.FromCustomDataMode(custom);
        Assert.Equal("8", serve.PocketDecodeSteps);
        Assert.Equal("24", serve.PocketLayers);
    }

    [Fact]
    public void ResponseCacheMinutes_IsForwardedWhenSent_InBothModes()
    {
        var wrapper = EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs.Concat(new[] { "0.9", "4", "24", "0" }).ToArray());
        var list = wrapper.BootstrapArguments(includeParentPid: false);
        Assert.Equal(new[] { "--response-cache-minutes", "0" }, list.Skip(list.Count - 2));

        // Not sent (older plugin, or cache left on): the wrapper's config.json decides.
        Assert.DoesNotContain("--response-cache-minutes",
            EchokrauTtsServeArgs.FromWrapperMode(WrapperArgs.Concat(new[] { "0.9", "4", "24", "" }).ToArray())
                .BootstrapArguments(includeParentPid: false));

        string[] custom =
        {
            "installcustomdataek", @"C:\LocalTTS", "", "", "False", "True", "8765", "de", "280",
            "pocket", "false", "0.9", "8", "24", "0",
        };
        Assert.Equal("0", EchokrauTtsServeArgs.FromCustomDataMode(custom).ResponseCacheMinutes);
    }
}
