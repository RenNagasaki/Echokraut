namespace Echokraut.Helper.Functional.Scd;

public abstract class ScdEntry
{
    public ScdFile File { get; }
    public ScdEntryHeader Header { get; }

    protected ScdEntry(ScdFile file, ScdEntryHeader header)
    {
        File = file;
        Header = header;
    }

    public abstract byte[] GetDecoded();
}
