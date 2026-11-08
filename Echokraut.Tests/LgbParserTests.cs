using Echokraut.Services;
using Xunit;

namespace Echokraut.Tests;

public class LgbParserTests
{
    // LGB1 magic = 0x3142474C, LGP1 magic = 0x3150474C
    private static readonly byte[] Lgb1Magic = { 0x4C, 0x47, 0x42, 0x31 }; // "LGB1"
    private static readonly byte[] Lgp1Magic = { 0x4C, 0x47, 0x50, 0x31 }; // "LGP1"

    [Fact]
    public void ParseENpcEntries_EmptyData_ReturnsEmpty()
    {
        var result = LgbParser.ParseENpcEntries(Array.Empty<byte>());
        Assert.Empty(result);
    }

    [Fact]
    public void ParseENpcEntries_NullData_ReturnsEmpty()
    {
        var result = LgbParser.ParseENpcEntries(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseENpcEntries_WrongMagic_ReturnsEmpty()
    {
        var data = new byte[100];
        data[0] = 0xFF; // wrong magic
        var result = LgbParser.ParseENpcEntries(data);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseENpcEntries_ValidENpcEntry_ExtractsBaseIdAndBehavior()
    {
        // Build a minimal LGB file with one layer containing one ENpc entry
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // File header (12 bytes)
        w.Write(Lgb1Magic);    // magic "LGB1"
        w.Write(0);            // fileSize (placeholder)
        w.Write(1);            // chunkCount

        // Chunk header (24 bytes, starting at offset 0x0C)
        w.Write(Lgp1Magic);    // magic "LGP1"
        w.Write(0);            // chunkSize
        w.Write(0);            // layerGroupId
        w.Write(0);            // nameOffset
        w.Write(0);            // layersOffset (unused)
        w.Write(1);            // layerCount = 1

        // Layer offset array (1 entry, at offset 0x24)
        var layerOffsetsBase = (int)ms.Position;
        var layerDataOffset = 4; // layer data starts 4 bytes after the offset array start
        w.Write(layerDataOffset);

        // Layer data (at offset 0x28)
        var layerStart = (int)ms.Position;
        w.Write((uint)1);     // layerId
        w.Write(0);            // nameOffset
        var instanceObjectsOffsetField = (int)ms.Position - layerStart;
        w.Write(0);            // instanceObjectsOffset (placeholder)
        w.Write(1);            // instanceObjectCount = 1

        // Pad to layer header size (we need at least 16 bytes for the header)
        // instanceObjectsOffset is relative to layerStart, so calculate where our offset array will be
        var instanceObjArrayStart = (int)ms.Position;
        var instanceObjectsOffset = instanceObjArrayStart - layerStart;

        // Go back and write the correct instanceObjectsOffset
        ms.Position = layerStart + 8; // offset of instanceObjectsOffset field
        w.Write(instanceObjectsOffset);
        ms.Position = instanceObjArrayStart;

        // Instance object offset array (1 entry)
        var objOffsetArrayStart = (int)ms.Position;
        var entryOffset = 4; // entry starts 4 bytes after the offset array start
        w.Write(entryOffset);

        // ENpc instance object entry
        var entryStart = (int)ms.Position;

        // Common header (48 bytes = 0x30)
        w.Write(LgbParser.TypeENpc);  // AssetType = 8 (ENpc)
        w.Write((uint)12345);          // InstanceId
        w.Write(0);                    // NameOffset
        // Translation (12 bytes)
        w.Write(1.0f); w.Write(2.0f); w.Write(3.0f);
        // Rotation (12 bytes)
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f);
        // Scale (12 bytes)
        w.Write(1.0f); w.Write(1.0f); w.Write(1.0f);

        // ENpc type-specific data (44 bytes)
        w.Write((uint)1006004);   // +0x00: BaseId (ENpcBase row ID)
        w.Write((uint)0);         // +0x04: PopWeather
        w.Write((byte)0);         // +0x08: PopTimeStart
        w.Write((byte)0);         // +0x09: PopTimeEnd
        w.Write((ushort)0);       // +0x0A: Padding
        w.Write((uint)0);         // +0x0C: MoveAi
        w.Write((byte)0);         // +0x10: WanderingRange
        w.Write((byte)0);         // +0x11: Route
        w.Write((ushort)0);       // +0x12: EventGroup
        w.Write((uint)0);         // +0x14: padding1
        w.Write((uint)0);         // +0x18: padding2
        w.Write((uint)30085);     // +0x1C: Behavior
        w.Write((uint)0);         // +0x20: padding3
        w.Write((uint)0);         // +0x24: padding4

        var data = ms.ToArray();
        var result = LgbParser.ParseENpcEntries(data);

        Assert.Single(result);
        Assert.Equal((uint)1006004, result[0].BaseId);
        Assert.Equal((uint)30085, result[0].BehaviorId);
    }

    [Fact]
    public void ParseENpcEntries_NonENpcEntries_AreSkipped()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // File header
        w.Write(Lgb1Magic);
        w.Write(0);
        w.Write(1);

        // Chunk header
        w.Write(Lgp1Magic);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(1); // 1 layer

        // Layer offset
        w.Write(4);

        // Layer header
        var layerStart = (int)ms.Position;
        w.Write((uint)1);
        w.Write(0);
        var instanceObjOffsetPos = (int)ms.Position;
        w.Write(0); // placeholder
        w.Write(1); // 1 entry

        var instanceObjArrayStart = (int)ms.Position;
        ms.Position = instanceObjOffsetPos;
        w.Write(instanceObjArrayStart - layerStart);
        ms.Position = instanceObjArrayStart;

        // Object offset
        w.Write(4);

        // EventObject entry (type 45, not ENpc)
        w.Write(LgbParser.TypeEventObject); // AssetType = 45
        w.Write((uint)99999);
        w.Write(0);
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f);
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f);
        w.Write(1.0f); w.Write(1.0f); w.Write(1.0f);
        // Type-specific data
        w.Write((uint)500); // BaseId for EventObject
        // Pad enough bytes
        for (var i = 0; i < 10; i++) w.Write((uint)0);

        var result = LgbParser.ParseENpcEntries(ms.ToArray());
        Assert.Empty(result);
    }

    [Fact]
    public void ParseENpcEntries_MultipleLayersAndEntries_ExtractsAll()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // File header
        w.Write(Lgb1Magic);
        w.Write(0);
        w.Write(1);

        // Chunk header
        w.Write(Lgp1Magic);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(2); // 2 layers

        var layerOffsetsBase = (int)ms.Position;

        // Reserve space for 2 layer offsets
        w.Write(0); // placeholder layer 0
        w.Write(0); // placeholder layer 1

        // Layer 0: 1 ENpc entry
        var layer0Start = (int)ms.Position;
        ms.Position = layerOffsetsBase;
        w.Write(layer0Start - layerOffsetsBase);
        ms.Position = layer0Start;

        w.Write((uint)1); // layerId
        w.Write(0);        // nameOffset
        var layer0ObjOffPos = (int)ms.Position;
        w.Write(0);        // instanceObjectsOffset placeholder
        w.Write(1);        // 1 entry

        var layer0ObjArray = (int)ms.Position;
        ms.Position = layer0ObjOffPos;
        w.Write(layer0ObjArray - layer0Start);
        ms.Position = layer0ObjArray;

        w.Write(4); // entry offset
        WriteENpcEntry(w, 2000001, 100);

        // Layer 1: 1 ENpc entry
        var layer1Start = (int)ms.Position;
        ms.Position = layerOffsetsBase + 4;
        w.Write(layer1Start - layerOffsetsBase);
        ms.Position = layer1Start;

        w.Write((uint)2);
        w.Write(0);
        var layer1ObjOffPos = (int)ms.Position;
        w.Write(0);
        w.Write(1);

        var layer1ObjArray = (int)ms.Position;
        ms.Position = layer1ObjOffPos;
        w.Write(layer1ObjArray - layer1Start);
        ms.Position = layer1ObjArray;

        w.Write(4);
        WriteENpcEntry(w, 2000002, 200);

        var result = LgbParser.ParseENpcEntries(ms.ToArray());
        Assert.Equal(2, result.Count);
        Assert.Equal((uint)2000001, result[0].BaseId);
        Assert.Equal((uint)100, result[0].BehaviorId);
        Assert.Equal((uint)2000002, result[1].BaseId);
        Assert.Equal((uint)200, result[1].BehaviorId);
    }

    [Fact]
    public void ParseENpcEntries_ZeroBaseId_IsSkipped()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // File + chunk header
        w.Write(Lgb1Magic); w.Write(0); w.Write(1);
        w.Write(Lgp1Magic); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(1);

        w.Write(4); // layer offset

        var layerStart = (int)ms.Position;
        w.Write((uint)1); w.Write(0);
        var objOffPos = (int)ms.Position;
        w.Write(0); w.Write(1);
        var objArray = (int)ms.Position;
        ms.Position = objOffPos;
        w.Write(objArray - layerStart);
        ms.Position = objArray;

        w.Write(4);
        WriteENpcEntry(w, 0, 500); // BaseId = 0 should be skipped

        var result = LgbParser.ParseENpcEntries(ms.ToArray());
        Assert.Empty(result);
    }

    [Fact]
    public void ScanEntriesForNearbyIds_FindsBalloonIdNearEntry()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // Build minimal LGB with one ENpc entry that has a "Balloon ID" at offset +48 from BaseId
        w.Write(Lgb1Magic); w.Write(0); w.Write(1);
        w.Write(Lgp1Magic); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(1);
        w.Write(4);

        var layerStart = (int)ms.Position;
        w.Write((uint)1); w.Write(0);
        var objOffPos = (int)ms.Position;
        w.Write(0); w.Write(1);
        var objArray = (int)ms.Position;
        ms.Position = objOffPos;
        w.Write(objArray - layerStart);
        ms.Position = objArray;

        w.Write(4);
        // Write ENpc entry, but put a known "Balloon ID" in the padding fields
        var entryStart = (int)ms.Position;
        // Common header
        w.Write(LgbParser.TypeENpc);
        w.Write((uint)0); w.Write(0);
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f);
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f);
        w.Write(1.0f); w.Write(1.0f); w.Write(1.0f);
        // Type-specific
        w.Write((uint)1006004); // BaseId
        w.Write((uint)0);       // PopWeather
        w.Write((uint)0);       // PopTimeStart/End
        w.Write((uint)0);       // MoveAi
        w.Write((uint)0);       // WanderingRange etc.
        w.Write((uint)0);       // padding1
        w.Write((uint)0);       // padding2
        w.Write((uint)30085);   // Behavior
        w.Write((uint)0);       // padding3
        w.Write((uint)42);      // padding4 — pretend this is a Balloon ID
        // Extra bytes beyond documented structure
        w.Write((uint)777);     // another value past the entry

        var data = ms.ToArray();
        var entries = LgbParser.ParseENpcEntries(data);
        Assert.Single(entries);

        var validIds = new HashSet<uint> { 42, 777 };
        var found = LgbParser.ScanEntriesForNearbyIds(data, entries, validIds);

        Assert.True(found.ContainsKey(42));
        Assert.Equal((uint)1006004, found[42]);
        Assert.True(found.ContainsKey(777));
        Assert.Equal((uint)1006004, found[777]);
    }

    [Fact]
    public void ScanEntriesForNearbyIds_IgnoresZeroValues()
    {
        var entries = new List<LgbParser.ENpcEntry>
        {
            new(1006004, 100, 0)
        };
        // Data with zeros around the entry
        var data = new byte[200];
        var validIds = new HashSet<uint> { 0 }; // zero should never match

        var found = LgbParser.ScanEntriesForNearbyIds(data, entries, validIds);
        Assert.Empty(found);
    }

    private static void WriteENpcEntry(BinaryWriter w, uint baseId, uint behaviorId)
    {
        // Common header (48 bytes)
        w.Write(LgbParser.TypeENpc);
        w.Write((uint)0); // InstanceId
        w.Write(0);        // NameOffset
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f); // Translation
        w.Write(0.0f); w.Write(0.0f); w.Write(0.0f); // Rotation
        w.Write(1.0f); w.Write(1.0f); w.Write(1.0f); // Scale

        // ENpc type-specific (44 bytes)
        w.Write(baseId);       // +0x00: BaseId
        w.Write((uint)0);      // +0x04: PopWeather
        w.Write((uint)0);      // +0x08: PopTimeStart/End/padding
        w.Write((uint)0);      // +0x0C: MoveAi
        w.Write((uint)0);      // +0x10: WanderingRange/Route/EventGroup
        w.Write((uint)0);      // +0x14: padding1
        w.Write((uint)0);      // +0x18: padding2
        w.Write(behaviorId);   // +0x1C: Behavior
        w.Write((uint)0);      // +0x20: padding3
        w.Write((uint)0);      // +0x24: padding4
    }
}
