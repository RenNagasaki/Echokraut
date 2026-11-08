using System.Collections.Generic;

namespace Echokraut.Helper.Functional;

/// <summary>
/// Helpers for reading Excel sheets by raw column index.
///
/// <para>Column indices are hard-coded against a specific game version, and Square moves them
/// between patches. Reading a moved column throws, and the harvester's answer is to skip that
/// value — which is right (a missing link is better than a crash) but used to happen in ~30
/// separate silent <c>catch { }</c> blocks. The point of routing them through here is that the
/// caller can COUNT the misses and say so once, instead of quietly harvesting nothing.</para>
/// </summary>
public static class LuminaColumnProbe
{
    /// <summary>
    /// Merges <paramref name="ids"/> into <paramref name="target"/> under <paramref name="rowId"/>,
    /// unioning with whatever is already stored there.
    /// </summary>
    /// <remarks>
    /// Several sheets can point at the same intermediate row id, so this always unions. The first
    /// of the harvester's sheet blocks used to assign outright instead — harmless only because it
    /// happened to run first, against an empty dictionary.
    /// </remarks>
    public static void MergeInto(Dictionary<uint, HashSet<uint>> target, uint rowId, HashSet<uint> ids)
    {
        if (ids.Count == 0) return;

        if (!target.TryGetValue(rowId, out var existing))
            target[rowId] = ids;
        else
            foreach (var id in ids) existing.Add(id);
    }
}
