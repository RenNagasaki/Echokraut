namespace Echokraut.Helper.Functional.Scd;

public enum ScdOggCryptType : short
{
    None = 0x0000,
    VorbisHeaderXor = 0x2002,
    FullXorUsingTable = 0x2003,
}
