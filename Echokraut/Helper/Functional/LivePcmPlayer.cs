namespace Echokraut.Helper.Functional;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;

public enum PlaybackState { Stopped, Playing, Paused }

public sealed class LivePcmStreamPlayer : IDisposable
{
    public event EventHandler? PlaybackEnded;
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    readonly Stream _source;
    readonly bool _leaveOpen;
    readonly bool _autoDetectWav;
    readonly int _bufferMs;
    readonly int _readChunkMs;
    readonly int _deviceIndex;

    int _sr;
    int _ch;
    bool _float32;

    readonly BlockingCollection<byte[]> _q;
    readonly CancellationTokenSource _cts = new();
    readonly ManualResetEventSlim _playGate = new(true);
    readonly object _stateLock = new();

    int _h;
    Task? _reader, _writer;
    int _ended;
    SyncProcedure? _endSync;
    float _volume = 1f;

    public double Volume
    {
        get { lock (_stateLock) return _volume; }
        set
        {
            var v = (float)Math.Clamp(value, 0.0, 1.0);
            lock (_stateLock)
            {
                _volume = v;
                if (_h != 0) Bass.ChannelSetAttribute(_h, ChannelAttribute.Volume, _volume);
            }
        }
    }

    public LivePcmStreamPlayer(
        Stream source,
        int sampleRate = 24000,
        int channels = 1,
        bool float32 = false,
        int deviceIndex = -1,
        int bufferMs = 250,
        int readChunkMs = 20,
        bool leaveOpen = false,
        bool autoDetectWavHeader = true,
        int queueCapacity = 256,
        double initialVolume = 1.0)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (!source.CanRead) throw new ArgumentException("Stream ist nicht lesbar.", nameof(source));

        _source = source;
        _sr = sampleRate;
        _ch = channels;
        _float32 = float32;
        _deviceIndex = deviceIndex;
        _bufferMs = bufferMs;
        _readChunkMs = readChunkMs;
        _leaveOpen = leaveOpen;
        _autoDetectWav = autoDetectWavHeader;
        _volume = (float)Math.Clamp(initialVolume, 0.0, 1.0);

        _q = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), Math.Max(8, queueCapacity));
    }

    public void Start()
    {
        if (_reader != null || _writer != null) return;

        if (_autoDetectWav && TryConsumeWavHeader(_source, out var fmt))
        {
            _sr = fmt.SampleRate;
            _ch = fmt.Channels;
            _float32 = fmt.IsFloat32;
        }

        if (!Bass.Init(_deviceIndex, _sr, DeviceInitFlags.Default) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException($"Bass.Init failed: {Bass.LastError}");

        var flags = _float32 ? BassFlags.Float : BassFlags.Default;
        _h = Bass.CreateStream(_sr, _ch, flags, StreamProcedureType.Push);
        if (_h == 0)
            throw new InvalidOperationException($"CreateStream failed: {Bass.LastError}");

        Bass.ChannelSetAttribute(_h, ChannelAttribute.Buffer, _bufferMs / 1000f);
        Bass.ChannelSetAttribute(_h, ChannelAttribute.Volume, _volume);

        _endSync = OnEndSync;
        if (Bass.ChannelSetSync(_h, SyncFlags.End, 0, _endSync) == 0)
            throw new InvalidOperationException($"ChannelSetSync(End) failed: {Bass.LastError}");

        _reader = Task.Run(ReadLoop, _cts.Token);
        _writer = Task.Run(WriteLoop, _cts.Token);

        if (!Bass.ChannelPlay(_h, false))
            throw new InvalidOperationException($"ChannelPlay failed: {Bass.LastError}");

        SetState(PlaybackState.Playing);
    }

    public void Pause()
    {
        lock (_stateLock)
        {
            if (State != PlaybackState.Playing) return;
            Bass.ChannelPause(_h);
            _playGate.Reset();
            SetState(PlaybackState.Paused);
        }
    }

    public void Resume()
    {
        lock (_stateLock)
        {
            if (State != PlaybackState.Paused) return;
            _playGate.Set();
            if (!Bass.ChannelPlay(_h, false))
                throw new InvalidOperationException($"ChannelPlay (resume) failed: {Bass.LastError}");
            SetState(PlaybackState.Playing);
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (State == PlaybackState.Stopped) return;
            _playGate.Set();
            _cts.Cancel();
            _q.CompleteAdding();
            try { Task.WaitAll(new[] { _reader, _writer }, 2000); } catch { }
            Bass.ChannelStop(_h);
            SetState(PlaybackState.Stopped);
        }
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
        if (_h != 0) { Bass.StreamFree(_h); _h = 0; }
        Bass.Free();
        _cts.Cancel();
        _cts.Dispose();
        if (!_leaveOpen) _source.Dispose();
    }

    void OnEndSync(int handle, int channel, int data, IntPtr user)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            SetState(PlaybackState.Stopped);
            ThreadPool.QueueUserWorkItem(_ => PlaybackEnded?.Invoke(this, EventArgs.Empty));
        }
    }

    async Task ReadLoop()
    {
        try
        {
            int bps = _float32 ? 4 : 2;
            int frame = Math.Max(1, bps * _ch);
            int readBytes = Math.Max(frame, _sr * frame * _readChunkMs / 1000);

            var buf = new byte[readBytes];
            var carry = new MemoryStream(capacity: readBytes * 2);

            while (!_cts.IsCancellationRequested)
            {
                int n = await _source.ReadAsync(buf.AsMemory(0, buf.Length), _cts.Token).ConfigureAwait(false);
                if (n <= 0) break;

                carry.Write(buf, 0, n);

                while (carry.Length >= frame)
                {
                    int whole = (int)(carry.Length / frame) * frame;
                    if (whole == 0) break;

                    int outLen = Math.Min(whole, readBytes * 2);
                    var chunk = new byte[outLen];

                    carry.Position = 0;
                    int copied = await carry.ReadAsync(chunk, 0, outLen, _cts.Token).ConfigureAwait(false);

                    int leftoverLen = (int)(carry.Length - copied);
                    if (leftoverLen > 0)
                    {
                        var tmp = new byte[leftoverLen];
                        await carry.ReadAsync(tmp, 0, leftoverLen, _cts.Token).ConfigureAwait(false);
                        carry.SetLength(0);
                        if (leftoverLen > 0) await carry.WriteAsync(tmp, 0, leftoverLen, _cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        carry.SetLength(0);
                    }

                    if (copied > 0)
                    {
                        var send = chunk;
                        if (copied != chunk.Length)
                        {
                            send = new byte[copied];
                            Buffer.BlockCopy(chunk, 0, send, 0, copied);
                        }
                        _q.Add(send, _cts.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _q.CompleteAdding();
        }
    }

    async Task WriteLoop()
    {
        try
        {
            foreach (var chunk in _q.GetConsumingEnumerable(_cts.Token))
            {
                _playGate.Wait(_cts.Token);

                int offset = 0;
                while (offset < chunk.Length && !_cts.IsCancellationRequested)
                {
                    _playGate.Wait(_cts.Token);
                    int wrote = PutData(_h, chunk, offset, chunk.Length - offset);
                    if (wrote < 0) throw new InvalidOperationException($"StreamPutData failed: {Bass.LastError}");
                    if (wrote == 0)
                    {
                        await Task.Delay(5, _cts.Token).ConfigureAwait(false);
                        continue;
                    }
                    offset += wrote;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!_cts.IsCancellationRequested)
                Bass.StreamPutData(_h, IntPtr.Zero, (int)StreamProcedureType.End);
        }
    }

    static int PutData(int handle, byte[] buffer, int offset, int count)
    {
        if (count <= 0) return 0;
        if (offset == 0)
            return Bass.StreamPutData(handle, buffer, count);

        var gch = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var ptr = IntPtr.Add(gch.AddrOfPinnedObject(), offset);
            return Bass.StreamPutData(handle, ptr, count);
        }
        finally
        {
            gch.Free();
        }
    }

    void SetState(PlaybackState s)
    {
        lock (_stateLock) { State = s; }
    }

    sealed class WavFormat
    {
        public int SampleRate = 24000;
        public int Channels = 1;
        public bool IsFloat32 = false;
    }

    static bool TryConsumeWavHeader(Stream s, out WavFormat fmt)
    {
        fmt = new WavFormat();

        if (!s.CanSeek) return false;

        long start = s.Position;
        var br = new BinaryReader(s, System.Text.Encoding.ASCII, leaveOpen: true);
        try
        {
            if (new string(br.ReadChars(4)) != "RIFF") { s.Position = start; return false; }
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") { s.Position = start; return false; }

            short audioFormat = 1;
            short channels = 1;
            int sampleRate = 24000;
            short bitsPerSample = 16;
            bool haveFmt = false;
            long dataPos = -1;

            while (s.Position + 8 <= s.Length)
            {
                string id = new string(br.ReadChars(4));
                uint len = br.ReadUInt32();
                long next = s.Position + len;

                if (id == "fmt ")
                {
                    audioFormat = br.ReadInt16();
                    channels = br.ReadInt16();
                    sampleRate = br.ReadInt32();
                    br.ReadInt32();
                    br.ReadInt16();
                    bitsPerSample = br.ReadInt16();
                    if (len > 16) s.Position = next;
                    haveFmt = true;
                }
                else if (id == "data")
                {
                    dataPos = s.Position;
                    break;
                }
                else
                {
                    s.Position = next;
                }
            }

            if (!haveFmt || dataPos < 0) { s.Position = start; return false; }

            s.Position = dataPos;

            fmt.SampleRate = sampleRate;
            fmt.Channels = channels;
            fmt.IsFloat32 = (audioFormat == 3 && bitsPerSample == 32) || (audioFormat == 1 && bitsPerSample == 32);
            return true;
        }
        catch
        {
            s.Position = start;
            return false;
        }
    }
}
