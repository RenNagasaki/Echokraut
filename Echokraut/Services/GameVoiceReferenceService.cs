using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using EchoScd;

namespace Echokraut.Services;

/// <inheritdoc cref="IGameVoiceReferenceService"/>
public sealed class GameVoiceReferenceService : IGameVoiceReferenceService
{
    /// <summary>How long a <c>/health</c> answer is trusted before the next line asks again.</summary>
    internal static readonly TimeSpan ProbeTtl = TimeSpan.FromMinutes(5);

    /// <summary>NPCs whose decoded reference stays in memory (§2.1: "z. B. 20 NPCs").</summary>
    internal const int LruCapacity = 20;

    internal const string AliasResource = "Echokraut.Resources.GameVoiceAliases.json";
    internal const string SplitsResource = "Echokraut.Resources.GameVoiceActorSplits.json";
    internal const string AliasOverrideFile = "game_voice_aliases.json";
    internal const string SplitsOverrideFile = "game_voice_actor_splits.json";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly Configuration _config;
    private readonly ILogService _log;
    private readonly IDatabaseService _db;
    private readonly IGameVoiceDataSource _data;
    private readonly IEchokrauTtsReferenceClient _client;
    private readonly Func<DateTime> _utcNow;
    private readonly CancellationTokenSource _cts = new();

    private readonly List<GameVoiceAliasEntry> _aliases;
    private readonly VoiceActorSplits _splits;

    // Text indexes per language (text only, built once on first use).
    private readonly object _indexLock = new();
    private readonly object _buildLock = new();
    private readonly Dictionary<ClientLanguage, LanguageIndex> _indexes = new();
    private readonly ConcurrentDictionary<ClientLanguage, byte> _indexBuildsPending = new();

    // Server probe.
    private readonly object _probeLock = new();
    private ProbeState _probe = ProbeState.Unknown;
    private bool _asrAvailable;
    private DateTime _probedUtc = DateTime.MinValue;
    private int _probeRunning;
    private GameVoiceAvailability _lastReported = GameVoiceAvailability.Disabled;

    // Ranking: one run at a time; each NPC queued at most once; NPCs without usable clips are
    // remembered for this session so every line of an unvoiced NPC doesn't re-scan.
    private readonly SemaphoreSlim _rankGate = new(1, 1);
    private readonly ConcurrentDictionary<(int, int), byte> _rankPending = new();
    private readonly ConcurrentDictionary<(int, int), byte> _noCandidates = new();

    private readonly ReferenceLru _lru = new(LruCapacity);
    private volatile bool _indexBuildRunning;
    private bool _disposed;

    public event Action<GameVoiceAvailability>? AvailabilityChanged;

    public GameVoiceReferenceService(
        Configuration config,
        ILogService log,
        IDatabaseService db,
        IGameVoiceDataSource data,
        IEchokrauTtsReferenceClient client,
        string configDirectory)
        : this(config, log, db, data, client, configDirectory, () => DateTime.UtcNow)
    {
    }

    internal GameVoiceReferenceService(
        Configuration config,
        ILogService log,
        IDatabaseService db,
        IGameVoiceDataSource data,
        IEchokrauTtsReferenceClient client,
        string configDirectory,
        Func<DateTime> utcNow)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));

        _aliases = LoadAliases(configDirectory);
        _splits = LoadSplits(configDirectory);
    }

    // ── Availability (§5.3) ─────────────────────────────────────────────────

    public GameVoiceAvailability Availability
    {
        get
        {
            if (!_config.GameVoiceReferenceEnabled) return GameVoiceAvailability.Disabled;
            if (_config.BackendSelection != TTSBackends.EchokrauTTS) return GameVoiceAvailability.WrongBackend;
            lock (_probeLock)
            {
                return _probe switch
                {
                    ProbeState.Supported => GameVoiceAvailability.Available,
                    ProbeState.Unsupported => GameVoiceAvailability.ServerUnsupported,
                    _ => GameVoiceAvailability.Unknown,
                };
            }
        }
    }

    public bool IsIndexBuildRunning => _indexBuildRunning;

    public async Task<GameVoiceAvailability> RefreshAvailabilityAsync(EKEventId eventId, CancellationToken ct = default)
    {
        if (!_config.GameVoiceReferenceEnabled || _config.BackendSelection != TTSBackends.EchokrauTTS)
            return Report(eventId);

        var health = await _client.GetHealthAsync(eventId, ct).ConfigureAwait(false);
        lock (_probeLock)
        {
            // Only an explicit true counts. A missing field is an older wrapper that would treat
            // ref_audio as an unknown field — and might write anything it gets to disk.
            _probe = health?.inline_reference == true ? ProbeState.Supported : ProbeState.Unsupported;
            _asrAvailable = health?.asr_check == true;
            _probedUtc = _utcNow();
        }
        return Report(eventId);
    }

    public void ReportReferenceRejected(EKEventId eventId)
    {
        lock (_probeLock)
        {
            _probe = ProbeState.Unknown;
            _probedUtc = DateTime.MinValue;
        }
        _log.Warning(nameof(ReportReferenceRejected),
            "EchokrauTTS rejected the game-voice reference; using the voice pack and asking /health again.", eventId);
        Report(eventId);
    }

    /// <summary>Logs a state change once ("einmalige Info", §5.3) and raises the event.</summary>
    private GameVoiceAvailability Report(EKEventId eventId)
    {
        var now = Availability;
        bool changed;
        lock (_probeLock)
        {
            changed = now != _lastReported;
            _lastReported = now;
        }
        if (!changed) return now;
        if (now == GameVoiceAvailability.ServerUnsupported)
            _log.Info(nameof(Availability),
                "EchokrauTTS does not support in-memory game-voice references (/health inline_reference missing or false) — using the voice pack.",
                eventId);
        else if (now == GameVoiceAvailability.Available)
            _log.Info(nameof(Availability), "Game-voice reference available.", eventId);
        AvailabilityChanged?.Invoke(now);
        return now;
    }

    private void EnsureFreshProbe(EKEventId eventId)
    {
        bool stale;
        lock (_probeLock) stale = _probe == ProbeState.Unknown || _utcNow() - _probedUtc > ProbeTtl;
        if (!stale || Interlocked.Exchange(ref _probeRunning, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await RefreshAvailabilityAsync(eventId, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* disposed */ }
            catch (Exception ex) { _log.Warning(nameof(EnsureFreshProbe), ex.Message, eventId); }
            finally { Interlocked.Exchange(ref _probeRunning, 0); }
        });
    }

    // ── Live path ───────────────────────────────────────────────────────────

    public InlineVoiceReference? TryGetReference(GameVoiceSpeaker speaker, EKEventId eventId)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (_disposed || speaker.CharacterId <= 0 || string.IsNullOrWhiteSpace(speaker.Name)) return null;

        var availability = Availability;
        if (availability is GameVoiceAvailability.Disabled or GameVoiceAvailability.WrongBackend) return null;
        EnsureFreshProbe(eventId);
        if (availability != GameVoiceAvailability.Available) return null;

        var key = Key(speaker);
        if (_lru.TryGet(key, out var cached)) return cached;

        var rows = _db.GetGameVoiceReferences(speaker.CharacterId, (int)speaker.Language);
        if (rows.Count == 0 || rows.Any(r => r.IndexVersion != GameVoiceRanking.IndexVersion))
        {
            QueueRanking(speaker, eventId);
            return null;
        }

        var index = TryGetBuiltIndex(speaker.Language);
        if (index == null)
        {
            // The clip ids are known, their game text is not yet: build the text index in the
            // background, this line uses the voice pack.
            QueueIndexBuild(speaker.Language, eventId);
            return null;
        }

        var clips = new List<DecodedReferenceClip>(rows.Count);
        foreach (var row in rows)
        {
            var pcm = index.LinesByBase.TryGetValue(row.AudioBase, out var line)
                ? _data.TryDecodeVoice(row.AudioBase, speaker.Language)
                : null;
            if (line == null || pcm == null)
            {
                // §2.6: a clip a patch removed (or whose text changed) invalidates the index.
                _log.Info(nameof(TryGetReference),
                    $"Stored game-voice clip {row.AudioBase} is gone; re-ranking character {speaker.CharacterId}.", eventId);
                _db.ReplaceGameVoiceReferences(speaker.CharacterId, (int)speaker.Language, Array.Empty<GameVoiceReferenceEntity>());
                QueueRanking(speaker, eventId);
                return null;
            }
            clips.Add(new DecodedReferenceClip(row.AudioBase, line.Text, ToMono24k(pcm)));
        }

        var reference = InlineReferenceAssembler.Assemble(clips);
        if (reference != null) _lru.Put(key, reference);
        return reference;
    }

    private void QueueRanking(GameVoiceSpeaker speaker, EKEventId eventId)
    {
        if (!_config.GameVoiceRankOnConversationStart) return;
        var key = Key(speaker);
        if (_noCandidates.ContainsKey(key) || !_rankPending.TryAdd(key, 0)) return;
        _ = Task.Run(async () =>
        {
            try { await RankAsync(speaker, eventId, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* disposed */ }
            catch (Exception ex) { _log.Warning(nameof(QueueRanking), ex.ToString(), eventId); }
            finally { _rankPending.TryRemove(key, out _); }
        });
    }

    private void QueueIndexBuild(ClientLanguage language, EKEventId eventId)
    {
        if (!_indexBuildsPending.TryAdd(language, 0)) return;
        _ = Task.Run(() =>
        {
            try { GetOrBuildIndex(language, _cts.Token, eventId); }
            catch (OperationCanceledException) { /* disposed */ }
            catch (Exception ex) { _log.Warning(nameof(QueueIndexBuild), ex.ToString(), eventId); }
            finally { _indexBuildsPending.TryRemove(language, out _); }
        });
    }

    // ── Ranking (§4) ────────────────────────────────────────────────────────

    public async Task<int> RankAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (speaker.CharacterId <= 0 || string.IsNullOrWhiteSpace(speaker.Name)) return 0;

        await _rankGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RankLockedAsync(speaker, eventId, ct).ConfigureAwait(false);
        }
        finally
        {
            _rankGate.Release();
        }
    }

    private async Task<int> RankLockedAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct)
    {
        var language = speaker.Language;
        var langCode = LanguageCode(language);
        var index = GetOrBuildIndex(language, ct, eventId);

        var lines = CandidateLines(speaker, index);
        var npcName = speaker.Name.Trim();
        var epoch = _splits.LatestEpoch(npcName, langCode);
        var candidates = lines
            .Where(l => _splits.IsLatestEpoch(npcName, langCode, l.AudioBase))          // F4: newest actor only
            .Where(l => SpeechTextFilter.IsSpeech(l.Text, langCode))                     // §4 text, hard
            .OrderByDescending(l => l.AudioBase, StringComparer.Ordinal)                 // newest patch first
            .Take(GameVoiceRanking.MaxDecodedCandidates)
            .ToList();

        var scored = new List<ScoredClip>();
        foreach (var line in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var pcm = _data.TryDecodeVoice(line.AudioBase, language);
            if (pcm == null) continue;
            var seconds = pcm.Seconds;
            if (!GameVoiceRanking.IsAcceptableDuration(seconds)) continue;              // §4 duration, hard
            var metrics = SignalAnalyzer.Analyze(pcm);
            var gate = SignalAnalyzer.Check(metrics);
            scored.Add(new ScoredClip(line, seconds,
                GameVoiceRanking.PreScore(line.Text, seconds, langCode, metrics, gate)));
            // pcm goes out of scope here — the top clips are decoded again for ASR below rather
            // than holding up to 40 clips in memory.
        }

        var top = scored.OrderByDescending(s => s.PreScore).Take(GameVoiceRanking.AsrTopN).ToList();
        bool asr;
        lock (_probeLock) asr = _asrAvailable;
        foreach (var clip in top)
        {
            double? wer = null;
            if (asr)
            {
                var pcm = _data.TryDecodeVoice(clip.Line.AudioBase, language);
                var heard = pcm == null
                    ? null
                    : await _client.AsrCheckAsync(eventId, ToMono24k(pcm), langCode.ToLowerInvariant(), ct).ConfigureAwait(false);
                if (heard != null) wer = WordErrorRate.Compute(clip.Line.Text, heard, langCode);
            }
            clip.FinalScore = GameVoiceRanking.FinalScore(clip.PreScore, wer);
        }

        var chosen = Choose(top);
        var now = _utcNow();
        var rows = chosen.Select((c, i) => new GameVoiceReferenceEntity
        {
            CharacterId = speaker.CharacterId,
            Language = (int)language,
            Epoch = epoch,
            AudioBase = c.Line.AudioBase,
            Rank = i + 1,
            Score = c.FinalScore,
            IndexVersion = GameVoiceRanking.IndexVersion,
            CreatedUtc = now,
        }).ToList();

        var key = Key(speaker);
        _db.ReplaceGameVoiceReferences(speaker.CharacterId, (int)language, rows);
        _lru.Remove(key);
        if (rows.Count == 0) _noCandidates.TryAdd(key, 0);
        else _noCandidates.TryRemove(key, out _);

        _log.Info(nameof(RankAsync),
            $"Game-voice ranking for character {speaker.CharacterId} ({langCode}): {lines.Count} line(s), " +
            $"{candidates.Count} candidate(s), {scored.Count} usable, {rows.Count} stored" +
            (asr ? "" : " (no ASR check)"), eventId);
        return rows.Count;
    }

    /// <summary>
    /// Best clip always; up to two more spread over duration (§4 "Diversität") from the rest of the
    /// eligible top clips, all above <see cref="GameVoiceRanking.MinScore"/>; returned best first.
    /// </summary>
    private static List<ScoredClip> Choose(List<ScoredClip> top)
    {
        var eligible = top.Where(c => c.FinalScore >= GameVoiceRanking.MinScore)
            .OrderByDescending(c => c.FinalScore).ToList();
        if (eligible.Count == 0) return eligible;
        var rest = SampleSelector.PickDiverse(eligible.Skip(1).ToList(), GameVoiceRanking.MaxReferences - 1, c => c.Seconds);
        return new[] { eligible[0] }.Concat(rest.OrderByDescending(c => c.FinalScore)).ToList();
    }

    /// <summary>
    /// The lines that are "the same NPC" (§2.2, §3, F2): resolve the speaker to ENpcResident ids
    /// (live base id, the character's DB instances, exact client-language name), take their English
    /// names, and ask the speaker map which tokens those names own.
    /// </summary>
    internal List<GameVoiceLine> CandidateLines(GameVoiceSpeaker speaker, LanguageIndex index)
    {
        var ids = new HashSet<uint>();
        if (speaker.NpcBaseId != 0) ids.Add(speaker.NpcBaseId);
        foreach (var instance in _db.GetInstancesForCharacter(speaker.CharacterId))
            if (instance.NpcBaseId > 0) ids.Add((uint)instance.NpcBaseId);
        var name = speaker.Name.Trim();
        foreach (var (id, clientName) in index.ClientNames)
            if (string.Equals(clientName, name, StringComparison.OrdinalIgnoreCase)) ids.Add(id);

        var englishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            if (index.EnglishNames.TryGetValue(id, out var en)) englishNames.Add(en);
        if (speaker.Language == ClientLanguage.English) englishNames.Add(name);

        var result = new List<GameVoiceLine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in index.Speakers.TokensFor(englishNames))
            foreach (var line in index.Text.LinesFor(token))
                if (seen.Add(line.AudioBase)) result.Add(line);
        return result;
    }

    // ── Batch (F5) ──────────────────────────────────────────────────────────

    public async Task<int> BuildIndexAsync(ClientLanguage language, IProgress<(int Done, int Total)>? progress, CancellationToken ct = default)
    {
        if (_indexBuildRunning) return 0;
        _indexBuildRunning = true;
        try
        {
            var eventId = new EKEventId(0, TextSource.None);
            if (Availability != GameVoiceAvailability.Available)
                await RefreshAvailabilityAsync(eventId, ct).ConfigureAwait(false);
            if (Availability != GameVoiceAvailability.Available)
            {
                _log.Info(nameof(BuildIndexAsync), $"Not building the game-voice index: {Availability}.", eventId);
                return 0;
            }

            var npcs = _db.GetNpcs()
                .Where(c => c.Language == (int)language && !string.IsNullOrWhiteSpace(c.Name))
                .Where(c =>
                {
                    var rows = _db.GetGameVoiceReferences(c.Id, (int)language);
                    return rows.Count == 0 || rows.Any(r => r.IndexVersion != GameVoiceRanking.IndexVersion);
                })
                .ToList();

            var indexed = 0;
            for (var i = 0; i < npcs.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var c = npcs[i];
                if (await RankAsync(new GameVoiceSpeaker(c.Id, c.Name, 0, language), eventId, ct).ConfigureAwait(false) > 0)
                    indexed++;
                progress?.Report((i + 1, npcs.Count));
            }
            _log.Info(nameof(BuildIndexAsync), $"Game-voice index: {indexed} of {npcs.Count} NPC(s) got a reference.", eventId);
            return indexed;
        }
        finally
        {
            _indexBuildRunning = false;
        }
    }

    public void ClearMemory()
    {
        _lru.Clear();
        _noCandidates.Clear();
        lock (_indexLock) _indexes.Clear();
    }

    // ── Text index (§2.2) ───────────────────────────────────────────────────

    internal sealed record LanguageIndex(
        GameVoiceTextIndex Text,
        GameVoiceSpeakerMap Speakers,
        IReadOnlyDictionary<string, GameVoiceLine> LinesByBase,
        IReadOnlyDictionary<uint, string> ClientNames,
        IReadOnlyDictionary<uint, string> EnglishNames);

    private LanguageIndex? TryGetBuiltIndex(ClientLanguage language)
    {
        lock (_indexLock) return _indexes.TryGetValue(language, out var idx) ? idx : null;
    }

    internal LanguageIndex GetOrBuildIndex(ClientLanguage language, CancellationToken ct, EKEventId eventId)
    {
        if (TryGetBuiltIndex(language) is { } ready) return ready;
        // Building scans every cutscene sheet (seconds). It runs under its own lock so the live
        // path's TryGetBuiltIndex (dictionary lock only) never waits for it.
        lock (_buildLock)
        {
            if (TryGetBuiltIndex(language) is { } existing) return existing;

            var text = GameVoiceTextIndex.Build(_data.EnumerateCutsceneRows(language, ct));
            var english = _data.GetNpcNames(ClientLanguage.English);
            var client = language == ClientLanguage.English ? english : _data.GetNpcNames(language);
            var speakers = GameVoiceSpeakerMap.Build(text.Tokens, english, _aliases, out var warnings);
            foreach (var w in warnings) _log.Warning(nameof(GetOrBuildIndex), w, eventId);

            var byBase = new Dictionary<string, GameVoiceLine>(StringComparer.Ordinal);
            foreach (var token in text.Tokens)
                foreach (var line in text.LinesFor(token))
                    byBase[line.AudioBase] = line;

            var built = new LanguageIndex(text, speakers, byBase, client, english);
            lock (_indexLock) _indexes[language] = built;
            _log.Info(nameof(GetOrBuildIndex),
                $"Game-voice text index ({LanguageCode(language)}): {text.LineCount} voiced line(s).", eventId);
            return built;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (int, int) Key(GameVoiceSpeaker s) => (s.CharacterId, (int)s.Language);

    /// <summary>echoscd language code (also what <c>SpeechTextFilter</c> and the splits file use).</summary>
    internal static string LanguageCode(ClientLanguage language) => language switch
    {
        ClientLanguage.German => "DE",
        ClientLanguage.French => "FR",
        ClientLanguage.Japanese => "JA",
        _ => "EN",
    };

    /// <summary>The data source already resamples to 24 kHz mono; this guards a source that does not.</summary>
    private static short[] ToMono24k(PcmBuffer pcm) =>
        pcm.Channels == 1 && pcm.SampleRate == InlineVoiceReference.SampleRate
            ? pcm.Samples
            : PcmResampler.ToMono(pcm, InlineVoiceReference.SampleRate).Samples;

    private List<GameVoiceAliasEntry> LoadAliases(string configDirectory)
    {
        var list = new List<GameVoiceAliasEntry>();
        foreach (var file in LoadLayered<GameVoiceAliasFile>(AliasResource, configDirectory, AliasOverrideFile))
            list.AddRange(file.Aliases ?? new List<GameVoiceAliasEntry>());
        return list;
    }

    private VoiceActorSplits LoadSplits(string configDirectory)
    {
        var merged = new VoiceActorSplitsFile { Version = 2 };
        foreach (var file in LoadLayered<VoiceActorSplitsFile>(SplitsResource, configDirectory, SplitsOverrideFile))
            merged.Splits.AddRange(file.Splits ?? new List<VoiceActorSplitEntry>());
        var splits = VoiceActorSplits.Build(merged, out var warnings);
        foreach (var w in warnings) _log.Warning(nameof(LoadSplits), w, new EKEventId(0, TextSource.None));
        return splits;
    }

    /// <summary>Embedded resource first, then the local override in the plugin config dir (later wins).</summary>
    private IEnumerable<T> LoadLayered<T>(string resource, string configDirectory, string overrideFile) where T : class
    {
        var result = new List<T>();
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream != null && JsonSerializer.Deserialize<T>(stream, JsonOpts) is { } embedded) result.Add(embedded);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadLayered), $"Embedded {resource} unreadable: {ex.Message}", new EKEventId(0, TextSource.None));
        }

        if (string.IsNullOrEmpty(configDirectory)) return result;
        var path = Path.Combine(configDirectory, overrideFile);
        if (!File.Exists(path)) return result;
        try
        {
            if (JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts) is { } local) result.Add(local);
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(LoadLayered), $"{path} unreadable, ignored: {ex.Message}", new EKEventId(0, TextSource.None));
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        ClearMemory();
    }

    private enum ProbeState { Unknown, Supported, Unsupported }

    private sealed class ScoredClip
    {
        public ScoredClip(GameVoiceLine line, double seconds, double preScore)
        {
            Line = line;
            Seconds = seconds;
            PreScore = preScore;
            FinalScore = preScore;
        }

        public GameVoiceLine Line { get; }
        public double Seconds { get; }
        public double PreScore { get; }
        public double FinalScore { get; set; }
    }

    /// <summary>Tiny thread-safe LRU of assembled references (decoded PCM, RAM only).</summary>
    private sealed class ReferenceLru
    {
        private readonly int _capacity;
        private readonly object _lock = new();
        private readonly LinkedList<((int, int) Key, InlineVoiceReference Value)> _order = new();
        private readonly Dictionary<(int, int), LinkedListNode<((int, int) Key, InlineVoiceReference Value)>> _map = new();

        public ReferenceLru(int capacity) => _capacity = capacity;

        public bool TryGet((int, int) key, out InlineVoiceReference value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }
            value = null!;
            return false;
        }

        public void Put((int, int) key, InlineVoiceReference value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var existing)) _order.Remove(existing);
                var node = _order.AddFirst((key, value));
                _map[key] = node;
                while (_map.Count > _capacity && _order.Last is { } last)
                {
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }
        }

        public void Remove((int, int) key)
        {
            lock (_lock)
                if (_map.Remove(key, out var node)) _order.Remove(node);
        }

        public void Clear()
        {
            lock (_lock) { _map.Clear(); _order.Clear(); }
        }
    }
}
