using System.Text;
using Xunit;

namespace EchokrautLocalInstaller.Tests;

public class WineLaunchTests
{
    [Fact]
    public void BuildArgsFile_PutsScriptFirstOneArgumentPerLine()
    {
        var content = WineLaunch.BuildArgsFile("/home/u/drive_c/LocalTTS/echokrautts/bootstrap/install_linux.sh",
            new[] { "--port", "8765" });

        Assert.Equal("/home/u/drive_c/LocalTTS/echokrautts/bootstrap/install_linux.sh\n--port\n8765\n", content);
    }

    [Fact]
    public void BuildArgsFile_KeepsSpacesInsideAnArgument()
    {
        var content = WineLaunch.BuildArgsFile("/home/my user/install_linux.sh", Array.Empty<string>());

        Assert.Equal("/home/my user/install_linux.sh\n", content);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    public void BuildArgsFile_RejectsLineBreaks(string arg)
    {
        Assert.Throws<ArgumentException>(() => WineLaunch.BuildArgsFile("/x.sh", new[] { arg }));
    }

    [Fact]
    public void StartArguments_RunsBashThroughStartUnix()
    {
        Assert.Equal(new[] { "/unix", "/bin/bash", "/r/launch_linux.sh", "/r" },
            WineLaunch.StartArguments("/r/launch_linux.sh", "/r"));
    }

    [Theory]
    [InlineData("0\n", 0)]
    [InlineData("143", 143)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void ParseExitCode_ReadsCodeOrNullWhileUnwritten(string content, int? expected)
    {
        Assert.Equal(expected, WineLaunch.ParseExitCode(content));
    }

    [Fact]
    public void ToLf_ConvertsCrlf()
    {
        Assert.Equal("#!/bin/bash\necho hi\n", WineLaunch.ToLf("#!/bin/bash\r\necho hi\r\n"));
    }

    [Fact]
    public void SplitCompleteLines_HoldsBackUnfinishedLine()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"event\":\"progress\"}\nhalf a li");

        var lines = WineLaunch.SplitCompleteLines(bytes, out var consumed);

        Assert.Equal(new[] { "{\"event\":\"progress\"}" }, lines);
        Assert.Equal(Encoding.UTF8.GetByteCount("{\"event\":\"progress\"}\n"), consumed);
    }

    [Fact]
    public void SplitCompleteLines_NothingWithoutNewline()
    {
        var lines = WineLaunch.SplitCompleteLines(Encoding.UTF8.GetBytes("no newline yet"), out var consumed);

        Assert.Empty(lines);
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void SplitCompleteLines_StripsCrAndKeepsUtf8()
    {
        var lines = WineLaunch.SplitCompleteLines(Encoding.UTF8.GetBytes("Stimme für Ä\r\nzwei\n"), out _);

        Assert.Equal(new[] { "Stimme für Ä", "zwei" }, lines);
    }

    [Fact]
    public void SplitCompleteLines_DoesNotCutMultiByteCharacter()
    {
        var full = Encoding.UTF8.GetBytes("a\nä");
        // Chunk ends between the two bytes of 'ä'.
        var lines = WineLaunch.SplitCompleteLines(full.AsSpan(0, full.Length - 1), out var consumed);

        Assert.Equal(new[] { "a" }, lines);
        Assert.Equal(2, consumed);
    }

    [Fact]
    public void ToUnixPath_NullOutsideWine()
    {
        // The test host runs on plain Windows, where kernel32 has no wine_get_unix_file_name export.
        if (!OperatingSystem.IsWindows()) return;

        Assert.Null(WineLaunch.ToUnixPath(@"C:\LocalTTS"));
    }

    [Fact]
    public void LaunchScript_IsEmbedded()
    {
        using var stream = typeof(WineLaunch).Assembly.GetManifestResourceStream(WineLaunch.ScriptResourceName);

        Assert.NotNull(stream);
        var text = new StreamReader(stream!).ReadToEnd();
        Assert.StartsWith("#!/usr/bin/env bash", text);
        // Every file the installer relies on must be part of the script's protocol.
        foreach (var file in WineLaunch.RunFiles)
            Assert.Contains($"$RUN/{file}", text);
    }
}
