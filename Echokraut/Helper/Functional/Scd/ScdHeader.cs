using System.Runtime.InteropServices;

namespace Echokraut.Helper.Functional.Scd;

[StructLayout(LayoutKind.Sequential)]
public struct ScdHeader
{
    public short Unknown1Count;
    public short Unknown2Count;
    public short EntryCount;
    public short Unknown1;
    public int Unknown1Offset;
    public int EntryTableOffset;
    public int Unknown2Offset;
    public int Unknown2;
    public int UnknownOffset1;
}
