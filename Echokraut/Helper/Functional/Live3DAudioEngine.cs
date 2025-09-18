namespace Echokraut.Helper.Functional;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using System.Buffers.Binary;
using System.Collections.Generic;

public enum PlaybackState { Stopped, Playing, Paused }

public sealed class Live3DAudioEngine : IDisposable
{
    public event Action<Guid>? SourceEnded;

    readonly int _deviceIndex;
    bool _inited;

    float _distanceFactor = 1f;
    float _dopplerFactor = 1f;
    internal float DistanceFactor => _distanceFactor;
    internal float DopplerFactor => _dopplerFactor;

    readonly ConcurrentDictionary<Guid, Source> _sources = new();

    public Live3DAudioEngine(int deviceIndex = -1) => _deviceIndex = deviceIndex;

    public void ConfigureListener(Vector3D position, Vector3D front, Vector3D top,
                                  float distanceFactor = 1f, float rolloffFactor = 1f, float dopplerFactor = 1f)
    {
        EnsureInit();
        _distanceFactor = distanceFactor;
        _dopplerFactor = dopplerFactor;
        NormalizeOrthonormalize(ref front, ref top); // siehe Helper unten
        Bass.Set3DPosition(position, new Vector3D(), front, top);
        Bass.Set3DFactors(distanceFactor, rolloffFactor, dopplerFactor);
        Bass.Apply3D();
    }

// Hilfsfunktion: sorgt für orthogonale, normierte front/top
    static void NormalizeOrthonormalize(ref Vector3D front, ref Vector3D top)
    {
        front = Normalize(front);
        // top orthogonalisieren
        var right = Normalize(Cross(front, top));
        top = Normalize(Cross(right, front));

        static Vector3D Normalize(Vector3D v) {
            var len = MathF.Sqrt(v.X*v.X + v.Y*v.Y + v.Z*v.Z);
            return len > 1e-6f ? new Vector3D(v.X/len, v.Y/len, v.Z/len) : new Vector3D(0,1,0);
        }
        static Vector3D Cross(Vector3D a, Vector3D b)
            => new Vector3D(a.Y*b.Z - a.Z*b.Y, a.Z*b.X - a.X*b.Z, a.X*b.Y - a.Y*b.X);
    }

    public Guid PlayStream(Stream source,
                           int sampleRate = 24000,
                           int channels = 1,
                           bool float32 = false,
                           int bufferMs = 350,
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
        int _bitsIn;
        bool _isIEEEFloatIn;
        bool _convertToFloat;
        int _bytesPerSampleIn;
        int _bytesPerSampleOut;

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
        Vector3D _lastPos;
        long _lastTicks;
        Vector3D _smoothPos;
        const float _smoothHz = 20f; // ~50 ms Zeitkonstante


        // Prefetch/Strip-Header für nicht-seekbare Streams
        readonly Queue<byte[]> _prefetchQueue = new();
        bool _headerChecked;

        // De-click Ramp
        bool _rampNeeded = true;

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
            _bitsIn = float32 ? 32 : 16;
            _isIEEEFloatIn = float32;
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
            if (_autoDetectWav)
            {
                if (_source.CanSeek)
                {
                    if (TryConsumeWavHeaderSeekable(_source, out var fmt))
                    {
                        _sr = fmt.SampleRate; _ch = fmt.Channels; _bitsIn = fmt.BitsPerSample; _isIEEEFloatIn = fmt.IsIEEEFloat;
                    }
                }
                else
                {
                    TrySniffAndStripWavHeaderNonSeek(_source, _prefetchQueue, out var fmt);
                    if (fmt != null)
                    {
                        _sr = fmt.SampleRate; _ch = fmt.Channels; _bitsIn = fmt.BitsPerSample; _isIEEEFloatIn = fmt.IsIEEEFloat;
                    }
                }
                _headerChecked = true;
            }

            if (_ch != 1) throw new InvalidOperationException("3D benötigt Mono (1 Kanal).");

            _bytesPerSampleIn = Math.Max(2, _bitsIn / 8);
            _convertToFloat = !_isIEEEFloatIn && _bitsIn != 16;
            var outFlags = (_isIEEEFloatIn || _convertToFloat) ? (BassFlags.Float | BassFlags.Bass3D)
                                                               : (BassFlags.Default | BassFlags.Bass3D);
            _bytesPerSampleOut = (_isIEEEFloatIn || _convertToFloat) ? 4 : 2;

            _h = Bass.CreateStream(_sr, _ch, outFlags, StreamProcedureType.Push);
            if (_h == 0)
                throw new InvalidOperationException($"CreateStream failed: {Bass.LastError}");
            
            // Linear/normaler Rolloff, ab 1 m keine Pegelerhöhung mehr (nahfeld)
            Bass.ChannelSet3DAttributes(_h, ManagedBass.Mode3D.Normal, 1f, 0f, -1, -1, 0);
            // minDistance=1f → unter 1 m bleibt Pegel konstant; maxDistance=0f = unendlich

            Bass.ChannelSetAttribute(_h, ChannelAttribute.Buffer, _bufferMs / 1000f);
            Bass.ChannelSetAttribute(_h, ChannelAttribute.Volume, 0f);
            Bass.ChannelSet3DPosition(_h, _srcPos, null, new Vector3D());
            Bass.Apply3D();

            _endSync = OnEndSync;
            if (Bass.ChannelSetSync(_h, SyncFlags.End, 0, _endSync) == 0)
                throw new InvalidOperationException($"ChannelSetSync(End) failed: {Bass.LastError}");

            _reader = Task.Run(ReadLoop, _cts.Token);
            _writer = Task.Run(WriteLoop, _cts.Token);
            
            _lastPos = _srcPos;
            _smoothPos = _srcPos;
            _lastTicks = Stopwatch.GetTimestamp();
            PrebufferAndStartWithFadeIn();
            SetState(PlaybackState.Playing);
        }

        void PrebufferAndStartWithFadeIn()
        {
            int frameOut = _bytesPerSampleOut * _ch;
            int targetMs = Math.Min(_bufferMs, 400);
            int targetBytes = Math.Max(frameOut, _sr * frameOut * targetMs / 1000);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1500)
            {
                int avail = Bass.ChannelGetData(_h, IntPtr.Zero, (int)DataFlags.Available);
                if (avail >= targetBytes) break;
                Thread.Sleep(5);
            }

            if (!Bass.ChannelPlay(_h, false))
                throw new InvalidOperationException($"ChannelPlay failed: {Bass.LastError}");

            float target = _volume; // exakt Ziel, kein Auto-Boost
            int fadeMs = 50;
            Bass.ChannelSlideAttribute(_h, ChannelAttribute.Volume, target, fadeMs);
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
            var now = Stopwatch.GetTimestamp();
            var dt = Math.Max(1e-4f, (float)(now - _lastTicks) / Stopwatch.Frequency);

            // Exponentielles Glätten
            float alpha = 1f - MathF.Exp(-dt * _smoothHz);
            _smoothPos = Add(_smoothPos, Scale(Sub(position, _smoothPos), alpha));

            // Velocity (in units/sek). BASS erwartet m/s, daher DistanceFactor berücksichtigen.
            var velUnitsPerSec = Scale(Sub(_smoothPos, _lastPos), 1f / dt);
            var velMetersPerSec = Scale(velUnitsPerSec, 1f / _engine.DistanceFactor);

            _lastPos = _smoothPos;
            _lastTicks = now;

            if (_h != 0)
            {
                Bass.ChannelSet3DPosition(_h, _smoothPos, null, velMetersPerSec);
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
            Set3DSourcePosition(_positionProvider());
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
                int frameIn = Math.Max(1, _bytesPerSampleIn * _ch);
                int readBytes = Math.Max(frameIn, _sr * frameIn * _readChunkMs / 1000);

                var buf = new byte[readBytes];
                var carry = new MemoryStream(capacity: readBytes * 2);

                // Prefetched (z. B. bereits nach WAV-Header)
                while (_prefetchQueue.Count > 0)
                {
                    var p = _prefetchQueue.Dequeue();
                    await EnqueueAligned(p, frameIn, carry).ConfigureAwait(false);
                }

                while (!_cts.IsCancellationRequested)
                {
                    int n = await _source.ReadAsync(buf.AsMemory(0, buf.Length), _cts.Token).ConfigureAwait(false);
                    if (n <= 0) break;

                    if (_autoDetectWav && !_headerChecked && !_source.CanSeek)
                    {
                        // Falls jemand Start() modifiziert hat: defensiv
                        _headerChecked = true;
                    }

                    carry.Write(buf, 0, n);

                    while (carry.Length >= frameIn)
                    {
                        int whole = (int)(carry.Length / frameIn) * frameIn;
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

        async Task EnqueueAligned(byte[] data, int frameIn, MemoryStream carry)
        {
            carry.Write(data, 0, data.Length);
            while (carry.Length >= frameIn)
            {
                int whole = (int)(carry.Length / frameIn) * frameIn;
                int outLen = Math.Min(whole, Math.Max(data.Length, frameIn) * 2);
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
                else carry.SetLength(0);

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

        async Task WriteLoop()
        {
            try
            {
                foreach (var chunk in _q.GetConsumingEnumerable(_cts.Token))
                {
                    _playGate.Wait(_cts.Token);

                    if (_convertToFloat)
                    {
                        int samples = chunk.Length / _bytesPerSampleIn;
                        int outBytes = samples * 4;
                        var outBuf = new byte[outBytes];
                        PcmIntToFloat(chunk, _bytesPerSampleIn * 8, outBuf);

                        if (_rampNeeded) { ApplyRampInFloat(outBuf, _ch, _sr, 0.01); _rampNeeded = false; }

                        await PushAll(outBuf).ConfigureAwait(false);
                    }
                    else if (_isIEEEFloatIn)
                    {
                        if (_rampNeeded) { ApplyRampInFloat(chunk, _ch, _sr, 0.01); _rampNeeded = false; }
                        await PushAll(chunk).ConfigureAwait(false);
                    }
                    else
                    {
                        if (_rampNeeded) { ApplyRampInInt16(chunk, _ch, _sr, 0.01); _rampNeeded = false; }
                        await PushAll(chunk).ConfigureAwait(false);
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

        async Task PushAll(byte[] data)
        {
            int offset = 0;
            while (offset < data.Length && !_cts.IsCancellationRequested)
            {
                _playGate.Wait(_cts.Token);
                int wrote = PutData(_h, data, offset, data.Length - offset);
                if (wrote < 0) throw new InvalidOperationException($"StreamPutData failed: {Bass.LastError}");
                if (wrote == 0) { await Task.Delay(5, _cts.Token).ConfigureAwait(false); continue; }
                offset += wrote;
            }
        }
        static Vector3D Sub(Vector3D a, Vector3D b) => new(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
        static Vector3D Add(Vector3D a, Vector3D b) => new(a.X+b.X, a.Y+b.Y, a.Z+b.Z);
        static Vector3D Scale(Vector3D a, float s) => new(a.X*s, a.Y*s, a.Z*s);
        static void PcmIntToFloat(ReadOnlySpan<byte> inBuf, int bitsPerSample, Span<byte> outBuf)
        {
            var dst = MemoryMarshal.Cast<byte, float>(outBuf);
            int bps = bitsPerSample / 8;
            int samples = inBuf.Length / bps;

            if (bitsPerSample == 24)
            {
                int j = 0;
                for (int i = 0; i < samples; i++, j += 3)
                {
                    int v = (sbyte)inBuf[j + 2];
                    v = (v << 8) | inBuf[j + 1];
                    v = (v << 8) | inBuf[j + 0];
                    dst[i] = Math.Clamp(v / 8388608f, -1f, 1f);
                }
            }
            else if (bitsPerSample == 32)
            {
                for (int i = 0; i < samples; i++)
                {
                    int v = BinaryPrimitives.ReadInt32LittleEndian(inBuf.Slice(i * 4, 4));
                    dst[i] = Math.Clamp(v / 2147483648f, -1f, 1f);
                }
            }
            else
            {
                for (int i = 0; i < samples; i++)
                {
                    short v = BinaryPrimitives.ReadInt16LittleEndian(inBuf.Slice(i * 2, 2));
                    dst[i] = Math.Clamp(v / 32768f, -1f, 1f);
                }
            }
        }

        static void ApplyRampInFloat(Span<byte> buf, int ch, int sr, double seconds)
        {
            int samples = buf.Length / 4;
            int rampSamples = Math.Min(samples, (int)(sr * seconds) * ch);
            var f = MemoryMarshal.Cast<byte, float>(buf);
            for (int i = 0; i < rampSamples; i++)
                f[i] *= (float)((i + 1) / (double)rampSamples);
        }

        static void ApplyRampInInt16(Span<byte> buf, int ch, int sr, double seconds)
        {
            int samples = buf.Length / 2;
            int rampSamples = Math.Min(samples, (int)(sr * seconds) * ch);
            for (int i = 0; i < rampSamples; i++)
            {
                short v = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(i * 2, 2));
                int scaled = (int)Math.Round(v * ((i + 1) / (double)rampSamples));
                BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(i * 2, 2), (short)Math.Clamp(scaled, short.MinValue, short.MaxValue));
            }
        }

        static int PutData(int handle, byte[] buffer, int offset, int count)
        {
            if (count <= 0) return 0;
            if (offset == 0) return Bass.StreamPutData(handle, buffer, count);
            var gch = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var ptr = IntPtr.Add(gch.AddrOfPinnedObject(), offset);
                return Bass.StreamPutData(handle, ptr, count);
            }
            finally { gch.Free(); }
        }

        void SetState(PlaybackState s) { lock (_stateLock) { State = s; } }

        sealed class WavFormat
        {
            public int SampleRate;
            public int Channels;
            public int BitsPerSample;
            public bool IsIEEEFloat;
            public WavFormat(int sr, int ch, int bps, bool f) { SampleRate = sr; Channels = ch; BitsPerSample = bps; IsIEEEFloat = f; }
        }

        static bool TryConsumeWavHeaderSeekable(Stream s, out WavFormat fmt)
        {
            fmt = default!;
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
                bool isFloat = false;
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
                        br.ReadInt32(); br.ReadInt16();
                        bitsPerSample = br.ReadInt16();

                        if (len > 16)
                        {
                            short cbSize = br.ReadInt16();
                            if (audioFormat == unchecked((short)0xFFFE) && cbSize >= 22)
                            {
                                br.ReadInt16();
                                br.ReadInt32();
                                var guid = new Guid(br.ReadBytes(16));
                                var ieee = new Guid("00000003-0000-0010-8000-00AA00389B71");
                                isFloat = guid == ieee;
                                int consumed = 2 + 4 + 16;
                                int left = cbSize - consumed;
                                if (left > 0) br.ReadBytes(left);
                            }
                            else
                            {
                                if (cbSize > 0) br.ReadBytes(cbSize);
                            }
                        }
                        else isFloat = (audioFormat == 3);
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
                fmt = new WavFormat(sampleRate, channels, bitsPerSample, (audioFormat == 3) || isFloat);
                return true;
            }
            catch
            {
                s.Position = start;
                return false;
            }
        }

        static void TrySniffAndStripWavHeaderNonSeek(Stream s, Queue<byte[]> outQueue, out WavFormat? fmt)
        {
            fmt = null;
            using var ms = new MemoryStream();
            var tmp = new byte[4096];
            const int MaxHeaderBytes = 2 * 1024 * 1024; // Sicherheitslimit

            int dataStart = -1;
            short audioFormat = 1;
            short channels = 1;
            int sampleRate = 24000;
            short bitsPerSample = 16;
            bool isFloat = false;

            int total = 0;
            while (total < MaxHeaderBytes)
            {
                int n = s.Read(tmp, 0, tmp.Length);
                if (n <= 0) break;
                ms.Write(tmp, 0, n);
                total += n;

                var span = ms.GetBuffer().AsSpan(0, (int)ms.Length);

                // Kein RIFF/WAVE? Dann sofort alles als Rohdaten durchreichen.
                if (span.Length >= 12 &&
                    span.Slice(0, 4).SequenceEqual("RIFF"u8) &&
                    span.Slice(8, 4).SequenceEqual("WAVE"u8))
                {
                    int pos = 12;

                    while (pos + 8 <= span.Length)
                    {
                        var id = span.Slice(pos, 4); pos += 4;
                        if (pos + 4 > span.Length) break;

                        uint len = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos, 4));
                        pos += 4;

                        // Falls Chunk noch nicht vollständig im Puffer liegt -> mehr lesen
                        long next = (long)pos + len + (len % 2 == 1 ? 1 : 0); // Padding
                        if (next > span.Length) { pos -= 8; break; }

                        if (id.SequenceEqual("fmt "u8))
                        {
                            if (len >= 16)
                            {
                                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(pos, 2));
                                channels = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(pos + 2, 2));
                                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos + 4, 4));
                                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(pos + 14, 2));
                                isFloat = (audioFormat == 3);
                            }
                        }
                        else if (id.SequenceEqual("data"u8))
                        {
                            dataStart = pos;
                            fmt = new WavFormat(sampleRate, channels, bitsPerSample, isFloat);

                            int payloadLen = (int)ms.Length - dataStart;
                            if (payloadLen > 0)
                            {
                                var payload = new byte[payloadLen];
                                span.Slice(dataStart, payloadLen).CopyTo(payload);
                                outQueue.Enqueue(payload);
                            }
                            return; // Header vollständig entfernt
                        }

                        pos = (int)next; // zum nächsten Chunk springen (inkl. Padding)
                    }
                }
                else
                {
                    // Kein WAV → alles durchreichen
                    outQueue.Enqueue(ms.ToArray());
                    return;
                }
            }

            // data-Chuck nicht gefunden (zu exotisch/groß) → sicherheitshalber alles als Rohdaten
            outQueue.Enqueue(ms.ToArray());
        }
    }
}
