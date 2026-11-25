using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Helper.Functional.GameVoice;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using EchoScd;

namespace Echokraut.Services;

/// <inheritdoc cref="IGameVoiceReferenceService"/>
/// <remarks>
/// Phase 6 (plan game-voice-reference §11): the index lives per <b>speaker token + voice-actor
/// epoch</b>, not per character. A speaker resolves to the tokens it owns (alias file, exact
/// name), each token to the epoch its story progress selects (<see cref="IStoryProgressService"/>),
/// and the index rows of those (token, epoch) pairs give the clips. Ranking a pair happens in the
/// batch for every voiced token, or live as a fallback for pairs without a current index.
/// </remarks>
public sealed class GameVoiceReferenceService : IGameVoiceReferenceService
{
    /// <summary>How long a <c>/health</c> answer is trusted before the next line asks again.</summary>
    internal static readonly TimeSpan ProbeTtl = TimeSpan.FromMinutes(5);

    /// <summary>Speaker selections whose decoded reference stays in memory (§2.1: "z. B. 20 NPCs").</summary>
    internal const int LruCapacity = 20;

    /// <summary>
    /// How long the FIRST line of a speaker without an index waits for the quick ranking stage
    /// before it falls back to the voice pack (2026-11-15). The batch index makes this rare.
    /// </summary>
    internal static readonly TimeSpan DefaultFirstLineWait = TimeSpan.FromSeconds(4);

    /// <summary><see cref="DefaultFirstLineWait"/>; settable so tests need not wait seconds.</summary>
    internal TimeSpan FirstLineWait { get; set; } = DefaultFirstLineWait;

    /// <summary>
    /// Test seam: runs in <see cref="GetReferenceAsync"/> between the lookup and the check for
    /// pending work — the window in which a finishing background step used to be missed.
    /// </summary>
    internal Action? BeforePendingCheck { get; set; }

    /// <summary>
    /// Candidates the quick stage decodes (newest first, within the chosen epoch — which is what
    /// fixes the Zwischenlösung's "quick and refine stage pick different actors").
    /// </summary>
    internal const int QuickStageCandidates = 10;

    /// <summary>Clips per patch the voice-change detection embeds (§11.3; measured with 4, 3 suffice).</summary>
    internal const int EmbeddingsPerPatch = 3;

    /// <summary>Parallel decodes in the batch (Whisper and embedding calls stay serial).</summary>
    internal static int BatchDecodeParallelism => Math.Clamp(Environment.ProcessorCount / 2, 1, 6);

    /// <summary>Appended to the lines hash of an index ranked without Whisper (fast batch mode).</summary>
    internal const string NoAsrHashSuffix = ":noasr";

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Configuration _config;
    private readonly ILogService _log;
    private readonly IDatabaseService _db;
    private readonly IGameVoiceDataSource _data;
    private readonly IEchokrauTtsReferenceClient _client;
    private readonly IGameVoiceMappingService _mapping;
    private readonly IStoryProgressService _progress;
    private readonly Func<DateTime> _utcNow;
    private readonly CancellationTokenSource _cts = new();

    // Text indexes per language (text only, built once on first use).
    private readonly object _indexLock = new();
    private readonly object _buildLock = new();
    private readonly Dictionary<ClientLanguage, LanguageIndex> _indexes = new();
    private readonly ConcurrentDictionary<ClientLanguage, Task> _indexBuildsPending = new();

    // Server probe.
    private readonly object _probeLock = new();
    private ProbeState _probe = ProbeState.Unknown;
    private bool _asrAvailable;
    private string? _speakerEncoder;
    // Longest reference the wrapper takes (/health inline_ref_max_seconds); 15 s for an older one.
    private double _maxReferenceSeconds = InlineReferenceAssembler.DefaultMaxSeconds;
    private DateTime _probedUtc = DateTime.MinValue;
    private int _probeRunning;
    private volatile Task? _probeTask;
    private GameVoiceAvailability _lastReported = GameVoiceAvailability.Disabled;

    // Ranking runs in two stages per (token, epoch): the quick stage (decode + pre-score, no
    // Whisper) under _rankGate leaves a provisional reference for a waiting line; the ASR stage
    // under _asrGate refines it and stores the index. Two gates, so a new speaker's quick stage
    // never waits behind another one's Whisper checks.
    private readonly SemaphoreSlim _rankGate = new(1, 1);
    private readonly SemaphoreSlim _asrGate = new(1, 1);
    private readonly ConcurrentDictionary<TokenEpoch, byte> _rankPending = new();
    private readonly ConcurrentDictionary<TokenEpoch, TaskCompletionSource<bool>> _quickReady = new();
    private readonly ConcurrentDictionary<TokenEpoch, List<DecodedReferenceClip>> _provisional = new();
    private readonly ConcurrentDictionary<string, byte> _seenWithoutToken = new(StringComparer.OrdinalIgnoreCase);

    private readonly ReferenceLru _lru = new(LruCapacity);
    private readonly IEchokrauTtsInstanceService _instance;
    private volatile bool _indexBuildRunning;
    // Bumped whenever a background step a waiting first line depends on finishes (probe, text index,
    // quick stage) — BEFORE its task completes, so GetReferenceAsync can tell "nothing pending" from
    // "a step finished between my lookup and my pending check" (CI 2026-11-23: voice pack after 14 ms).
    private int _stepsFinished;
    private bool _disposed;

    public event Action<GameVoiceAvailability>? AvailabilityChanged;

    public GameVoiceReferenceService(
        Configuration config,
        ILogService log,
        IDatabaseService db,
        IGameVoiceDataSource data,
        IEchokrauTtsReferenceClient client,
        IGameVoiceMappingService mapping,
        IStoryProgressService progress,
        IEchokrauTtsInstanceService instance)
        : this(config, log, db, data, client, mapping, progress, instance, () => DateTime.UtcNow)
    {
    }

    internal GameVoiceReferenceService(
        Configuration config,
        ILogService log,
        IDatabaseService db,
        IGameVoiceDataSource data,
        IEchokrauTtsReferenceClient client,
        IGameVoiceMappingService mapping,
        IStoryProgressService progress,
        IEchokrauTtsInstanceService instance,
        Func<DateTime> utcNow)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _mapping = mapping ?? throw new ArgumentNullException(nameof(mapping));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        _mapping.Changed += OnMappingChanged;
        _instance.OnInstanceReady += OnInstanceReady;
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

    internal double MaxReferenceSeconds { get { lock (_probeLock) return _maxReferenceSeconds; } }

    public async Task<GameVoiceAvailability> RefreshAvailabilityAsync(EKEventId eventId, CancellationToken ct = default)
    {
        if (!_config.GameVoiceReferenceEnabled || _config.BackendSelection != TTSBackends.EchokrauTTS)
            return Report(eventId);

        var health = await _client.GetHealthAsync(eventId, ct).ConfigureAwait(false);
        lock (_probeLock)
        {
            if (health == null)
            {
                // Not reachable (restarting after an engine switch, still loading) says nothing about
                // game-voice support: stay Unknown so the next line asks again, instead of the voice
                // pack for the whole ProbeTtl (live test 2026-11-15: 4 min of voice pack after a restart).
                _probe = ProbeState.Unknown;
                _probedUtc = DateTime.MinValue;
            }
            else
            {
                // Only an explicit true counts. A missing field is an older wrapper that would treat
                // ref_audio as an unknown field — and might write anything it gets to disk.
                _probe = health.inline_reference == true ? ProbeState.Supported : ProbeState.Unsupported;
                _asrAvailable = health.asr_check == true;
                _speakerEncoder = health.speaker_embedding == true ? health.speaker_encoder : null;
                var max = health.inline_ref_max_seconds is > 0 and var m ? m : InlineReferenceAssembler.DefaultMaxSeconds;
                // An engine switch changes the limit: references built for the old one would be rejected or short.
                if (Math.Abs(max - _maxReferenceSeconds) > 0.001) _lru.Clear();
                _maxReferenceSeconds = max;
                _probedUtc = _utcNow();
            }
        }
        MarkProgress();
        return Report(eventId);
    }

    /// <summary>
    /// The local instance (re)started — after an engine switch it is a different engine with its own
    /// reference limit: forget the probe and the built references and ask /health right away, so the
    /// first line does not send the old engine's reference (Qwen 20 s → Pocket 400, live test 2026-11-15).
    /// </summary>
    private void OnInstanceReady()
    {
        if (_disposed) return;
        lock (_probeLock)
        {
            _probe = ProbeState.Unknown;
            _probedUtc = DateTime.MinValue;
        }
        _lru.Clear();
        if (_config.GameVoiceReferenceEnabled && _config.BackendSelection == TTSBackends.EchokrauTTS)
            EnsureFreshProbe(new EKEventId(0, TextSource.None));
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
        _probeTask = Task.Run(async () =>
        {
            try { await RefreshAvailabilityAsync(eventId, _cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* disposed */ }
            catch (Exception ex) { _log.Warning(nameof(EnsureFreshProbe), ex.Message, eventId); }
            finally { Interlocked.Exchange(ref _probeRunning, 0); }
        });
    }

    // ── Live path ───────────────────────────────────────────────────────────

    /// <summary>The non-waiting lookup behind <see cref="GetReferenceAsync"/>: memory, then the stored index.</summary>
    public InlineVoiceReference? TryGetReference(GameVoiceSpeaker speaker, EKEventId eventId)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (_disposed || string.IsNullOrWhiteSpace(speaker.Name)) return null;

        var availability = Availability;
        if (availability is GameVoiceAvailability.Disabled or GameVoiceAvailability.WrongBackend) return null;
        EnsureFreshProbe(eventId);
        if (availability != GameVoiceAvailability.Available) return null;

        var index = TryGetBuiltIndex(speaker.Language);
        if (index == null)
        {
            // The token map needs the text index: build it in the background, this line waits
            // (GetReferenceAsync) or uses the voice pack.
            QueueIndexBuild(speaker.Language, eventId);
            return null;
        }

        var selection = Select(speaker, index, out var context, out var split);
        if (selection.Count == 0)
        {
            _seenWithoutToken.TryAdd(speaker.Name.Trim(), 0);
            return null;
        }
        if (split)
            _log.Info(nameof(TryGetReference),
                $"Game voice for '{speaker.Name}': {string.Join(", ", selection)} — story {(context?.ToString() ?? "unknown, newest epoch")}", eventId);

        var key = LruKey(selection);
        if (_lru.TryGet(key, out var cached)) return cached;

        var scored = new List<(TokenEpoch Pair, GameVoiceLine Line, double Score)>();
        var provisional = new List<DecodedReferenceClip>();
        var pending = false;
        foreach (var pair in selection)
        {
            var epochLines = EpochLinesOf(index, pair);
            var rows = _db.GetGameVoiceIndex((int)pair.Language, pair.Token).Where(r => r.Epoch == pair.Epoch).ToList();
            if (!IsCurrent(rows, epochLines.Hash, requireAsr: false))
            {
                if (_provisional.TryGetValue(pair, out var prov)) provisional.AddRange(prov);
                QueueRanking(pair, eventId);
                pending = true;
                continue;
            }
            foreach (var row in rows.Where(r => r.Rank > 0))
            {
                if (index.LinesByBase.TryGetValue(row.AudioBase, out var line))
                    scored.Add((pair, line, row.Score));
                else
                {
                    Invalidate(pair, eventId, $"clip {row.AudioBase} is no longer in the text index");
                    pending = true;
                }
            }
        }

        if (scored.Count == 0)
            return provisional.Count == 0 ? null : InlineReferenceAssembler.Assemble(provisional, MaxReferenceSeconds);

        var clips = new List<DecodedReferenceClip>();
        foreach (var (pair, line, _) in scored.OrderByDescending(s => s.Score).Take(GameVoiceRanking.MaxReferences))
        {
            var clip = DecodeClip(line, speaker.Language);
            if (clip == null)
            {
                // §2.6: a clip a patch removed (or broke) invalidates that index.
                Invalidate(pair, eventId, $"stored clip {line.AudioBase} no longer decodes");
                return null;
            }
            clips.Add(clip);
        }

        var reference = InlineReferenceAssembler.Assemble(clips, MaxReferenceSeconds);
        if (reference != null && !pending) _lru.Put(key, reference);
        return reference;
    }

    public async Task<InlineVoiceReference?> GetReferenceAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct = default)
    {
        // One budget for everything a first line may have to wait for, in the order it comes up:
        // the /health probe (first line after a plugin start), the text index, the quick ranking
        // stage. Each TryGetReference call starts the next step; nothing pending → voice pack now.
        // A step can finish between the lookup and the pending check without the lookup having
        // started the next one: the progress counter catches that and looks again.
        var deadline = _utcNow() + FirstLineWait;
        while (true)
        {
            var finished = Volatile.Read(ref _stepsFinished);
            var reference = TryGetReference(speaker, eventId);
            if (reference != null) return reference;
            BeforePendingCheck?.Invoke();
            var pending = PendingWorkFor(speaker);
            var remaining = deadline - _utcNow();
            if (pending == null)
            {
                if (Volatile.Read(ref _stepsFinished) == finished) return null;
                if (remaining > TimeSpan.Zero) continue;
            }
            if (pending == null
                || remaining <= TimeSpan.Zero
                || await Task.WhenAny(pending, Task.Delay(remaining, ct)).ConfigureAwait(false) != pending)
            {
                _log.Info(nameof(GetReferenceAsync),
                    $"Game voice for '{speaker.Name}' not ready after {FirstLineWait.TotalSeconds:0} s; this line uses the voice pack.", eventId);
                return null;
            }
        }
    }

    /// <summary>The background step this speaker's reference is waiting for, or null if none runs.</summary>
    internal Task? PendingWorkFor(GameVoiceSpeaker speaker)
    {
        if (_probeTask is { IsCompleted: false } probe) return probe;
        if (_indexBuildsPending.TryGetValue(speaker.Language, out var build)) return build;
        var index = TryGetBuiltIndex(speaker.Language);
        if (index == null) return null;
        var waits = Select(speaker, index)
            .Select(p => _quickReady.TryGetValue(p, out var q) && !q.Task.IsCompleted ? q.Task : null)
            .OfType<Task<bool>>()
            .ToList();
        return waits.Count == 0 ? null : Task.WhenAny(waits);
    }

    /// <summary>A background step finished; call BEFORE completing the task a line may wait on.</summary>
    private void MarkProgress() => Interlocked.Increment(ref _stepsFinished);

    private DecodedReferenceClip? DecodeClip(GameVoiceLine line, ClientLanguage language)
    {
        var pcm = _data.TryDecodeVoice(line.AudioBase, language);
        return pcm == null ? null : new DecodedReferenceClip(line.AudioBase, line.Text, ToMono24k(pcm));
    }

    private void Invalidate(TokenEpoch pair, EKEventId eventId, string why)
    {
        _log.Info(nameof(TryGetReference), $"Game-voice index {pair} dropped ({why}); ranking again.", eventId);
        _db.ReplaceGameVoiceIndex((int)pair.Language, pair.Token, pair.Epoch, Array.Empty<GameVoiceIndexEntity>());
        _lru.RemoveContaining(pair.ToString());
        QueueRanking(pair, eventId);
    }

    private void QueueRanking(TokenEpoch pair, EKEventId eventId)
    {
        if (!_config.GameVoiceRankOnConversationStart) return;
        if (!_rankPending.TryAdd(pair, 0)) return;
        _quickReady[pair] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            try
            {
                var index = GetOrBuildIndex(pair.Language, _cts.Token, eventId);
                await RankPairAsync(pair, index, withAsr: true, parallelDecode: false, eventId, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* disposed */ }
            catch (Exception ex) { _log.Warning(nameof(QueueRanking), ex.ToString(), eventId); }
            finally
            {
                // A failure before the quick stage must not leave the first line waiting the full time.
                MarkProgress();
                if (_quickReady.TryRemove(pair, out var quick)) quick.TrySetResult(false);
                _provisional.TryRemove(pair, out _);
                _rankPending.TryRemove(pair, out _);
            }
        });
    }

    private void QueueIndexBuild(ClientLanguage language, EKEventId eventId)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_indexBuildsPending.TryAdd(language, gate.Task)) return;
        _ = Task.Run(() =>
        {
            try { GetOrBuildIndex(language, _cts.Token, eventId); }
            catch (OperationCanceledException) { /* disposed */ }
            catch (Exception ex) { _log.Warning(nameof(QueueIndexBuild), ex.ToString(), eventId); }
            finally
            {
                _indexBuildsPending.TryRemove(language, out _);
                gate.TrySetResult();
            }
        });
    }

    // ── Speaker → tokens → epochs (§11.1, §11.4) ───────────────────────────

    /// <summary>The (token, epoch) pairs a speaker speaks with right now.</summary>
    internal List<TokenEpoch> Select(GameVoiceSpeaker speaker, LanguageIndex index) =>
        Select(speaker, index, out _, out _);

    /// <param name="context">Story patch the epochs were chosen for (null = unknown → newest epoch).</param>
    /// <param name="split">True when at least one token has more than one epoch (a voice-actor change applies).</param>
    private List<TokenEpoch> Select(GameVoiceSpeaker speaker, LanguageIndex index, out StoryContext? context, out bool split)
    {
        var result = new List<TokenEpoch>();
        context = null;
        split = false;
        Func<string, bool>? reached = null;
        var contextAsked = false;
        foreach (var token in OwnedTokens(speaker, index))
        {
            var epochs = EpochsOf(index, token);
            if (epochs.Count == 0) continue;
            if (epochs.Count > 1 && !contextAsked)
            {
                // Only a token with a voice-actor change needs the story patch (word match on the
                // accepted quests' texts) — every other NPC skips it.
                contextAsked = true;
                split = true;
                var ctx = _progress.ContextFor(speaker.LineText, speaker.Language);
                context = ctx;
                if (ctx != null) reached = t => _progress.IsReached(t, ctx.PatchKey);
            }
            result.Add(new TokenEpoch(speaker.Language, token, GameVoiceEpochs.Choose(epochs, reached)));
        }
        return result;
    }

    /// <summary>
    /// The tokens that are "the same NPC" (§2.2, §3, F2): a user override first (§11.5, local), else
    /// resolve the speaker to ENpcResident ids (live base id, the character's DB instances, exact
    /// client-language name), take their English names and ask the speaker map which tokens those own.
    /// </summary>
    internal List<string> OwnedTokens(GameVoiceSpeaker speaker, LanguageIndex index)
    {
        var name = speaker.Name.Trim();
        if (_config.GameVoiceNpcOverrides.TryGetValue(OverrideKey(speaker.Language, name), out var forced))
        {
            var token = VoiceExtractKey.Normalize(forced ?? "");
            return token.Length == 0 || index.Text.LinesFor(token).Count == 0 ? new List<string>() : new List<string> { token };
        }

        var ids = new HashSet<uint>();
        if (speaker.NpcBaseId != 0) ids.Add(speaker.NpcBaseId);
        if (speaker.CharacterId > 0)
            foreach (var instance in _db.GetInstancesForCharacter(speaker.CharacterId))
                if (instance.NpcBaseId > 0) ids.Add((uint)instance.NpcBaseId);
        foreach (var (id, clientName) in index.ClientNames)
            if (string.Equals(clientName, name, StringComparison.OrdinalIgnoreCase)) ids.Add(id);

        var englishNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            if (index.EnglishNames.TryGetValue(id, out var en)) englishNames.Add(en);
        if (speaker.Language == ClientLanguage.English) englishNames.Add(name);

        return index.Speakers.TokensFor(englishNames).ToList();
    }

    /// <summary>Key of <see cref="Configuration.GameVoiceNpcOverrides"/>: <c>DE|y'shtola</c>.</summary>
    public static string OverrideKey(ClientLanguage language, string npcName) =>
        LanguageCode(language) + "|" + (npcName ?? "").Trim().ToLowerInvariant();

    /// <summary>Epochs a token has clips in (one empty epoch without a split).</summary>
    internal List<string> EpochsOf(LanguageIndex index, string token) =>
        TokenEpochsOf(index, token).Keys.ToList();

    private EpochLines EpochLinesOf(LanguageIndex index, TokenEpoch pair) =>
        TokenEpochsOf(index, pair.Token).TryGetValue(pair.Epoch, out var lines) ? lines : EpochLines.Empty;

    /// <summary>Lines of a token grouped by voice-actor epoch, memoized per text index.</summary>
    private IReadOnlyDictionary<string, EpochLines> TokenEpochsOf(LanguageIndex index, string token)
    {
        return index.Epochs.GetOrAdd(token, t =>
        {
            var langCode = LanguageCode(index.Language);
            var splits = _mapping.Splits;
            var splitName = SplitNameFor(index, t, splits, langCode);
            return index.Text.LinesFor(t)
                .GroupBy(l => splitName == null ? "" : splits.ResolveEpoch(splitName, langCode, l.AudioBase), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => EpochLines.Of(g.ToList()), StringComparer.Ordinal);
        });
    }

    /// <summary>
    /// The name a split entry for this token is filed under: the token itself, or a client-language
    /// / English name of the NPC that owns it (the research files splits under the name shown in
    /// the client, e.g. "Y'shtola"). Null when no split applies.
    /// </summary>
    private static string? SplitNameFor(LanguageIndex index, string token, VoiceActorSplits splits, string langCode)
    {
        if (splits.HasSplit(token, langCode)) return token;
        var owner = index.Speakers.OwnerOf(token);
        if (owner == null || !index.NamesByOwner.TryGetValue(owner, out var names)) return null;
        return names.FirstOrDefault(n => splits.HasSplit(n, langCode));
    }

    private void OnMappingChanged()
    {
        // New aliases/splits: rebuild the token maps, forget epoch groupings and decoded references.
        // The index itself is per token, so nothing needs re-ranking (§11.5 "Wirkung").
        List<LanguageIndex> built;
        lock (_indexLock) built = _indexes.Values.ToList();
        foreach (var index in built)
        {
            var speakers = GameVoiceSpeakerMap.Build(index.Text.Tokens, index.EnglishNames, _mapping.Aliases, out _);
            var rebuilt = index with { Speakers = speakers, Epochs = new ConcurrentDictionary<string, IReadOnlyDictionary<string, EpochLines>>(StringComparer.Ordinal) };
            lock (_indexLock) _indexes[index.Language] = rebuilt;
            MarkProgress();
        }
        _lru.Clear();
    }

    // ── Ranking (§4) ────────────────────────────────────────────────────────

    public async Task<int> RankAsync(GameVoiceSpeaker speaker, EKEventId eventId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        if (string.IsNullOrWhiteSpace(speaker.Name)) return 0;
        var index = GetOrBuildIndex(speaker.Language, ct, eventId);
        var stored = 0;
        foreach (var pair in Select(speaker, index))
            stored += await RankPairAsync(pair, index, withAsr: true, parallelDecode: false, eventId, ct).ConfigureAwait(false);
        return stored;
    }

    /// <summary>Quick stage + ASR stage for one (token, epoch); stores the rows. Returns stored clips.</summary>
    private async Task<int> RankPairAsync(TokenEpoch pair, LanguageIndex index, bool withAsr, bool parallelDecode,
        EKEventId eventId, CancellationToken ct)
    {
        var langCode = LanguageCode(pair.Language);
        var epochLines = EpochLinesOf(index, pair);
        var candidates = epochLines.Lines
            .Where(l => SpeechTextFilter.IsSpeech(l.Text, langCode))                     // §4 text, hard
            .OrderByDescending(l => l.AudioBase, StringComparer.Ordinal)                 // newest patch first
            .Take(GameVoiceRanking.MaxDecodedCandidates)
            .ToList();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<ScoredClip> quick;
        await _rankGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            quick = PreScoreAll(candidates.Take(QuickStageCandidates), pair.Language, langCode, parallelDecode, ct);
            // Only when a live line waits for it: the batch must not decode chosen clips twice.
            if (_quickReady.ContainsKey(pair))
            {
                var clips = Choose(TopByPreScore(quick)).Select(c => DecodeClip(c.Line, pair.Language)).OfType<DecodedReferenceClip>().ToList();
                if (clips.Count > 0) _provisional[pair] = clips;
            }
        }
        finally
        {
            _rankGate.Release();
        }
        var quickMs = watch.ElapsedMilliseconds;
        MarkProgress();
        if (_quickReady.TryGetValue(pair, out var ready)) ready.TrySetResult(true);

        bool asr;
        lock (_probeLock) asr = withAsr && _asrAvailable;
        var scored = quick.Concat(PreScoreAll(candidates.Skip(QuickStageCandidates), pair.Language, langCode, parallelDecode, ct)).ToList();
        var top = TopByPreScore(scored);
        if (asr)
        {
            await _asrGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var clip in top)
                {
                    var pcm = _data.TryDecodeVoice(clip.Line.AudioBase, pair.Language);
                    var heard = pcm == null
                        ? null
                        : await _client.AsrCheckAsync(eventId, ToMono24k(pcm), langCode.ToLowerInvariant(), ct).ConfigureAwait(false);
                    double? wer = heard != null ? WordErrorRate.Compute(clip.Line.Text, heard, langCode) : null;
                    clip.FinalScore = GameVoiceRanking.FinalScore(clip.PreScore, wer);
                }
            }
            finally
            {
                _asrGate.Release();
            }
        }

        var chosen = Choose(top);
        var now = _utcNow();
        var hash = epochLines.Hash + (asr ? "" : NoAsrHashSuffix);
        var rows = chosen.Select((c, i) => new GameVoiceIndexEntity
        {
            Language = (int)pair.Language,
            Token = pair.Token,
            Epoch = pair.Epoch,
            AudioBase = c.Line.AudioBase,
            Rank = i + 1,
            Score = c.FinalScore,
            IndexVersion = GameVoiceRanking.IndexVersion,
            LinesHash = hash,
            CreatedUtc = now,
        }).ToList();
        if (rows.Count == 0)
            rows.Add(new GameVoiceIndexEntity
            {
                Language = (int)pair.Language, Token = pair.Token, Epoch = pair.Epoch, AudioBase = "", Rank = 0,
                Score = 0, IndexVersion = GameVoiceRanking.IndexVersion, LinesHash = hash, CreatedUtc = now,
            });

        _db.ReplaceGameVoiceIndex((int)pair.Language, pair.Token, pair.Epoch, rows);
        _provisional.TryRemove(pair, out _);
        _lru.RemoveContaining(pair.ToString()); // a reference built from the old index is stale

        var storedClips = rows.Count(r => r.Rank > 0);
        _log.Debug(nameof(RankPairAsync),
            $"Game-voice ranking {pair}: {epochLines.Lines.Count} line(s), {candidates.Count} candidate(s), " +
            $"{scored.Count} usable, {storedClips} stored" + (asr ? "" : " (no ASR check)") +
            $"; quick stage {quickMs} ms, total {watch.ElapsedMilliseconds} ms", eventId);
        return storedClips;
    }

    /// <summary>Decode + §4 hard duration filter + pre-score. PCM is not kept (≤ 40 clips).</summary>
    private List<ScoredClip> PreScoreAll(IEnumerable<GameVoiceLine> lines, ClientLanguage language, string langCode, bool parallel, CancellationToken ct)
    {
        var list = lines.ToList();
        var results = new ScoredClip?[list.Count];
        void Score(int i)
        {
            ct.ThrowIfCancellationRequested();
            var line = list[i];
            var pcm = _data.TryDecodeVoice(line.AudioBase, language);
            if (pcm == null) return;
            var seconds = pcm.Seconds;
            if (!GameVoiceRanking.IsAcceptableDuration(seconds)) return;               // §4 duration, hard
            var metrics = SignalAnalyzer.Analyze(pcm);
            var gate = SignalAnalyzer.Check(metrics);
            results[i] = new ScoredClip(line, seconds, GameVoiceRanking.PreScore(line.Text, seconds, langCode, metrics, gate));
        }

        if (parallel && list.Count > 1)
            Parallel.For(0, list.Count, new ParallelOptions { MaxDegreeOfParallelism = BatchDecodeParallelism, CancellationToken = ct }, Score);
        else
            for (var i = 0; i < list.Count; i++) Score(i);
        return results.OfType<ScoredClip>().ToList();
    }

    /// <summary>The <see cref="GameVoiceRanking.AsrTopN"/> best by pre-score, final score = pre-score until Whisper ran.</summary>
    private static List<ScoredClip> TopByPreScore(List<ScoredClip> scored)
    {
        var top = scored.OrderByDescending(s => s.PreScore).Take(GameVoiceRanking.AsrTopN).ToList();
        foreach (var clip in top) clip.FinalScore = GameVoiceRanking.FinalScore(clip.PreScore, null);
        return top;
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
    /// Index rows are current when they carry this ranking version and the epoch's lines are
    /// unchanged. <paramref name="requireAsr"/>: a fast (no-Whisper) index counts as outdated.
    /// </summary>
    private static bool IsCurrent(IReadOnlyCollection<GameVoiceIndexEntity> rows, string hash, bool requireAsr)
    {
        if (rows.Count == 0) return false;
        foreach (var r in rows)
            if (!IsCurrent(r.IndexVersion, r.LinesHash, hash, requireAsr)) return false;
        return true;
    }

    private static bool IsCurrent(int version, string stored, string hash, bool requireAsr) =>
        version == GameVoiceRanking.IndexVersion
        && (stored == hash || (!requireAsr && stored == hash + NoAsrHashSuffix));

    // ── Batch (§11.2) ───────────────────────────────────────────────────────

    public async Task<GameVoiceBatchResult> BuildIndexAsync(ClientLanguage language, GameVoiceBatchOptions options,
        IProgress<GameVoiceBatchProgress>? progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_indexBuildRunning) return NotRun("already running");
        _indexBuildRunning = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var eventId = new EKEventId(0, TextSource.None);
        try
        {
            if (Availability != GameVoiceAvailability.Available)
                await RefreshAvailabilityAsync(eventId, ct).ConfigureAwait(false);
            if (Availability != GameVoiceAvailability.Available)
            {
                _log.Info(nameof(BuildIndexAsync), $"Not building the game-voice index: {Availability}.", eventId);
                return NotRun(Availability.ToString());
            }

            var index = await Task.Run(() => GetOrBuildIndex(language, ct, eventId), ct).ConfigureAwait(false);
            bool asr;
            string? encoder;
            lock (_probeLock) { asr = options.WithAsr && _asrAvailable; encoder = _speakerEncoder; }
            var work = OutdatedPairs(index, asr);
            var tokens = index.Text.Tokens.Count();
            var upToDate = index.Text.Tokens.Sum(t => EpochsOf(index, t).Count) - work.Count;

            var ranked = 0;
            var withReference = 0;
            var cancelled = false;
            try
            {
                for (var i = 0; i < work.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var pair = work[i];
                    if (await RankPairAsync(pair, index, asr, parallelDecode: true, eventId, ct).ConfigureAwait(false) > 0)
                        withReference++;
                    ranked++;
                    progress?.Report(new GameVoiceBatchProgress(i + 1, work.Count, pair.Token, watch.Elapsed));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
            }

            var harvest = HarvestDirectory();
            var langCode = LanguageCode(language);
            string? reportPath = null;
            var unmatched = 0;
            try
            {
                var report = BuildUnmatchedReport(index, langCode);
                unmatched = report.Unmatched.Count + report.Ambiguous.Count;
                reportPath = WriteJson(harvest, UnmatchedReportFile(language), report);
            }
            catch (Exception ex)
            {
                _log.Warning(nameof(BuildIndexAsync), $"Matching report not written: {ex.Message}", eventId);
            }

            string? suggestionsPath = null;
            var suggestions = 0;
            if (options.DetectVoiceChanges && !cancelled)
            {
                if (encoder == null)
                    _log.Info(nameof(BuildIndexAsync), "Voice-change detection skipped: the active engine has no speaker encoder (/health speaker_embedding).", eventId);
                else
                {
                    try
                    {
                        var file = await DetectVoiceChangesAsync(index, encoder, eventId, progress, watch, ct).ConfigureAwait(false);
                        suggestions = file.Splits.Count;
                        suggestionsPath = WriteJson(harvest, $"game_voice_actor_splits_suggested_{langCode.ToLowerInvariant()}.json", file);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                    }
                }
            }

            _log.Info(nameof(BuildIndexAsync),
                $"Game-voice index ({langCode}): {tokens} token(s), {ranked} ranked ({withReference} with a reference), " +
                $"{upToDate} up to date, {unmatched} unmatched, {suggestions} voice-change suggestion(s)" +
                (asr ? "" : ", without Whisper") + (cancelled ? ", CANCELLED" : "") + $", {watch.Elapsed:hh\\:mm\\:ss}.", eventId);
            return new GameVoiceBatchResult(tokens, ranked, upToDate, withReference, unmatched, suggestions,
                reportPath, suggestionsPath, watch.Elapsed, cancelled);
        }
        finally
        {
            _indexBuildRunning = false;
        }

        static GameVoiceBatchResult NotRun(string why) =>
            new(0, 0, 0, 0, 0, 0, null, null, TimeSpan.Zero, false, why);
    }

    /// <summary>File name of the matching report in <c>harvest/</c> (the maintenance window reads it).</summary>
    public static string UnmatchedReportFile(ClientLanguage language) =>
        $"game_voice_unmatched_{LanguageCode(language).ToLowerInvariant()}.json";

    /// <summary>How many of the newest clips the self-test tries before it reports "none decodes".</summary>
    internal const int SelfCheckDecodeAttempts = 25;

    public Task<GameVoiceSelfCheck> SelfCheckAsync(ClientLanguage language, CancellationToken ct = default)
    {
        if (!_config.GameVoiceReferenceEnabled) return Task.FromResult(new GameVoiceSelfCheck(0, 0, null, 0));
        return Task.Run(() =>
        {
            var index = GetOrBuildIndex(language, ct, new EKEventId(0, TextSource.None));
            var tokens = index.Text.Tokens.Count();
            foreach (var line in index.LinesByBase.Values
                         .OrderByDescending(l => l.AudioBase, StringComparer.Ordinal)
                         .Take(SelfCheckDecodeAttempts))
            {
                ct.ThrowIfCancellationRequested();
                // Decoded in memory and dropped right here: only its length leaves this method.
                var pcm = _data.TryDecodeVoice(line.AudioBase, language);
                if (pcm != null && pcm.FrameCount > 0)
                    return new GameVoiceSelfCheck(tokens, index.Text.LineCount, line.AudioBase, pcm.Seconds);
            }
            return new GameVoiceSelfCheck(tokens, index.Text.LineCount, null, 0);
        }, ct);
    }

    public Task<string?> SampleVoicedNpcNameAsync(ClientLanguage language, CancellationToken ct = default)
    {
        if (!_config.GameVoiceReferenceEnabled) return Task.FromResult<string?>(null);
        return Task.Run(() =>
        {
            var index = GetOrBuildIndex(language, ct, new EKEventId(0, TextSource.None));
            var candidates = index.Text.Tokens
                .Where(t => index.Speakers.OwnerOf(t) != null)
                .OrderByDescending(t => index.Text.LinesFor(t).Count)
                .ThenBy(t => t, StringComparer.Ordinal);
            foreach (var token in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (EpochsOf(index, token).Count != 1) continue; // a voice-actor change needs the story context
                if (!index.NamesByOwner.TryGetValue(index.Speakers.OwnerOf(token)!, out var names) || names.Count == 0) continue;
                var name = names[0];
                // Round trip: the name must resolve to exactly this NPC's tokens, and this one among them.
                if (OwnedTokens(new GameVoiceSpeaker(0, name, 0, language), index).Contains(token, StringComparer.Ordinal))
                    return name;
            }
            return (string?)null;
        }, ct);
    }

    public async Task<int> CountOutdatedTokensAsync(ClientLanguage language, CancellationToken ct = default)
    {
        var index = await Task.Run(() => GetOrBuildIndex(language, ct, new EKEventId(0, TextSource.None)), ct).ConfigureAwait(false);
        bool asr;
        lock (_probeLock) asr = _asrAvailable;
        return OutdatedPairs(index, asr).Select(p => p.Token).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>(token, epoch) pairs without a current index, in token order.</summary>
    private List<TokenEpoch> OutdatedPairs(LanguageIndex index, bool requireAsr)
    {
        var states = new Dictionary<(string, string), GameVoiceIndexState>();
        foreach (var s in _db.GetGameVoiceIndexStates((int)index.Language)) states.TryAdd((s.Token, s.Epoch), s);
        var work = new List<TokenEpoch>();
        foreach (var token in index.Text.Tokens.OrderBy(t => t, StringComparer.Ordinal))
            foreach (var (epoch, lines) in TokenEpochsOf(index, token).OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var current = states.TryGetValue((token, epoch), out var state)
                    && IsCurrent(state.IndexVersion, state.LinesHash, lines.Hash, requireAsr);
                if (!current) work.Add(new TokenEpoch(index.Language, token, epoch));
            }
        return work;
    }

    internal GameVoiceUnmatchedReport BuildUnmatchedReport(LanguageIndex index, string langCode)
    {
        var report = new GameVoiceUnmatchedReport { Language = langCode, CreatedUtc = _utcNow() };
        var names = index.EnglishNames.Values.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var token in index.Text.Tokens.OrderBy(t => t, StringComparer.Ordinal))
        {
            if (index.Speakers.OwnerOf(token) != null || index.Speakers.DisabledTokens.Contains(token)) continue;
            var lines = index.Text.LinesFor(token);
            var entry = new GameVoiceUnmatchedToken
            {
                Token = token,
                Clips = lines.Count,
                Samples = lines.OrderByDescending(l => l.Text.Length).Take(3).Select(l => l.Text).ToList(),
                Suggestions = GameVoiceMatchSuggester.Suggest(token, names),
            };
            (index.Speakers.AmbiguousTokens.Contains(token) ? report.Ambiguous : report.Unmatched).Add(entry);
        }
        report.SeenWithoutToken = _seenWithoutToken.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        // Biggest first: a token with 200 clips matters more than a one-line extra.
        report.Unmatched = report.Unmatched.OrderByDescending(e => e.Clips).ThenBy(e => e.Token, StringComparer.Ordinal).ToList();
        return report;
    }

    /// <summary>
    /// §11.3: embeds a few clips per patch of every token with enough patches, then lets
    /// <see cref="SpeakerChangeDetector"/> suggest boundaries. Written as a split file for review —
    /// never applied by itself (the measured margins are too thin, see the detector's remarks).
    /// </summary>
    private async Task<VoiceActorSplitsFile> DetectVoiceChangesAsync(LanguageIndex index, string encoder, EKEventId eventId,
        IProgress<GameVoiceBatchProgress>? progress, System.Diagnostics.Stopwatch watch, CancellationToken ct)
    {
        var langCode = LanguageCode(index.Language);
        var minPatches = 2 * SpeakerChangeDetector.MinPatchesPerSide;
        var candidates = index.Text.Tokens
            .Select(t => (Token: t, Lines: index.Text.LinesFor(t).Where(l => SpeechTextFilter.IsSpeech(l.Text, langCode)).ToList()))
            .Where(x => x.Lines.Select(l => PatchOf(l.AudioBase)).Distinct().Count() >= minPatches)
            .OrderBy(x => x.Token, StringComparer.Ordinal)
            .ToList();

        var samples = new Dictionary<string, List<SpeakerSample>>(StringComparer.Ordinal);
        var max = (int)(GameVoiceRanking.MaxSeconds * InlineVoiceReference.SampleRate);
        for (var i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (token, lines) = candidates[i];
            var list = samples[token] = new List<SpeakerSample>();
            foreach (var patch in lines.GroupBy(l => PatchOf(l.AudioBase)))
            {
                var picked = 0;
                foreach (var line in patch.OrderByDescending(l => l.Text.Length > 200 ? 0 : l.Text.Length))
                {
                    if (picked >= EmbeddingsPerPatch) break;
                    var pcm = _data.TryDecodeVoice(line.AudioBase, index.Language);
                    if (pcm == null || pcm.Seconds < 2.5) continue;
                    var mono = ToMono24k(pcm);
                    if (mono.Length > max) mono = mono[..max];
                    var emb = await _client.SpeakerEmbeddingAsync(eventId, mono, ct).ConfigureAwait(false);
                    if (emb == null || emb.Encoder != encoder) continue;
                    list.Add(new SpeakerSample(patch.Key, emb.Vector));
                    picked++;
                }
            }
            progress?.Report(new GameVoiceBatchProgress(i + 1, candidates.Count, token, watch.Elapsed));
        }

        var mean = SpeakerChangeDetector.Mean(samples.Values.SelectMany(s => s).Select(s => s.Vector));
        var threshold = SpeakerChangeDetector.ThresholdFor(encoder);
        var splits = _mapping.Splits;
        var file = new VoiceActorSplitsFile { Version = 2 };
        foreach (var (token, list) in samples)
        {
            var found = SpeakerChangeDetector.Detect(list, mean, threshold);
            if (found.Count == 0) continue;
            var owner = index.Speakers.OwnerOf(token);
            var name = owner != null && index.NamesByOwner.TryGetValue(owner, out var n) && n.Count > 0 ? n[0] : token;
            var known = SplitNameFor(index, token, splits, langCode) != null;
            file.Splits.Add(new VoiceActorSplitEntry
            {
                NpcName = name,
                Language = langCode,
                BoundaryPatches = found.Select(f => f.BoundaryToken).ToList(),
                Comment = $"auto-detected {_utcNow():yyyy-MM-dd}, token {token}, encoder {encoder}" +
                          (SpeakerChangeDetector.IsCalibrated(encoder) ? "" : " (UNCALIBRATED)") + ": " +
                          string.Join("; ", found.Select(f => $"{f.BoundaryToken} gap {f.Gap:0.00} ({f.PatchesBefore}|{f.PatchesAfter} patches)")) +
                          (known ? "; a split is already configured" : "") + " — listen before committing",
            });
        }
        return file;
    }

    /// <summary><c>vo_voiceman_06100_000010</c> → <c>06100</c>; <c>00000</c> for other shapes (oldest).</summary>
    internal static string PatchOf(string audioBase)
    {
        var parts = (audioBase ?? "").Split('_');
        return parts.Length >= 3 && parts[2].Length == 5 && parts[2].All(char.IsAsciiDigit) ? parts[2] : "00000";
    }

    private string HarvestDirectory() =>
        Path.Combine(_config.SaveToLocal ? _config.LocalSaveLocation : _config.TtsInstallRoot, "harvest");

    private static string WriteJson<T>(string directory, string file, T value)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, file);
        File.WriteAllText(path, JsonSerializer.Serialize(value, WriteOpts));
        return path;
    }

    public void ClearMemory()
    {
        _lru.Clear();
        _provisional.Clear();
        lock (_indexLock) _indexes.Clear();
    }

    // ── Text index (§2.2) ───────────────────────────────────────────────────

    internal sealed record LanguageIndex(
        ClientLanguage Language,
        GameVoiceTextIndex Text,
        GameVoiceSpeakerMap Speakers,
        IReadOnlyDictionary<string, GameVoiceLine> LinesByBase,
        IReadOnlyDictionary<uint, string> ClientNames,
        IReadOnlyDictionary<uint, string> EnglishNames,
        IReadOnlyDictionary<string, List<string>> NamesByOwner,
        ConcurrentDictionary<string, IReadOnlyDictionary<string, EpochLines>> Epochs);

    private LanguageIndex? TryGetBuiltIndex(ClientLanguage language)
    {
        lock (_indexLock) return _indexes.TryGetValue(language, out var idx) ? idx : null;
    }

    internal LanguageIndex GetOrBuildIndex(ClientLanguage language, CancellationToken ct, EKEventId eventId)
    {
        if (TryGetBuiltIndex(language) is { } ready) return ready;
        _progress.Prepare(language); // quest texts for the epoch of lines before a quest is accepted
        // Building scans every cutscene sheet (seconds). It runs under its own lock so the live
        // path's TryGetBuiltIndex (dictionary lock only) never waits for it.
        lock (_buildLock)
        {
            if (TryGetBuiltIndex(language) is { } existing) return existing;

            var text = GameVoiceTextIndex.Build(_data.EnumerateCutsceneRows(language, ct));
            var english = _data.GetNpcNames(ClientLanguage.English);
            var client = language == ClientLanguage.English ? english : _data.GetNpcNames(language);
            var speakers = GameVoiceSpeakerMap.Build(text.Tokens, english, _mapping.Aliases, out var warnings);
            foreach (var w in warnings) _log.Warning(nameof(GetOrBuildIndex), w, eventId);

            var byBase = new Dictionary<string, GameVoiceLine>(StringComparer.Ordinal);
            foreach (var token in text.Tokens)
                foreach (var line in text.LinesFor(token))
                    byBase[line.AudioBase] = line;

            // Owner (normalized English full name) → its client-language names first, then English:
            // what a split entry may be filed under.
            var namesByOwner = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (id, en) in english)
            {
                var owner = VoiceExtractKey.Normalize(en ?? "");
                if (owner.Length == 0) continue;
                if (!namesByOwner.TryGetValue(owner, out var list)) namesByOwner[owner] = list = new List<string>();
                if (client.TryGetValue(id, out var cn) && !string.IsNullOrWhiteSpace(cn) && !list.Contains(cn.Trim(), StringComparer.OrdinalIgnoreCase))
                    list.Insert(0, cn.Trim());
                if (!string.IsNullOrWhiteSpace(en) && !list.Contains(en.Trim(), StringComparer.OrdinalIgnoreCase))
                    list.Add(en.Trim());
            }

            var built = new LanguageIndex(language, text, speakers, byBase, client, english, namesByOwner,
                new ConcurrentDictionary<string, IReadOnlyDictionary<string, EpochLines>>(StringComparer.Ordinal));
            lock (_indexLock) _indexes[language] = built;
            MarkProgress();
            _log.Info(nameof(GetOrBuildIndex),
                $"Game-voice text index ({LanguageCode(language)}): {text.LineCount} voiced line(s), {text.Tokens.Count()} speaker token(s).", eventId);
            return built;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string LruKey(IEnumerable<TokenEpoch> selection) =>
        string.Join(" ", selection.Select(p => p.ToString()).OrderBy(s => s, StringComparer.Ordinal));

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mapping.Changed -= OnMappingChanged;
        _instance.OnInstanceReady -= OnInstanceReady;
        _cts.Cancel();
        _cts.Dispose();
        ClearMemory();
    }

    /// <summary>One (language, speaker token, voice-actor epoch) — the unit of the index.</summary>
    internal readonly record struct TokenEpoch(ClientLanguage Language, string Token, string Epoch)
    {
        public override string ToString() => $"{LanguageCode(Language)}:{Token}:{Epoch}";
    }

    /// <summary>The lines of one (token, epoch) and their hash (audio bases + texts).</summary>
    internal sealed record EpochLines(IReadOnlyList<GameVoiceLine> Lines, string Hash)
    {
        public static readonly EpochLines Empty = new(Array.Empty<GameVoiceLine>(), "");

        public static EpochLines Of(List<GameVoiceLine> lines)
        {
            var sb = new StringBuilder();
            foreach (var l in lines.OrderBy(l => l.AudioBase, StringComparer.Ordinal))
                sb.Append(l.AudioBase).Append('\t').Append(l.Text).Append('\n');
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
            return new EpochLines(lines, hash);
        }
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
        private readonly LinkedList<(string Key, InlineVoiceReference Value)> _order = new();
        private readonly Dictionary<string, LinkedListNode<(string Key, InlineVoiceReference Value)>> _map = new(StringComparer.Ordinal);

        public ReferenceLru(int capacity) => _capacity = capacity;

        public bool TryGet(string key, out InlineVoiceReference value)
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

        public void Put(string key, InlineVoiceReference value)
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

        /// <summary>Drops every selection that uses <paramref name="pair"/> (its index changed).</summary>
        public void RemoveContaining(string pair)
        {
            lock (_lock)
            {
                foreach (var k in _map.Keys.Where(k => k.Split(' ').Contains(pair, StringComparer.Ordinal)).ToList())
                    if (_map.Remove(k, out var node)) _order.Remove(node);
            }
        }

        public void Clear()
        {
            lock (_lock) { _map.Clear(); _order.Clear(); }
        }
    }
}
