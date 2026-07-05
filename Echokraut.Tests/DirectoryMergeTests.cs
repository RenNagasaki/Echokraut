using System;
using System.IO;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>Voice-sync merge-copy: overwrite same-named, keep extras, never delete.</summary>
public class DirectoryMergeTests : IDisposable
{
    private readonly string _tmp;
    private readonly string _src;
    private readonly string _dst;

    public DirectoryMergeTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "ek_merge_" + Guid.NewGuid().ToString("N"));
        _src = Path.Combine(_tmp, "src");
        _dst = Path.Combine(_tmp, "dst");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(_dst);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MergeCopy_CopiesNewFiles_OverwritesSameNamed_KeepsExtras()
    {
        File.WriteAllText(Path.Combine(_src, "a.wav"), "new-a");
        File.WriteAllText(Path.Combine(_src, "b.wav"), "new-b");
        File.WriteAllText(Path.Combine(_dst, "a.wav"), "old-a"); // same name → overwritten
        File.WriteAllText(Path.Combine(_dst, "extra.wav"), "keep-me"); // extra → kept

        var copied = DirectoryMerge.MergeCopy(_src, _dst);

        Assert.Equal(2, copied);
        Assert.Equal("new-a", File.ReadAllText(Path.Combine(_dst, "a.wav")));
        Assert.Equal("new-b", File.ReadAllText(Path.Combine(_dst, "b.wav")));
        Assert.Equal("keep-me", File.ReadAllText(Path.Combine(_dst, "extra.wav")));
    }

    [Fact]
    public void MergeCopy_CopiesSidecarsAndSubdirs()
    {
        File.WriteAllText(Path.Combine(_src, "v.wav"), "wav");
        File.WriteAllText(Path.Combine(_src, "v.txt"), "transcript");
        var sub = Path.Combine(_src, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "c.wav"), "c");

        var copied = DirectoryMerge.MergeCopy(_src, _dst);

        Assert.Equal(3, copied);
        Assert.True(File.Exists(Path.Combine(_dst, "v.txt")));
        Assert.True(File.Exists(Path.Combine(_dst, "sub", "c.wav")));
    }

    [Fact]
    public void MergeCopy_MissingSource_IsNoOp()
    {
        Assert.Equal(0, DirectoryMerge.MergeCopy(Path.Combine(_tmp, "does-not-exist"), _dst));
    }

    [Fact]
    public void MergeCopy_CreatesDestinationIfAbsent()
    {
        File.WriteAllText(Path.Combine(_src, "a.wav"), "a");
        var freshDst = Path.Combine(_tmp, "fresh");
        var copied = DirectoryMerge.MergeCopy(_src, freshDst);
        Assert.Equal(1, copied);
        Assert.True(File.Exists(Path.Combine(freshDst, "a.wav")));
    }

    [Fact]
    public void MergeCopy_NoOverwrite_KeepsExistingTarget()
    {
        File.WriteAllText(Path.Combine(_src, "a.wav"), "new");
        File.WriteAllText(Path.Combine(_dst, "a.wav"), "old");
        var copied = DirectoryMerge.MergeCopy(_src, _dst, overwrite: false);
        Assert.Equal(0, copied);
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dst, "a.wav")));
    }
}
