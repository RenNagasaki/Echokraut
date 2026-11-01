namespace Echokraut.DataClasses;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public enum WavEncoding : ushort
{
    PCM = 1,
    IEEEFloat = 3,
    Extensible = 0xFFFE,
    Unknown = 0
}

/// Liest einen RIFF/WAVE (PCM oder IEEE Float) und präsentiert den Data-Chunk als Stream.
/// Cross-platform, ohne NAudio.
public sealed class WavFileReader : Stream
{
    private readonly Stream _base;
    private readonly bool _disposeBase;
    private readonly long _dataStart;
    private readonly long _dataLength;

    public int SampleRate { get; }
    public short BitsPerSample { get; }
    public short Channels { get; }
    public int ByteRate { get; }
    public short BlockAlign { get; }
    public WavEncoding Encoding { get; }

    /// Dauer, falls Länge bekannt/seekbar.
    public TimeSpan Duration => ByteRate > 0 ? TimeSpan.FromSeconds((double)_dataLength / ByteRate) : TimeSpan.Zero;

    public WavFileReader(string filePath)
        : this(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read), leaveOpen: false) { }

    public WavFileReader(Stream stream, bool leaveOpen = false)
    {
        if (!stream.CanRead) throw new NotSupportedException("Input stream must be readable.");
        if (!stream.CanSeek) throw new NotSupportedException("Input stream must be seekable.");
        _base = stream;
        _disposeBase = !leaveOpen;

        using var br = new BinaryReader(_base, System.Text.Encoding.UTF8, leaveOpen: true);

        // ---- RIFF header ----
        string riff = new string(br.ReadChars(4));
        if (riff != "RIFF") throw new InvalidDataException("Not a RIFF file (little-endian).");
        _ = br.ReadUInt32(); // riffSize
        string wave = new string(br.ReadChars(4));
        if (wave != "WAVE") throw new InvalidDataException("Not a WAVE file.");

        bool haveFmt = false;
        long? dataStart = null;
        uint? dataSize = null;

        short channels = 0, bitsPerSample = 0, blockAlign = 0;
        int sampleRate = 0, byteRate = 0;
        WavEncoding enc = WavEncoding.Unknown;

        // ---- Chunk loop ----
        while (_base.Position + 8 <= _base.Length)
        {
            string id = new string(br.ReadChars(4));
            uint size = br.ReadUInt32();
            long chunkDataPos = _base.Position;

            if (id == "fmt ")
            {
                enc           = (WavEncoding)br.ReadUInt16();
                channels      = br.ReadInt16();
                sampleRate    = br.ReadInt32();
                byteRate      = br.ReadInt32();
                blockAlign    = br.ReadInt16();
                bitsPerSample = br.ReadInt16();

                // Überschüssige fmt-Bytes (z. B. Extensible) überspringen
                long toSkip = size - 16;
                if (toSkip > 0) _base.Seek(toSkip, SeekOrigin.Current);

                haveFmt = true;
            }
            else if (id == "data")
            {
                dataStart = _base.Position;
                dataSize  = size;
                // Wir brechen NICHT sofort ab, falls weitere Chunks folgen (selten), aber es reicht auch:
                _base.Seek(size, SeekOrigin.Current);
            }
            else
            {
                // Unbekannter/irrelevanter Chunk (LIST, fact, JUNK, …)
                _base.Seek(size, SeekOrigin.Current);
            }

            // Chunks sind auf gerade Bytezahl gepaddet
            if ((size & 1) == 1)
                _base.Seek(1, SeekOrigin.Current);
        }

        if (!haveFmt) throw new InvalidDataException("fmt chunk not found.");
        if (dataStart is null || dataSize is null) throw new InvalidDataException("data chunk not found.");

        // Header-Infos setzen
        SampleRate    = sampleRate;
        BitsPerSample = bitsPerSample;
        Channels      = channels;
        ByteRate      = byteRate;
        BlockAlign    = blockAlign;
        Encoding      = enc;

        _dataStart  = dataStart.Value;
        _dataLength = dataSize.Value;

        // Position auf Anfang der Audiodaten
        Position = 0;
    }

    // ---- Stream-Implementierung (nur Lesen/Seek) ----
    public override bool CanRead => true;
    public override bool CanSeek => _base.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _dataLength;

    private long _pos; // relative Position innerhalb des Data-Chunks
    public override long Position
    {
        get => _pos;
        set
        {
            if (value < 0 || value > _dataLength) throw new ArgumentOutOfRangeException(nameof(value));
            _pos = value;
            _base.Seek(_dataStart + _pos, SeekOrigin.Begin);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = _dataLength - _pos;
        if (remaining <= 0) return 0;
        if (count > remaining) count = (int)remaining;
        int read = _base.Read(buffer, offset, count);
        _pos += read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        long remaining = _dataLength - _pos;
        if (remaining <= 0) return 0;
        int toRead = (int)Math.Min(buffer.Length, remaining);
        int read = await _base.ReadAsync(buffer.Slice(0, toRead), cancellationToken);
        _pos += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin   => offset,
            SeekOrigin.Current => _pos + offset,
            SeekOrigin.End     => _dataLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        Position = target;
        return _pos;
    }

    public override void Flush() { /* no-op (read-only) */ }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _disposeBase) _base.Dispose();
    }
}
