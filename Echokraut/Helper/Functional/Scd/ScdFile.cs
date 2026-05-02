using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Echokraut.Helper.Functional.Scd;

/// <summary>
/// Parses an FFXIV SCD audio file from a raw byte buffer. Forked from
/// <c>SaintCoinach.Sound.ScdFile</c>; adapted to read directly from a byte array (so it
/// can be fed by <c>IDataManager.GetFile().Data</c> without pulling in SaintCoinach's
/// IO/Pack abstraction).
/// </summary>
public class ScdFile
{
    private bool _useLittleEndian;

    /// <summary>Raw input bytes — exposed internal so entries can read from offsets directly.</summary>
    internal byte[] InputBuffer { get; private set; }

    public ScdHeader ScdHeader { get; private set; }
    public ScdEntryHeader[] EntryHeaders { get; private set; } = Array.Empty<ScdEntryHeader>();
    public ScdEntry?[] Entries { get; private set; } = Array.Empty<ScdEntry?>();

    public ScdFile(byte[] data)
    {
        InputBuffer = data ?? throw new ArgumentNullException(nameof(data));
        Decode();
    }

    private void Decode()
    {
        Init();

        var fileHeaderSize = ReadInt16(0x0E);
        ReadScdHeaderAt(fileHeaderSize);

        var entryHeaders = new ScdEntryHeader[ScdHeader.EntryCount];
        var entryChunkOffsets = new int[ScdHeader.EntryCount];
        var entryDataOffsets = new int[ScdHeader.EntryCount];
        for (var i = 0; i < ScdHeader.EntryCount; ++i)
        {
            var headerOffset = ReadInt32(ScdHeader.EntryTableOffset + 4 * i);
            entryHeaders[i] = ReadEntryHeader(headerOffset);

            entryChunkOffsets[i] = headerOffset + Marshal.SizeOf<ScdEntryHeader>();
            entryDataOffsets[i] = entryChunkOffsets[i];
            for (var j = 0; j < entryHeaders[i].AuxChunkCount; ++j)
                entryDataOffsets[i] += ReadInt32(entryDataOffsets[i] + 4);
        }
        EntryHeaders = entryHeaders;

        var entries = new ScdEntry?[ScdHeader.EntryCount];
        for (var i = 0; i < ScdHeader.EntryCount; ++i)
            entries[i] = CreateEntry(entryHeaders[i], entryChunkOffsets[i], entryDataOffsets[i]);
        Entries = entries;

        // Original frees the buffer here, but ScdEntry instances reference it through
        // File.InputBuffer for lazy decode reads. Since our entries decode in their
        // constructor (eager), we could free — but holding the reference is harmless.
    }

    private void Init()
    {
        // Magic: "SEDBSSCF"
        if (ReadInt64(0, false) != 0x5345444253534346)
            throw new InvalidDataException("Not an SCD file (bad magic).");

        var verBigEndian = ReadInt32(8, false);
        var verLittleEndian = ReadInt32(8, true);
        if (verBigEndian == 2 || verBigEndian == 3)
            _useLittleEndian = false;
        else if (verLittleEndian == 2 || verLittleEndian == 3)
            _useLittleEndian = true;
        else
            throw new InvalidDataException("Unrecognised SCD version.");
    }

    private void ReadScdHeaderAt(int offset)
    {
        ScdHeader = new ScdHeader
        {
            Unknown1Count = ReadInt16(offset + 0x00),
            Unknown2Count = ReadInt16(offset + 0x02),
            EntryCount = ReadInt16(offset + 0x04),
            Unknown1 = ReadInt16(offset + 0x06),
            Unknown1Offset = ReadInt32(offset + 0x08),
            EntryTableOffset = ReadInt32(offset + 0x0C),
            Unknown2Offset = ReadInt32(offset + 0x10),
            Unknown2 = ReadInt32(offset + 0x14),
            UnknownOffset1 = ReadInt32(offset + 0x18),
        };
    }

    private ScdEntryHeader ReadEntryHeader(int offset) => new()
    {
        DataSize = ReadInt32(offset + 0x00),
        ChannelCount = ReadInt32(offset + 0x04),
        Frequency = ReadInt32(offset + 0x08),
        Codec = (ScdCodec)ReadInt32(offset + 0x0C),
        LoopStartSample = ReadInt32(offset + 0x10),
        LoopEndSample = ReadInt32(offset + 0x14),
        SamplesOffset = ReadInt32(offset + 0x18),
        AuxChunkCount = ReadInt16(offset + 0x1C),
        Unknown1 = ReadInt16(offset + 0x1E),
    };

    private ScdEntry? CreateEntry(ScdEntryHeader header, int chunksOffset, int dataOffset)
    {
        if (header.DataSize == 0 || header.Codec == ScdCodec.None)
            return null;

        return header.Codec switch
        {
            ScdCodec.OGG => new ScdOggEntry(this, header, dataOffset),
            ScdCodec.MSADPCM => new ScdAdpcmEntry(this, header, chunksOffset, dataOffset),
            _ => throw new NotSupportedException($"Unsupported SCD codec: {header.Codec}"),
        };
    }

    internal short ReadInt16(int offset) => ReadInt16(offset, _useLittleEndian);
    internal int ReadInt32(int offset) => ReadInt32(offset, _useLittleEndian);
    internal long ReadInt64(int offset) => ReadInt64(offset, _useLittleEndian);

    internal short ReadInt16(int offset, bool littleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        InputBuffer.AsSpan(offset, 2).CopyTo(buffer);
        if (BitConverter.IsLittleEndian != littleEndian) buffer.Reverse();
        return BitConverter.ToInt16(buffer);
    }

    internal int ReadInt32(int offset, bool littleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        InputBuffer.AsSpan(offset, 4).CopyTo(buffer);
        if (BitConverter.IsLittleEndian != littleEndian) buffer.Reverse();
        return BitConverter.ToInt32(buffer);
    }

    internal long ReadInt64(int offset, bool littleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        InputBuffer.AsSpan(offset, 8).CopyTo(buffer);
        if (BitConverter.IsLittleEndian != littleEndian) buffer.Reverse();
        return BitConverter.ToInt64(buffer);
    }
}
