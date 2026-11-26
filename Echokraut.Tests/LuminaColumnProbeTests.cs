using System.Collections.Generic;
using Echokraut.Helper.Functional;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The pure merge step behind the harvester's shop/warp → DefaultTalk linking.
/// </summary>
public class LuminaColumnProbeTests
{
    [Fact]
    public void MergeInto_NewRow_StoresTheIds()
    {
        var target = new Dictionary<uint, HashSet<uint>>();

        LuminaColumnProbe.MergeInto(target, 42, new HashSet<uint> { 1, 2 });

        Assert.Equal(new HashSet<uint> { 1, 2 }, target[42]);
    }

    [Fact]
    public void MergeInto_ExistingRow_UnionsInsteadOfReplacing()
    {
        // Several sheets can point at the same intermediate row; the second one must not drop
        // what the first contributed.
        var target = new Dictionary<uint, HashSet<uint>> { [42] = new() { 1, 2 } };

        LuminaColumnProbe.MergeInto(target, 42, new HashSet<uint> { 2, 3 });

        Assert.Equal(new HashSet<uint> { 1, 2, 3 }, target[42]);
    }

    [Fact]
    public void MergeInto_EmptyIds_DoesNotCreateAnEntry()
    {
        var target = new Dictionary<uint, HashSet<uint>>();

        LuminaColumnProbe.MergeInto(target, 42, new HashSet<uint>());

        Assert.Empty(target);
    }

    [Fact]
    public void MergeInto_EmptyIds_LeavesAnExistingEntryAlone()
    {
        var target = new Dictionary<uint, HashSet<uint>> { [42] = new() { 1 } };

        LuminaColumnProbe.MergeInto(target, 42, new HashSet<uint>());

        Assert.Equal(new HashSet<uint> { 1 }, target[42]);
    }
}
