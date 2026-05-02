namespace Echokraut.Helper.Functional.Scd;

/// <summary>
/// FFXIV SCD audio codec types. Forked from SaintCoinach.Sound.ScdCodec — adapted to read
/// from a raw byte buffer instead of a SaintCoinach IO file.
/// </summary>
public enum ScdCodec
{
    None = 0x00,
    OGG = 0x06,
    MSADPCM = 0x0C,
}
