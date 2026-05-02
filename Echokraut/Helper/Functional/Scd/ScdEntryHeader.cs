using System.Runtime.InteropServices;

namespace Echokraut.Helper.Functional.Scd;

[StructLayout(LayoutKind.Sequential)]
public struct ScdEntryHeader
{
    public int DataSize;
    public int ChannelCount;
    public int Frequency;
    public ScdCodec Codec;
    public int LoopStartSample;
    public int LoopEndSample;
    public int SamplesOffset;
    public short AuxChunkCount;
    public short Unknown1;
}
