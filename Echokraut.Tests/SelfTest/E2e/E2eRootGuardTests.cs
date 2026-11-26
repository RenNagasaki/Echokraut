using System;
using System.Collections.Generic;
using System.Linq;
using Echokraut.Services.SelfTest.E2e;
using Xunit;

namespace Echokraut.Tests.SelfTest.E2e;

public class E2eRootGuardTests
{
    /// <summary>A file system made of folder → entry names, plus link targets.</summary>
    internal sealed class FakeFs : IE2eRootFileSystem
    {
        public Dictionary<string, List<string>> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => Folders.ContainsKey(path);
        public IReadOnlyList<string> EntryNames(string path) => Folders.TryGetValue(path, out var e) ? e : [];

        public string ResolveLinks(string path)
        {
            foreach (var (link, target) in Links)
                if (path.StartsWith(link, StringComparison.OrdinalIgnoreCase))
                    return target + path[link.Length..];
            return path;
        }
    }

    private static readonly Func<string, (bool, string)> AnyPathValid = _ => (true, "");
    private readonly FakeFs _fs = new();
    private readonly string[] _real = [@"C:\LocalTTS", @"C:\LocalTTS\LocalSaves", @"C:\alltalk_tts"];

    private string? Check(string root, params string?[] real) =>
        E2eRootGuard.Check(root, real.Length > 0 ? real : _real, _fs, AnyPathValid);

    [Fact]
    public void FreshFolderOnE_IsSafe() => Assert.Null(Check(@"E:\EkE2E"));

    [Fact]
    public void ExistingFolderWithMarker_IsSafe()
    {
        _fs.Folders[@"E:\EkE2E"] = [E2eRootGuard.MarkerFileName, "echokrautts"];
        Assert.Null(Check(@"E:\EkE2E"));
    }

    [Fact]
    public void ExistingEmptyFolder_IsSafe()
    {
        _fs.Folders[@"E:\EkE2E"] = [];
        Assert.Null(Check(@"E:\EkE2E"));
    }

    [Fact]
    public void ExistingFolderWithoutMarker_IsRefused()
    {
        _fs.Folders[@"E:\EkE2E"] = ["my-stuff.txt"];
        Assert.Contains(E2eRootGuard.MarkerFileName, Check(@"E:\EkE2E"));
    }

    [Theory]
    [InlineData(@"C:\EkE2E", "must be a folder on drive E:")]
    [InlineData(@"E:\", "must be a folder on drive E:")]
    [InlineData(@"E:", "must be a folder on drive E:")]
    [InlineData(@"EkE2E", "must be a folder on drive E:")]
    [InlineData(@"\\server\share\EkE2E", "UNC or device path")]
    [InlineData(@"\\?\E:\EkE2E", "UNC or device path")]
    [InlineData(@"//server/share", "UNC or device path")]
    [InlineData(@"E:\EkE2E\..\Games", "'.' or '..' segments")]
    [InlineData("", "empty")]
    public void WrongPlace_IsRefused(string root, string reason) => Assert.Contains(reason, Check(root));

    [Theory]
    [InlineData(@"E:\LocalTTS", "overlaps")]          // equal
    [InlineData(@"e:\localtts\", "overlaps")]         // equal, case + trailing separator
    [InlineData(@"E:\LocalTTS\EkE2E", "overlaps")]    // inside
    [InlineData(@"E:\", "must be a folder on drive E:")]                // parent (refused as drive root anyway)
    public void OverlapWithRealPath_IsRefused(string root, string reason) =>
        Assert.Contains(reason, Check(root, @"E:\LocalTTS", @"E:\LocalTTS\LocalSaves"));

    [Fact]
    public void RealPathInsideRoot_IsRefused() =>
        Assert.Contains("overlaps", Check(@"E:\Tts", @"E:\Tts\Real"));

    [Fact]
    public void JunctionToARealPath_IsRefused()
    {
        _fs.Links[@"E:\EkE2E"] = @"C:\LocalTTS";
        Assert.Contains("overlaps", Check(@"E:\EkE2E"));
    }

    [Fact]
    public void InvalidInstallPath_IsRefusedWithTheUiReason()
    {
        var reason = E2eRootGuard.Check(@"E:\Ek E2E", _real, _fs, _ => (false, "no spaces"));
        Assert.Contains("no spaces", reason);
    }

    [WindowsFact]
    public void RealValidator_RejectsSpaces() =>
        Assert.NotNull(E2eRootGuard.Check(@"E:\Ek E2E", _real, _fs));

    [WindowsFact]
    public void RealValidator_AcceptsTheDefaultRoot() =>
        Assert.Null(E2eRootGuard.Check(@"E:\EkE2E", _real, _fs));

    [Fact]
    public void EmptyProtectedPaths_AreIgnored() => Assert.Null(E2eRootGuard.Check(@"E:\EkE2E", [null, "", " "], _fs, AnyPathValid));
}
