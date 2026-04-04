using System;
using System.Collections.Generic;
using System.IO;

namespace Echokraut.Services;

/// <summary>
/// Parses FFXIV LGB (Level Group Binary) files to extract structured layer data.
/// LGB files contain territory placement data: NPC positions, event objects, lights, etc.
/// Format: LGB1 header → LGP1 chunk → layer groups → instance objects (typed entries).
/// </summary>
public class LgbParser
{
    // Magic bytes
    private const uint MagicLgb1 = 0x3142474C; // "LGB1" little-endian
    private const uint MagicLgp1 = 0x3150474C; // "LGP1" little-endian

    // LayerEntryType values
    public const int TypeENpc = 8;
    public const int TypeBattleNpc = 9;
    public const int TypeEventObject = 45;

    // Common instance object header size (before type-specific data)
    private const int CommonHeaderSize = 0x30; // 48 bytes

    /// <summary>
    /// Represents an ENpc entry extracted from an LGB file.
    /// </summary>
    public readonly struct ENpcEntry
    {
        /// <summary>ENpcBase row ID from the game's Excel sheets.</summary>
        public readonly uint BaseId;

        /// <summary>Behavior row ID (may differ from ENpcBase's default Behavior column).</summary>
        public readonly uint BehaviorId;

        /// <summary>Byte offset of the entry start within the LGB file data.</summary>
        public readonly int EntryOffset;

        public ENpcEntry(uint baseId, uint behaviorId, int entryOffset)
        {
            BaseId = baseId;
            BehaviorId = behaviorId;
            EntryOffset = entryOffset;
        }
    }

    /// <summary>
    /// Parse an LGB file and extract all ENpc entries with their BaseId and Behavior.
    /// </summary>
    /// <param name="data">Raw LGB file bytes.</param>
    /// <returns>List of ENpc entries found in the file.</returns>
    public static List<ENpcEntry> ParseENpcEntries(byte[] data)
    {
        var results = new List<ENpcEntry>();
        if (data == null || data.Length < 12)
            return results;

        var reader = new BinaryReader(new MemoryStream(data));

        // File header: Magic (4), FileSize (4), ChunkCount (4)
        var magic = reader.ReadUInt32();
        if (magic != MagicLgb1)
            return results;

        reader.ReadInt32(); // fileSize
        reader.ReadInt32(); // chunkCount (always 1)

        // Chunk header at offset 0x0C
        if (reader.BaseStream.Position + 24 > data.Length)
            return results;

        var chunkMagic = reader.ReadUInt32();
        if (chunkMagic != MagicLgp1)
            return results;

        reader.ReadInt32(); // chunkSize
        reader.ReadInt32(); // layerGroupId
        reader.ReadInt32(); // nameOffset
        reader.ReadInt32(); // layersOffset (unused — we read from the offset array directly)
        var layerCount = reader.ReadInt32();

        if (layerCount <= 0 || layerCount > 10000)
            return results;

        // Layer offset array starts here (right after the chunk header fields)
        var layerOffsetsBase = (int)reader.BaseStream.Position;

        for (var i = 0; i < layerCount; i++)
        {
            if (layerOffsetsBase + (i + 1) * 4 > data.Length)
                break;

            reader.BaseStream.Position = layerOffsetsBase + i * 4;
            var layerOffset = reader.ReadInt32();
            var layerStart = layerOffsetsBase + layerOffset;

            if (layerStart < 0 || layerStart + 16 > data.Length)
                continue;

            ParseLayer(data, layerStart, results);
        }

        return results;
    }

    private static void ParseLayer(byte[] data, int layerStart, List<ENpcEntry> results)
    {
        // Layer header: LayerId (4), NameOffset (4), InstanceObjectsOffset (4), InstanceObjectCount (4), ...
        if (layerStart + 16 > data.Length)
            return;

        // var layerId = BitConverter.ToUInt32(data, layerStart);
        // var nameOffset = BitConverter.ToInt32(data, layerStart + 4);
        var instanceObjectsOffset = BitConverter.ToInt32(data, layerStart + 8);
        var instanceObjectCount = BitConverter.ToInt32(data, layerStart + 12);

        if (instanceObjectCount <= 0 || instanceObjectCount > 50000)
            return;

        // Instance objects offset array starts at layerStart + instanceObjectsOffset
        var objOffsetsBase = layerStart + instanceObjectsOffset;
        if (objOffsetsBase < 0 || objOffsetsBase + instanceObjectCount * 4 > data.Length)
            return;

        for (var j = 0; j < instanceObjectCount; j++)
        {
            var objOffsetPos = objOffsetsBase + j * 4;
            if (objOffsetPos + 4 > data.Length)
                break;

            var objOffset = BitConverter.ToInt32(data, objOffsetPos);
            var entryStart = objOffsetsBase + objOffset;

            if (entryStart < 0 || entryStart + CommonHeaderSize > data.Length)
                continue;

            var assetType = BitConverter.ToInt32(data, entryStart);

            if (assetType == TypeENpc)
            {
                ParseENpcEntry(data, entryStart, results);
            }
        }
    }

    private static void ParseENpcEntry(byte[] data, int entryStart, List<ENpcEntry> results)
    {
        // Common header (48 bytes) + ENpc type-specific data
        // Type-specific layout (ENPCInstanceObject, 44 bytes total):
        //   +0x00: BaseId (uint32) = ENpcBase row ID
        //   +0x04: PopWeather (uint32)
        //   +0x08: PopTimeStart (byte), PopTimeEnd (byte), padding (2 bytes)
        //   +0x0C: MoveAi (uint32)
        //   +0x10: WanderingRange (byte), Route (byte), EventGroup (uint16)
        //   +0x14: padding (uint32)
        //   +0x18: padding (uint32)
        //   +0x1C: Behavior (uint32)
        //   +0x20: padding (uint32)
        //   +0x24: padding (uint32)

        var typeDataStart = entryStart + CommonHeaderSize; // 0x30
        var behaviorOffset = typeDataStart + 0x1C;         // 0x4C from entry start

        if (behaviorOffset + 4 > data.Length)
            return;

        var baseId = BitConverter.ToUInt32(data, typeDataStart);
        var behaviorId = BitConverter.ToUInt32(data, behaviorOffset);

        // Sanity check: ENpcBase IDs are typically in reasonable ranges
        if (baseId == 0)
            return;

        results.Add(new ENpcEntry(baseId, behaviorId, entryStart));
    }

    /// <summary>
    /// Scan raw bytes within each ENpc entry's own data for uint32 values matching the given ID set.
    /// Only scans within the entry's bounds (common header + type-specific data + small margin
    /// for undocumented trailing fields). No backward scanning — Balloon data doesn't precede headers.
    /// </summary>
    /// <param name="data">Raw LGB file bytes.</param>
    /// <param name="entries">Parsed ENpc entries with byte offsets.</param>
    /// <param name="validIds">Set of valid IDs to match against (e.g., Balloon sheet row IDs).</param>
    /// <param name="scanForward">Bytes to scan forward from BaseId position (default 100).</param>
    /// <returns>Dictionary mapping matched ID → ENpcBase ID (first NPC found).</returns>
    public static Dictionary<uint, uint> ScanEntriesForNearbyIds(
        byte[] data,
        List<ENpcEntry> entries,
        HashSet<uint> validIds,
        int scanForward = 100)
    {
        var result = new Dictionary<uint, uint>();
        if (data == null || entries == null || validIds == null || validIds.Count == 0)
            return result;

        foreach (var entry in entries)
        {
            // BaseId is at entryStart + CommonHeaderSize (0x30)
            var baseIdPos = entry.EntryOffset + CommonHeaderSize;

            // Scan forward within entry bounds only
            for (var off = 4; off <= scanForward; off += 4)
            {
                var pos = baseIdPos + off;
                if (pos + 4 > data.Length) break;

                var val = BitConverter.ToUInt32(data, pos);
                if (val != 0 && validIds.Contains(val))
                {
                    result.TryAdd(val, entry.BaseId);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// For remaining unmatched IDs, search the raw data and attribute each to the nearest ENpc entry.
    /// This is a targeted second pass for IDs that weren't found within entry bounds.
    /// </summary>
    public static Dictionary<uint, uint> FindNearestEntryForIds(
        byte[] data,
        List<ENpcEntry> entries,
        HashSet<uint> unmatchedIds)
    {
        var result = new Dictionary<uint, uint>();
        if (data == null || entries == null || entries.Count == 0
            || unmatchedIds == null || unmatchedIds.Count == 0)
            return result;

        // Build sorted list of entry positions for binary search
        var entryPositions = new List<(int pos, uint baseId)>(entries.Count);
        foreach (var e in entries)
            entryPositions.Add((e.EntryOffset + CommonHeaderSize, e.BaseId));
        entryPositions.Sort((a, b) => a.pos.CompareTo(b.pos));

        // Scan entire file for unmatched IDs, attribute to nearest entry
        for (var off = 0; off + 4 <= data.Length; off += 4)
        {
            var val = BitConverter.ToUInt32(data, off);
            if (val == 0 || !unmatchedIds.Contains(val))
                continue;

            // Binary search for nearest entry
            var nearest = FindNearestEntry(entryPositions, off);
            if (nearest.baseId != 0)
            {
                result.TryAdd(val, nearest.baseId);
                unmatchedIds.Remove(val);
                if (unmatchedIds.Count == 0)
                    break;
            }
        }

        return result;
    }

    private static (int pos, uint baseId) FindNearestEntry(
        List<(int pos, uint baseId)> sorted, int target)
    {
        var idx = sorted.BinarySearch((target, 0),
            Comparer<(int pos, uint baseId)>.Create((a, b) => a.pos.CompareTo(b.pos)));

        if (idx >= 0)
            return sorted[idx];

        idx = ~idx; // insertion point
        var distBefore = idx > 0 ? target - sorted[idx - 1].pos : int.MaxValue;
        var distAfter = idx < sorted.Count ? sorted[idx].pos - target : int.MaxValue;

        return distBefore <= distAfter ? sorted[idx - 1] : sorted[idx];
    }
}
