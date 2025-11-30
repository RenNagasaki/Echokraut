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
using Echokraut.Helper.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
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
    internal Vector3D ListenerPosition => _listenerPosSmoothed;

    readonly ConcurrentDictionary<Guid, Source> _sources = new();

    Timer? _listenerTimer;
    int _listenerIntervalMs = 8;
    Vector3D _listenerFront = new(0, 0, 1);
    Vector3D _listenerTop   = new(0, 1, 0);
    Vector3D _listenerPosSmoothed;
    Vector3D _listenerPosLast;
    long _listenerLastTicks;
    const float _listenerSmoothHz = 30f;

    public Live3DAudioEngine(int deviceIndex = -1) => _deviceIndex = deviceIndex;

    public void ConfigureListener(Vector3D position, Vector3D front, Vector3D top,
                                  float distanceFactor = 1f, float rolloffFactor = 1f, float dopplerFactor = 1f)
    {
        EnsureInit();
        _distanceFactor = distanceFactor;
        _dopplerFactor = dopplerFactor;

        NormalizeOrthonormalize(ref front, ref top);
        _listenerFront = front;
        _listenerTop   = top;
        _listenerPosSmoothed = position;
        _listenerPosLast     = position;
        _listenerLastTicks   = Stopwatch.GetTimestamp();

        Bass.Set3DPosition(position, new Vector3D(), front, top);
        Bass.Set3DFactors(distanceFactor, rolloffFactor, dopplerFactor);
        Bass.Apply3D();

        _listenerTimer?.Dispose();
        _listenerTimer = new Timer(_ => ListenerTick(), null, 0, _listenerIntervalMs);
    }

    public void SetListenerPollInterval(int intervalMs)
    {
        _listenerIntervalMs = Math.Max(4, intervalMs);
        if (_listenerTimer != null) _listenerTimer.Change(0, _listenerIntervalMs);
    }

    static void NormalizeOrthonormalize(ref Vector3D front, ref Vector3D top)
    {
        front = Normalize(front);
        var right = Normalize(Cross(front, top));
        top = Normalize(Cross(right, front));

        static Vector3D Normalize(Vector3D v) {
            var len = MathF.Sqrt(v.X*v.X + v.Y*v.Y + v.Z*v.Z);
            return len > 1e-6f ? new Vector3D(v.X/len, v.Y/len, v.Z/len) : new Vector3D(0,1,0);
        }
        static Vector3D Cross(Vector3D a, Vector3D b)
            => new Vector3D(a.Y*b.Z - a.Z*b.Y, a.Z*b.X - a.X*b.Z, a.X*b.Y - a.Y*b.X);
    }

    unsafe void ListenerTick()
    {
        if (!_inited) return;

        if (DalamudHelper.Camera == null && CameraManager.Instance() != null)
            DalamudHelper.Camera = CameraManager.Instance()->GetActiveCamera();

        if (DalamudHelper.LocalPlayer == null)
            Plugin.Framework.RunOnFrameworkThread(() => DalamudHelper.LocalPlayer = Plugin.ClientState.LocalPlayer);

        if (DalamudHelper.Camera != null && DalamudHelper.LocalPlayer != null)
        {
            var matrix = DalamudHelper.Camera->CameraBase.SceneCamera.ViewMatrix;
            _listenerFront = new Vector3D(matrix[2], matrix[1], matrix[0]);

            var p = DalamudHelper.LocalPlayer.Position;
            var target = new Vector3D(p.X, p.Y, p.Z);

            var now = Stopwatch.GetTimestamp();
            var dt = Math.Max(1e-4f, (float)(now - _listenerLastTicks) / Stopwatch.Frequency);

            float alpha = 1f - MathF.Exp(-dt * _listenerSmoothHz);
            _listenerPosSmoothed = Lerp(_listenerPosSmoothed, target, alpha);

            var velUnitsPerSec = Scale(Sub(_listenerPosSmoothed, _listenerPosLast), 1f / dt);
            var velMetersPerSec = Scale(velUnitsPerSec, 1f / _distanceFactor);

            _listenerPosLast = _listenerPosSmoothed;
            _listenerLastTicks = now;

            Bass.Set3DPosition(_listenerPosSmoothed, velMetersPerSec, _listenerFront, _listenerTop);
            Bass.Apply3D();

            static Vector3D Sub(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            static Vector3D Scale(Vector3D a, float s) => new(a.X * s, a.Y * s, a.Z * s);
            static Vector3D Lerp(Vector3D a, Vector3D b, float t)
                => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }
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

        pollIntervalMs = Math.Min(pollIntervalMs, _listenerIntervalMs);

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
    public void SetSourcePoller(Guid id, Func<Vector3D> provider, int intervalMs = 8)
    {
        intervalMs = Math.Min(intervalMs, _listenerIntervalMs);
        if (_sources.TryGetValue(id, out var s)) s.Set3DSourcePoller(provider, intervalMs);
    }

    void EnsureInit()
    {
        if (_inited) return;

        Bass.Configure(Configuration.UpdatePeriod, 10);
        Bass.Configure(Configuration.DeviceBufferLength, 120);
        Bass.Configure(Configuration.SRCQuality, 4);

        if (!Bass.Init(_deviceIndex, 48000, DeviceInitFlags.Default | DeviceInitFlags.Device3D) &&
            Bass.LastError != Errors.Already)
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
        _listenerTimer?.Dispose();
        _listenerTimer = null;
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
        const float _smoothHz = 20f;

        int _dsp;
        DSPProcedure? _volDsp;
        bool _outIsFloat;

        readonly Queue<byte[]> _prefetchQueue = new();
        bool _headerChecked;

        int _rampSamplesLeft = 0;
        int _rampTotalSamples = 0;
        
        const float _coincidentEps = 0.02f; 
        bool _coincidentMode = false; 
        
        bool _allowEndSignal = false;
        bool _everWrote = false;

        void Set3DEnabled(bool enabled)
        {
            if (_h == 0) return;

            if (enabled)
            {
                Bass.ChannelFlags(_h, BassFlags.Bass3D, BassFlags.Bass3D);
                Bass.ChannelSetAttribute(_h, ChannelAttribute.Pan, 0f);
            }
            else
            {
                Bass.ChannelFlags(_h, 0, BassFlags.Bass3D);
                Bass.ChannelSetAttribute(_h, ChannelAttribute.Pan, 0f);
            }
        }

        Vector3D GetListenerPos() => _engine.ListenerPosition;

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
                    if (_h != 0) AttachVolumeDspIfNeeded();
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

        bool HasStartworthyPayload()
        {
            int frameOut = _bytesPerSampleOut * _ch;
            int minMs = 40;
            int minBytes = Math.Max(frameOut, _sr * frameOut * minMs / 1000);
            int avail = Bass.ChannelGetData(_h, IntPtr.Zero, (int)DataFlags.Available);
            return avail >= minBytes;
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

            Bass.ChannelSet3DAttributes(_h, ManagedBass.Mode3D.Normal, 1f, 0f, -1, -1, 0);

            var channelBufferMs = MathF.Max(_bufferMs, 450);
            Bass.ChannelSetAttribute(_h, ChannelAttribute.Buffer, channelBufferMs / 1000f);

            Bass.ChannelSetAttribute(_h, ChannelAttribute.Volume, 1f);
            Bass.ChannelSet3DPosition(_h, _srcPos, null, new Vector3D());
            Bass.Apply3D();
            AttachVolumeDspIfNeeded();

            _endSync = OnEndSync;
            if (Bass.ChannelSetSync(_h, SyncFlags.End, 0, _endSync) == 0)
                throw new InvalidOperationException($"ChannelSetSync(End) failed: {Bass.LastError}");

            _lastPos = _srcPos;
            _smoothPos = _srcPos;
            _lastTicks = Stopwatch.GetTimestamp();
            _reader = Task.Run(ReadLoop, _cts.Token);
            _writer = Task.Run(WriteLoop, _cts.Token);
            PrebufferAndStartWithFadeIn();

            if (!HasStartworthyPayload())
            {
                try { _cts.Cancel(); _q.CompleteAdding(); } catch {}
                try { if (_h != 0) { Bass.ChannelStop(_h); Bass.StreamFree(_h); } } catch {}
                _h = 0;
                SetState(PlaybackState.Stopped);
                ThreadPool.QueueUserWorkItem(_ => _engine.OnSourceEnded(_id));
                return;
            }

            if (!Bass.ChannelPlay(_h, false))
                throw new InvalidOperationException($"ChannelPlay failed: {Bass.LastError}");

            {
                var L = GetListenerPos();
                var dx = _srcPos.X - L.X;
                var dy = _srcPos.Y - L.Y;
                var dz = _srcPos.Z - L.Z;
                var dist = MathF.Sqrt(dx*dx + dy*dy + dz*dz);
                _coincidentMode = dist < _coincidentEps;
                Set3DEnabled(!_coincidentMode);
            }

            int rampMs = 40;
            _rampTotalSamples = Math.Max(1, _sr * rampMs / 1000 * _ch);
            _rampSamplesLeft  = _rampTotalSamples;

            _allowEndSignal = true;
            
            SetState(PlaybackState.Playing);
        }

        void PrebufferAndStartWithFadeIn()
        {
            int frameOut = _bytesPerSampleOut * _ch;

            int silenceMs    = 10;
            int silenceBytes = Math.Max(frameOut, _sr * frameOut * silenceMs / 1000);
            PushSilence(silenceBytes);

            int targetMs   = 320;
            int targetByte = Math.Max(frameOut, _sr * frameOut * targetMs / 1000);

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000)
            {
                int avail = Bass.ChannelGetData(_h, IntPtr.Zero, (int)DataFlags.Available);
                if (avail >= targetByte) break;
                Thread.Sleep(5);
            }
        }

        void PushSilence(int bytes)
        {
            if (bytes <= 0 || _h == 0) return;
            var zero = new byte[bytes];
            int off = 0;
            while (off < zero.Length)
            {
                int wrote = PutData(_h, zero, off, zero.Length - off);
                if (wrote < 0)
                {
                    var err = Bass.LastError;
                    if (err == Errors.Ended || err == Errors.Handle)
                        break;
                    throw new InvalidOperationException($"StreamPutData(silence) failed: {err}");
                }
                if (wrote == 0) { Thread.Sleep(1); continue; }
                off += wrote;
            }
        }

        void AttachVolumeDspIfNeeded()
        {
            if (_h == 0 || _dsp != 0) return;

            var info = Bass.ChannelGetInfo(_h);
            _outIsFloat = (info.Flags & BassFlags.Float) != 0;

            _volDsp = new DSPProcedure((handle, channel, buffer, length, user) =>
            {
                float vol = _volume;

                if (_rampSamplesLeft > 0)
                {
                    if (_outIsFloat)
                    {
                        int n = length / 4;
                        unsafe
                        {
                            float* f = (float*)buffer;
                            for (int i = 0; i < n; i++)
                            {
                                float k = 1f;
                                if (_rampSamplesLeft > 0)
                                {
                                    int done = _rampTotalSamples - _rampSamplesLeft;
                                    float t = (_rampTotalSamples > 0) ? (done / (float)_rampTotalSamples) : 1f;
                                    k = t;
                                    _rampSamplesLeft--;
                                }
                                f[i] *= (vol * k);
                            }
                        }
                        return;
                    }
                    else
                    {
                        int n = length / 2;
                        unsafe
                        {
                            short* s = (short*)buffer;
                            for (int i = 0; i < n; i++)
                            {
                                float k = 1f;
                                if (_rampSamplesLeft > 0)
                                {
                                    int done = _rampTotalSamples - _rampSamplesLeft;
                                    float t = (_rampTotalSamples > 0) ? (done / (float)_rampTotalSamples) : 1f;
                                    k = t;
                                    _rampSamplesLeft--;
                                }
                                int v = (int)(s[i] * (vol * k));
                                if (v > short.MaxValue) v = short.MaxValue;
                                else if (v < short.MinValue) v = short.MinValue;
                                s[i] = (short)v;
                            }
                        }
                        return;
                    }
                }

                if (vol >= 0.9999f) return;
                if (vol <= 0.0001f)
                {
                    unsafe { System.Runtime.CompilerServices.Unsafe.InitBlockUnaligned((void*)buffer, 0, (uint)length); }
                    return;
                }

                if (_outIsFloat)
                {
                    int n = length / 4;
                    unsafe
                    {
                        float* f = (float*)buffer;
                        for (int i = 0; i < n; i++) f[i] *= vol;
                    }
                }
                else
                {
                    int n = length / 2;
                    unsafe
                    {
                        short* s = (short*)buffer;
                        for (int i = 0; i < n; i++)
                        {
                            int v = (int)(s[i] * vol);
                            if (v > short.MaxValue) v = short.MaxValue;
                            else if (v < short.MinValue) v = short.MinValue;
                            s[i] = (short)v;
                        }
                    }
                }
            });

            _dsp = Bass.ChannelSetDSP(_h, _volDsp, IntPtr.Zero, 10);
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
            Task? reader, writer;
            int h, dsp;
            Stream src;
            bool leaveOpen;

            lock (_stateLock)
            {
                if (State == PlaybackState.Stopped)
                    return;

                _playGate.Set();
                _pollTimer?.Dispose();
                _pollTimer = null;

                _cts.Cancel();
                _q.CompleteAdding();

                SetState(PlaybackState.Stopped);

                reader    = _reader;    _reader = null;
                writer    = _writer;    _writer = null;
                h         = _h;         _h = 0;
                dsp       = _dsp;       _dsp = 0; _volDsp = null;
                src       = _source;
                leaveOpen = _leaveOpen;
            }

            if (h != 0)
            {
                try { Bass.ChannelStop(h); } catch { }
                try { Bass.ChannelRemoveSync(h, 0); } catch { }
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await SafeAwait(reader);
                    await SafeAwait(writer);
                }
                catch { }

                try { if (dsp != 0 && h != 0) Bass.ChannelRemoveDSP(h, dsp); } catch { }
                try { if (h != 0) Bass.StreamFree(h); } catch { }

                try { _cts.Dispose(); } catch { }
                if (!leaveOpen)
                {
                    try { src.Dispose(); } catch { }
                }
            });
        }

        static Task SafeAwait(Task? t) => t is null ? Task.CompletedTask :
                                          t.IsCompleted ? Task.CompletedTask : t.ContinueWith(_ => { }, TaskScheduler.Default);

        public void Dispose()
        {
            try { _pollTimer?.Dispose(); } catch { }
            if (_dsp != 0) { try { Bass.ChannelRemoveDSP(_h, _dsp); } catch { } _dsp = 0; _volDsp = null; }
            if (_h != 0) { try { Bass.StreamFree(_h); } catch { } _h = 0; }
            _cts.Cancel();
            _cts.Dispose();
            if (!_leaveOpen) _source.Dispose();
        }

        public void Set3DSourcePosition(Vector3D position)
        {
            var now = Stopwatch.GetTimestamp();
            var dt = Math.Max(1e-4f, (float)(now - _lastTicks) / Stopwatch.Frequency);

            float alpha = 1f - MathF.Exp(-dt * _smoothHz);
            _smoothPos = Add(_smoothPos, Scale(Sub(position, _smoothPos), alpha));

            var velUnitsPerSec = Scale(Sub(_smoothPos, _lastPos), 1f / dt);
            var velMetersPerSec = Scale(velUnitsPerSec, 1f / _engine.DistanceFactor);

            _lastPos = _smoothPos;
            _lastTicks = now;

            if (_h != 0)
            {
                var L = GetListenerPos();
                var dx = _smoothPos.X - L.X;
                var dy = _smoothPos.Y - L.Y;
                var dz = _smoothPos.Z - L.Z;
                var dist = MathF.Sqrt(dx*dx + dy*dy + dz*dz);

                bool wantCoincident = dist < _coincidentEps;

                if (wantCoincident != _coincidentMode)
                {
                    _coincidentMode = wantCoincident;
                    Set3DEnabled(!_coincidentMode);
                }

                if (!_coincidentMode)
                {
                    Bass.ChannelSet3DPosition(_h, _smoothPos, null, velMetersPerSec);
                }

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
                        _headerChecked = true;

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
                        await PushAll(outBuf).ConfigureAwait(false);
                    }
                    else if (_isIEEEFloatIn)
                    {
                        await PushAll(chunk).ConfigureAwait(false);
                    }
                    else
                    {
                        await PushAll(chunk).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_allowEndSignal && _everWrote && !_cts.IsCancellationRequested && _h != 0)
                    try { Bass.StreamPutData(_h, IntPtr.Zero, (int)StreamProcedureType.End); } catch { }
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
                _everWrote = true;
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
            const int MaxHeaderBytes = 2 * 1024 * 1024;

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

                        long next = (long)pos + len + (len % 2 == 1 ? 1 : 0);
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
                            return;
                        }

                        pos = (int)next;
                    }
                }
                else
                {
                    if (ms.Length > 0) outQueue.Enqueue(ms.ToArray());
                    return;
                }
            }

            if (ms.Length > 0) outQueue.Enqueue(ms.ToArray());
        }
    }
}
