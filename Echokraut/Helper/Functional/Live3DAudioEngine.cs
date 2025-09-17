namespace Echokraut.Helper.Functional;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;

public enum PlaybackState { Stopped, Playing, Paused }

public sealed class Live3DAudioEngine : IDisposable
{
    public event Action<Guid>? SourceEnded;

    readonly int _deviceIndex;
    bool _inited;

    readonly ConcurrentDictionary<Guid, Source> _sources = new();

    public Live3DAudioEngine(int deviceIndex = -1)
    {
        _deviceIndex = deviceIndex;
    }

    public void ConfigureListener(Vector3D position, Vector3D front, Vector3D top,
                                  float distanceFactor = 1f, float rolloffFactor = 1f, float dopplerFactor = 1f)
    {
        EnsureInit();
        Bass.Set3DPosition(position, new Vector3D(), front, top);
        Bass.Set3DFactors(distanceFactor, rolloffFactor, dopplerFactor);
        Bass.Apply3D();
    }

    public Guid PlayStream(Stream source,
                           int sampleRate = 24000,
                           int channels = 1,
                           bool float32 = false,
                           int bufferMs = 250,
                           int readChunkMs = 20,
                           bool leaveOpen = false,
                           bool autoDetectWavHeader = true,
                           double volume = 1.0,
                           Vector3D? initialPosition = null,
                           Func<Vector3D>? positionProvider = null,
                           int pollIntervalMs = 15)
    {
        EnsureInit();
        if (channels != 1) throw new InvalidOperationException("3D benötigt Mono (1 Kanal).");

        var id = Guid.NewGuid();
        var src = new Source(this, id, source, sampleRate, channels, float32, bufferMs, readChunkMs,
                             leaveOpen, autoDetectWavHeader, volume, initialPosition ?? new Vector3D(), positionProvider, pollIntervalMs);
        if (!_sources.TryAdd(id, src))
            throw new InvalidOperationException("ID collision.");
        src.Start();
        return id;
    }

    public PlaybackState GetState(Guid id) => _sources.TryGetValue(id, out var s) ? s.State : PlaybackState.Stopped;
    public void Pause(Guid id) { if (_sources.TryGetValue(id, out var s)) s.Pause(); }
    public void Resume(Guid id) { if (_sources.TryGetValue(id, out var s)) s.Resume(); }
    public void Stop(Guid id) { if (_sources.TryRemove(id, out var s)) s.StopAndDispose(); }
    public void StopAll() { foreach (var id in _sources.Keys) Stop(id); }

    public void SetVolume(Guid id, double volume) { if (_sources.TryGetValue(id, out var s)) s.Volume = volume; }
    public double GetVolume(Guid id) => _sources.TryGetValue(id, out var s) ? s.Volume : 0;

    public void SetSourcePosition(Guid id, Vector3D pos) { if (_sources.TryGetValue(id, out var s)) s.Set3DSourcePosition(pos); }
    public void SetSourcePoller(Guid id, Func<Vector3D> provider, int intervalMs = 15) { if (_sources.TryGetValue(id, out var s)) s.Set3DSourcePoller(provider, intervalMs); }

    void EnsureInit()
    {
        if (_inited) return;
        if (!Bass.Init(_deviceIndex, 48000, DeviceInitFlags.Default | DeviceInitFlags.Device3D) && Bass.LastError != Errors.Already)
            throw new InvalidOperationException($"Bass.Init failed: {Bass.LastError}");
        _inited = true;
    }

    internal void OnSourceEnded(Guid id)
    {
        _sources.TryRemove(id, out _);
        SourceEnded?.Invoke(id);
    }

    public void Dispose()
    {
        StopAll();
        if (_inited) Bass.Free();
        _inited = false;
    }

    sealed class Source : IDisposable
    {
        readonly Live3DAudioEngine _engine;
        readonly Guid _id;
        readonly Stream _source;
        readonly bool _leaveOpen;
        readonly bool _autoDetectWav;
        readonly int _bufferMs;
        readonly int _readChunkMs;

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
        float _volume;
        Timer? _pollTimer;
        Func<Vector3D>? _positionProvider;
        volatile Vector3D _srcPos;

        public PlaybackState State { get; private set; } = PlaybackState.Stopped;

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

        public Source(Live3DAudioEngine engine, Guid id, Stream source,
                      int sampleRate, int channels, bool float32,
                      int bufferMs, int readChunkMs,
                      bool leaveOpen, bool autoDetectWavHeader,
                      double initialVolume, Vector3D initialPos,
                      Func<Vector3D>? posProvider, int pollIntervalMs)
        {
            _engine = engine;
            _id = id;
            _source = source;
            _sr = sampleRate;
            _ch = channels;
            _float32 = float32;
            _bufferMs = bufferMs;
            _readChunkMs = readChunkMs;
            _leaveOpen = leaveOpen;
            _autoDetectWav = autoDetectWavHeader;
            _volume = (float)Math.Clamp(initialVolume, 0.0, 1.0);
            _srcPos = initialPos;
            _positionProvider = posProvider;
            _q = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 256);
            if (posProvider != null)
                _pollTimer = new Timer(_ => PollPosition(), null, pollIntervalMs, pollIntervalMs);
        }

        public void Start()
        {
            if (_autoDetectWav && TryConsumeWavHeader(_source, out var fmt))
            {
                _sr = fmt.SampleRate;
                _ch = fmt.Channels;
                _float32 = fmt.IsFloat32;
            }
            if (_ch != 1) throw new InvalidOperationException("3D benötigt Mono (1 Kanal).");

            var flags = (_float32 ? BassFlags.Float : BassFlags.Default) | BassFlags.Bass3D;
            _h = Bass.CreateStream(_sr, _ch, flags, StreamProcedureType.Push);
            if (_h == 0)
                throw new InvalidOperationException($"CreateStream failed: {Bass.LastError}");

            Bass.ChannelSetAttribute(_h, ChannelAttribute.Buffer, _bufferMs / 1000f);
            Bass.ChannelSetAttribute(_h, ChannelAttribute.Volume, _volume);
            Bass.ChannelSet3DPosition(_h, _srcPos, null, new Vector3D());
            Bass.Apply3D();

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

        public void StopAndDispose()
        {
            lock (_stateLock)
            {
                if (State == PlaybackState.Stopped)
                {
                    Dispose();
                    return;
                }
                _playGate.Set();
                _cts.Cancel();
                _q.CompleteAdding();
                try { Task.WaitAll(new[] { _reader, _writer }, 2000); } catch { }
                _pollTimer?.Dispose();
                _pollTimer = null;
                Bass.ChannelStop(_h);
                SetState(PlaybackState.Stopped);
                Dispose();
            }
        }

        public void Dispose()
        {
            try { _pollTimer?.Dispose(); } catch { }
            if (_h != 0) { try { Bass.StreamFree(_h); } catch { } _h = 0; }
            _cts.Cancel();
            _cts.Dispose();
            if (!_leaveOpen) _source.Dispose();
        }

        public void Set3DSourcePosition(Vector3D position)
        {
            _srcPos = position;
            if (_h != 0)
            {
                Bass.ChannelSet3DPosition(_h, _srcPos, null, new Vector3D());
                Bass.Apply3D();
            }
        }

        public void Set3DSourcePoller(Func<Vector3D> positionProvider, int intervalMs = 15)
        {
            _positionProvider = positionProvider ?? throw new ArgumentNullException(nameof(positionProvider));
            _pollTimer?.Dispose();
            _pollTimer = new Timer(_ => PollPosition(), null, intervalMs, intervalMs);
        }

        void PollPosition()
        {
            if (State != PlaybackState.Playing || _positionProvider == null) return;
            var p = _positionProvider();
            _srcPos = p;
            if (_h != 0)
            {
                Bass.ChannelSet3DPosition(_h, _srcPos, null, new Vector3D());
                Bass.Apply3D();
            }
        }

        void OnEndSync(int handle, int channel, int data, IntPtr user)
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                _pollTimer?.Dispose();
                _pollTimer = null;
                SetState(PlaybackState.Stopped);
                ThreadPool.QueueUserWorkItem(_ => _engine.OnSourceEnded(_id));
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
}
