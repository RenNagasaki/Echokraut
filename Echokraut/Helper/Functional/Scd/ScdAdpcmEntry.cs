using System;

namespace Echokraut.Helper.Functional.Scd;

public class ScdAdpcmEntry : ScdEntry
{
    private readonly byte[] _decoded;

    internal ScdAdpcmEntry(ScdFile file, ScdEntryHeader header, int chunksOffset, int dataOffset)
        : base(file, header)
    {
        _decoded = Decode(chunksOffset, dataOffset);
    }

    public override byte[] GetDecoded() => _decoded;

    private byte[] Decode(int chunksOffset, int dataOffset)
    {
        const int waveHeaderSize = 0x10;

        var wavHeaderOffset = dataOffset;
        var finalDataOffset = chunksOffset + Header.SamplesOffset;

        var decoded = new byte[0x1C + waveHeaderSize + Header.DataSize];
        var o = 0;
        decoded[o++] = (byte)'R';
        decoded[o++] = (byte)'I';
        decoded[o++] = (byte)'F';
        decoded[o++] = (byte)'F';

        Array.Copy(BitConverter.GetBytes(0x14 + waveHeaderSize + Header.DataSize), 0, decoded, o, 4);
        o += 4;

        decoded[o++] = (byte)'W';
        decoded[o++] = (byte)'A';
        decoded[o++] = (byte)'V';
        decoded[o++] = (byte)'E';
        decoded[o++] = (byte)'f';
        decoded[o++] = (byte)'m';
        decoded[o++] = (byte)'t';
        decoded[o++] = (byte)' ';

        Array.Copy(BitConverter.GetBytes(waveHeaderSize), 0, decoded, o, 4);
        o += 4;

        Array.Copy(File.InputBuffer, wavHeaderOffset, decoded, o, waveHeaderSize);
        o += waveHeaderSize;

        decoded[o++] = (byte)'d';
        decoded[o++] = (byte)'a';
        decoded[o++] = (byte)'t';
        decoded[o++] = (byte)'a';

        Array.Copy(BitConverter.GetBytes(Header.DataSize), 0, decoded, o, 4);
        o += 4;
        Array.Copy(File.InputBuffer, finalDataOffset, decoded, o, Header.DataSize);

        return decoded;
    }
}
