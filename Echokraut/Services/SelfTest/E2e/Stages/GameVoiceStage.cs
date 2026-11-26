using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Echokraut.DataClasses;
using Echokraut.Enums;
using Echokraut.Helper.Functional;
using Echokraut.Helper.Functional.GameVoice;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;

namespace Echokraut.Services.SelfTest.E2e.Stages;

/// <summary>
/// Build step B6 (plan › Coverage, R2/R5): the game voice on the local E2E engine. Hard constraint (root CLAUDE.md):
/// only through <see cref="IGameVoiceReferenceService"/>, only after the opt-in (decision D2, switched off again at
/// the end), only the same NPC, only in memory — every part runs inside an <see cref="E2eAudioFileWatch"/> and fails
/// when an audio file appeared under the install root, <c>pluginConfigs</c> or <c>%TEMP%</c>.
/// <list type="bullet">
/// <item><c>game-voice:engine-&lt;id&gt;</c> — reported from inside the engine loop (<see cref="RunOnEngineAsync"/>) while
/// that engine runs: <c>/health inline_reference</c> → availability (true → Available, else ServerUnsupported, e.g.
/// MOSS on PyTorch) → one line of the same NPC with the game voice resp. the voice pack.</item>
/// <item><c>game-voice:first-line</c> — two voiced NPCs without any index, one after the other: the first line waits for
/// the quick stage and must already speak with the game voice; quick and total ranking times into the report.</item>
/// <item><c>game-voice:character-field</c> — the NPC edit window's field through <see cref="GameVoiceNpcOverride"/>:
/// <c>-</c> → voice pack, the own token → game voice, empty → automatic, game voice.</item>
/// <item><c>game-voice:alias</c> — the maintenance walk's local alias: disabled → voice pack, assigned to its own
/// English name → game voice without re-ranking; "Export for the repo" in the resource format.</item>
/// </list>
/// </summary>
public sealed class GameVoiceStage : E2eStage
{
    public const string StageId = "game-voice";
    public const string PartPrefix = StageId + ":";
    public const string FirstLinePart = PartPrefix + "first-line";
    public const string CharacterFieldPart = PartPrefix + "character-field";
    public const string AliasPart = PartPrefix + "alias";

    /// <summary>The NPC the engine parts speak with (ranked once, by the first engine part).</summary>
    internal const string EngineNpcMark = "game-voice-engine-npc";

    /// <summary>The first-line NPCs (JSON), for the parts after it — also after a reload.</summary>
    internal const string FirstLineNpcsMark = "game-voice-first-line-npcs";

    /// <summary>The alias part's token and the local entry it had before (JSON, empty = none), put back at the next start.</summary>
    internal const string AliasOriginalMark = "game-voice-alias-original";

    /// <summary>How long the Whisper refinement after a first line may take before its timing is missing from the report.</summary>
    internal static readonly TimeSpan RankWait = TimeSpan.FromMinutes(3);

    private static readonly EKEventId Event = new(0, TextSource.VoiceTest);

    private readonly Configuration _config;
    private readonly IGameVoiceReferenceService _gameVoice;
    private readonly IGameVoiceMappingService _mapping;
    private readonly E2eGameVoiceGeneration _lines;
    private readonly Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> _health;
    private readonly Func<string?> _embeddedAliases;
    private readonly string? _pluginConfigDir;
    private readonly Func<E2eRequest, IEnumerable<(string Label, string? Path)>> _watchRoots;
    private readonly TimeSpan _rankWait;
    private readonly TimeSpan _poll;

    /// <param name="embeddedAliases">The JSON of the embedded <c>Resources/GameVoiceAliases.json</c> (format reference of the export).</param>
    /// <param name="pluginConfigDir">The plugin's config dir; its parent (<c>pluginConfigs</c>) is watched.</param>
    /// <param name="watchRoots">Folders the file watch covers; null = install root, <c>pluginConfigs</c>, <c>%TEMP%</c>.</param>
    public GameVoiceStage(Configuration config, IGameVoiceReferenceService gameVoice, IGameVoiceMappingService mapping,
        E2eGameVoiceGeneration lines, Func<CancellationToken, Task<EchokrauTtsHealthResponse?>> health,
        Func<string?> embeddedAliases, string? pluginConfigDir, TimeSpan? rankWait = null, TimeSpan? poll = null,
        Func<E2eRequest, IEnumerable<(string Label, string? Path)>>? watchRoots = null)
    {
        _config = config;
        _gameVoice = gameVoice;
        _mapping = mapping;
        _lines = lines;
        _health = health;
        _embeddedAliases = embeddedAliases;
        _pluginConfigDir = pluginConfigDir;
        _watchRoots = watchRoots ?? (r => E2eAudioFileWatch.RunRoots(r.InstallRoot, _pluginConfigDir));
        _rankWait = rankWait ?? RankWait;
        _poll = poll ?? TimeSpan.FromMilliseconds(250);
    }

    public override string Id => StageId;
    public override IReadOnlyList<string> Needs => [FirstTimeStage.StageId];

    public override string? UnmetNeed(E2eState state) => FirstUnmet(state, Needs) ?? EngineStage.ActiveEngineUnmet(state);

    // Two first lines with their Whisper refinement, five more lines, the export.
    public override TimeSpan Timeout => E2eGameVoiceGeneration.Timeout + _rankWait * 2 + TimeSpan.FromMinutes(10);

    /// <summary>Per engine of the loop: <c>game-voice:engine-qwen-0.6b</c>.</summary>
    public static string EnginePartId(string engineId) => PartPrefix + "engine-" + engineId.Trim().ToLowerInvariant();

    /// <summary>Whether the engine loop reports the per-engine parts: only when this stage belongs to the run.</summary>
    public static bool InRun(E2eRequest request) => request.Includes(StageId);

    public override async Task<SelfTestResult> RunAsync(E2eContext ctx, CancellationToken ct)
    {
        if (!ctx.State.ConfigIsE2e)
            return SelfTestResult.Skip("Only on an E2E config (after reset), never on the real one.");

        // Left over from a run that unloaded mid-stage.
        E2eGameVoiceGeneration.RestoreOptIn(ctx, _config, _gameVoice);
        PutBackAlias(ctx);
        var outcomes = new List<(string Id, SelfTestOutcome Outcome)>();
        try
        {
            E2eGameVoiceGeneration.EnableOptIn(ctx, _config, rankOnFirstLine: true);
            var availability = await _gameVoice.RefreshAvailabilityAsync(Event, ct).ConfigureAwait(false);
            var blocker = availability == GameVoiceAvailability.Available
                ? null
                : $"game voice is {availability} on {_config.EchokrauTts.TtsBackendArg} (needs an engine with inline_reference)";

            var first = await RunPartAsync(ctx, outcomes, FirstLinePart, blocker, c => Watched(ctx, FirstLineAsync, c), ct).ConfigureAwait(false);
            var npcs = FirstLineNpcs(ctx);
            var needFirst = blocker ?? (first == SelfTestOutcome.Pass && npcs.Count >= 2 ? null : $"needs {FirstLinePart} ({E2eLogFormat.Kind(first)})");
            await RunPartAsync(ctx, outcomes, CharacterFieldPart, needFirst,
                c => Watched(ctx, (x, cc) => CharacterFieldAsync(x, npcs[0], cc), c), ct).ConfigureAwait(false);
            await RunPartAsync(ctx, outcomes, AliasPart, needFirst,
                c => Watched(ctx, (x, cc) => AliasAsync(x, npcs[1], cc), c), ct).ConfigureAwait(false);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                PutBackAlias(ctx);
                E2eGameVoiceGeneration.RestoreOptIn(ctx, _config, _gameVoice);
            }
        }
        return FromParts(outcomes);
    }

    // ── per engine (called by the engine loop) ─────────────────────────────

    /// <summary>
    /// One engine of the loop, while it runs: <c>/health inline_reference</c> decides what the plugin must do
    /// (Available → the line speaks with the game voice; else ServerUnsupported → voice pack, game audio never sent).
    /// </summary>
    public async Task<SelfTestResult> RunOnEngineAsync(E2eContext ctx, string engineId, CancellationToken ct)
    {
        E2eGameVoiceGeneration.EnableOptIn(ctx, _config);
        try
        {
            return await Watched(ctx, async (x, c) =>
            {
                var health = await _health(c).ConfigureAwait(false);
                var expected = E2eGameVoiceChecks.ExpectedAvailability(health?.inline_reference);
                var actual = await _gameVoice.RefreshAvailabilityAsync(Event, c).ConfigureAwait(false);
                var inline = health == null ? "no /health" : $"inline_reference {health.inline_reference?.ToString().ToLowerInvariant() ?? "missing"}";
                if (actual != expected)
                    return SelfTestResult.Fail($"{engineId}: {inline}, availability {actual}, expected {expected}.");

                var (npc, rankDetail) = await EngineNpcAsync(x, c).ConfigureAwait(false);
                if (npc == null) return SelfTestResult.Fail($"{engineId}: {inline} -> {actual}; {rankDetail}");
                var line = await _lines.SpeakAsync(x, npc, c).ConfigureAwait(false);
                var want = actual == GameVoiceAvailability.Available ? ReferenceSource.GameVoice : ReferenceSource.VoicePack;
                var detail = $"{engineId}: {inline} -> {actual}; {npc}{rankDetail}: {E2eGameVoiceGeneration.Describe(line)}";
                return E2eGameVoiceGeneration.Mismatch(line, want, npc) is { } wrong
                    ? SelfTestResult.Fail($"{wrong} | {detail}")
                    : SelfTestResult.Pass(detail);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            if (!ct.IsCancellationRequested) E2eGameVoiceGeneration.RestoreOptIn(ctx, _config, _gameVoice);
        }
    }

    /// <summary>The engine parts' NPC: sampled and ranked by the first one, then only reused (index in the E2E DB).</summary>
    private async Task<(string? Npc, string Detail)> EngineNpcAsync(E2eContext ctx, CancellationToken ct)
    {
        if (ctx.State.Marks.GetValueOrDefault(EngineNpcMark) is { Length: > 0 } known) return (known, "");
        var npc = (await _gameVoice.SampleVoicedNpcsAsync(_lines.Language, 1, ct: ct).ConfigureAwait(false)).FirstOrDefault();
        if (npc == null) return (null, $"no voiced NPC with its own token in the text index ({_lines.Language})");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stored = await _lines.RankAsync(npc.Name, ct).ConfigureAwait(false);
        if (stored == 0) return (null, $"ranking {npc.Name} stored no clip");
        ctx.State.Marks[EngineNpcMark] = npc.Name;
        ctx.Save();
        return (npc.Name, $" ({stored} clip(s) ranked in {watch.ElapsedMilliseconds} ms)");
    }

    // ── first line without index ───────────────────────────────────────────

    private async Task<SelfTestResult> FirstLineAsync(E2eContext ctx, CancellationToken ct)
    {
        var npcs = await _gameVoice.SampleVoicedNpcsAsync(_lines.Language, 2, unindexedOnly: true, ct).ConfigureAwait(false);
        if (npcs.Count < 2)
            return SelfTestResult.Fail($"Only {npcs.Count} voiced NPC(s) without an index found ({_lines.Language}).");
        ctx.State.Marks[FirstLineNpcsMark] = JsonSerializer.Serialize(npcs);
        ctx.Save();

        using var ranks = new E2eRankRecorder(_gameVoice);
        var problems = new List<string>();
        var details = new List<string>();
        foreach (var npc in npcs)
        {
            ctx.Progress($"first line of {npc.Name} ({npc.Token}, {npc.Lines} voiced line(s)) without an index");
            var line = await _lines.SpeakAsync(ctx, npc.Name, ct).ConfigureAwait(false);
            if (E2eGameVoiceGeneration.Mismatch(line, ReferenceSource.GameVoice, npc.Name) is { } wrong) problems.Add(wrong);
            var timing = await WaitForRankingAsync(ranks, npc.Token, ct).ConfigureAwait(false);
            if (timing == null) problems.Add($"{npc.Name}: no ranking of {npc.Token} finished within {_rankWait.TotalSeconds:0} s");
            details.Add($"{npc.Name} ({npc.Token}): {E2eGameVoiceGeneration.Describe(line)}; " +
                        (timing == null ? "ranking -" : DescribeRanking(timing)));
        }
        var detail = string.Join(" | ", details);
        return FromProblems(problems, detail);
    }

    /// <summary>The first finished ranking of <paramref name="token"/> (the line's quick stage + its Whisper refinement).</summary>
    private async Task<GameVoiceRankTiming?> WaitForRankingAsync(E2eRankRecorder ranks, string token, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _rankWait;
        while (true)
        {
            if (ranks.For(token).FirstOrDefault() is { } t) return t;
            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(_poll, ct).ConfigureAwait(false);
        }
    }

    internal static string DescribeRanking(GameVoiceRankTiming t) =>
        $"ranking quick {t.QuickMs} ms, total {t.TotalMs} ms{(t.WithAsr ? " (Whisper)" : " (no Whisper)")}, {t.Stored} clip(s)";

    private static List<GameVoiceSampleNpc> FirstLineNpcs(E2eContext ctx) => FirstLineNpcs(ctx.State);

    /// <summary>Every NPC a game-voice part of this run spoke with (engine parts + first-line).</summary>
    internal static IEnumerable<string> NpcsSpokenBy(E2eState state)
    {
        if (state.Marks.GetValueOrDefault(EngineNpcMark) is { Length: > 0 } engineNpc) yield return engineNpc;
        foreach (var npc in FirstLineNpcs(state)) yield return npc.Name;
    }

    private static List<GameVoiceSampleNpc> FirstLineNpcs(E2eState state)
    {
        if (state.Marks.GetValueOrDefault(FirstLineNpcsMark) is not { Length: > 0 } json) return new();
        try { return JsonSerializer.Deserialize<List<GameVoiceSampleNpc>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    // ── character field ────────────────────────────────────────────────────

    private async Task<SelfTestResult> CharacterFieldAsync(E2eContext ctx, GameVoiceSampleNpc npc, CancellationToken ct)
    {
        var key = GameVoiceReferenceService.OverrideKey(_lines.Language, npc.Name);
        // Recorded once (touchedOptions), the edits then go to the live dictionary like the window's.
        E2eOptions.Set(ctx.State, _config, nameof(Configuration.GameVoiceNpcOverrides),
            new Dictionary<string, string>(_config.GameVoiceNpcOverrides));
        ctx.Save();
        var steps = new (string Input, string Display, ReferenceSource Want)[]
        {
            (GameVoiceNpcOverride.NoneMarker, GameVoiceNpcOverride.NoneMarker, ReferenceSource.VoicePack),
            (npc.Token, npc.Token.ToUpperInvariant(), ReferenceSource.GameVoice),
            ("", "", ReferenceSource.GameVoice),
        };
        var problems = new List<string>();
        var details = new List<string>();
        try
        {
            foreach (var (input, display, want) in steps)
            {
                GameVoiceNpcOverride.Apply(_config.GameVoiceNpcOverrides, key, input);
                _config.Save();
                var shown = GameVoiceNpcOverride.Display(_config.GameVoiceNpcOverrides, key);
                var label = input.Length == 0 ? "empty" : $"'{input}'";
                if (shown != display) problems.Add($"field {label} shows '{shown}', expected '{display}'");
                var line = await _lines.SpeakAsync(ctx, npc.Name, ct).ConfigureAwait(false);
                if (E2eGameVoiceGeneration.Mismatch(line, want, $"field {label}") is { } wrong) problems.Add(wrong);
                details.Add($"{label} -> {E2eGameVoiceGeneration.Describe(line)}");
            }
        }
        finally
        {
            E2eOptions.Restore(ctx.State, _config, [nameof(Configuration.GameVoiceNpcOverrides)]);
            _config.Save();
            ctx.Save();
        }
        var detail = $"{npc.Name}: {string.Join(", ", details)}";
        return FromProblems(problems, detail);
    }

    // ── alias assign + export ──────────────────────────────────────────────

    private async Task<SelfTestResult> AliasAsync(E2eContext ctx, GameVoiceSampleNpc npc, CancellationToken ct)
    {
        var token = VoiceExtractKey.Normalize(npc.Token);
        var before = _mapping.LocalAliases.FirstOrDefault(a => VoiceExtractKey.Normalize(a.ShortName) == token);
        ctx.State.Marks[AliasOriginalMark] = JsonSerializer.Serialize(new AliasOriginal(token, before));
        ctx.Save();

        var problems = new List<string>();
        var details = new List<string>();
        using var ranks = new E2eRankRecorder(_gameVoice);

        _mapping.SetLocalAlias(new GameVoiceAliasEntry { ShortName = token, Disabled = true, Comment = "E2E: no game voice" });
        var off = await _lines.SpeakAsync(ctx, npc.Name, ct).ConfigureAwait(false);
        if (E2eGameVoiceGeneration.Mismatch(off, ReferenceSource.VoicePack, $"{token} disabled") is { } w1) problems.Add(w1);
        details.Add($"disabled -> {E2eGameVoiceGeneration.Describe(off)}");

        _mapping.SetLocalAlias(new GameVoiceAliasEntry { ShortName = token, NpcName = npc.EnglishName, Comment = "E2E: assigned" });
        var on = await _lines.SpeakAsync(ctx, npc.Name, ct).ConfigureAwait(false);
        if (E2eGameVoiceGeneration.Mismatch(on, ReferenceSource.GameVoice, $"{token} -> {npc.EnglishName}") is { } w2) problems.Add(w2);
        details.Add($"assigned -> {E2eGameVoiceGeneration.Describe(on)}");
        // The index is per token: a mapping change must not rank again (§11.5 "Wirkung").
        if (ranks.Timings.Count > 0)
            problems.Add($"alias change ranked again: {string.Join(", ", ranks.Timings.Select(t => t.Token))}");

        var path = Path.Combine(GameVoiceReferenceService.HarvestDirectoryOf(_config), GameVoiceReferenceService.AliasRepoExportFile);
        var existed = File.Exists(path);
        try
        {
            var count = _mapping.ExportAliasesForRepo(path);
            var embedded = _embeddedAliases();
            if (embedded == null) problems.Add("embedded GameVoiceAliases.json unreadable");
            else
            {
                var (exportProblems, exportDetail) = E2eGameVoiceChecks.JudgeAliasExport(File.ReadAllText(path), embedded, token, npc.EnglishName);
                problems.AddRange(exportProblems);
                details.Add($"{exportDetail} (returned {count})");
            }
        }
        finally
        {
            if (!existed && File.Exists(path)) File.Delete(path);
        }

        var detail = $"{npc.Name} ({token}): {string.Join(", ", details)}";
        return FromProblems(problems, detail);
    }

    internal sealed record AliasOriginal(string Token, GameVoiceAliasEntry? Entry);

    /// <summary>Puts the alias part's token back to the local entry it had before (or none).</summary>
    private void PutBackAlias(E2eContext ctx)
    {
        if (ctx.State.Marks.GetValueOrDefault(AliasOriginalMark) is not { Length: > 0 } json) return;
        AliasOriginal? original = null;
        try { original = JsonSerializer.Deserialize<AliasOriginal>(json); }
        catch (JsonException) { }
        if (original != null)
        {
            if (original.Entry != null) _mapping.SetLocalAlias(original.Entry);
            else _mapping.RemoveLocalAlias(original.Token);
        }
        ctx.State.Marks.Remove(AliasOriginalMark);
        ctx.Save();
    }

    // ── file watch ─────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="part"/> inside an <see cref="E2eAudioFileWatch"/>: an audio file on disk fails it.</summary>
    private Task<SelfTestResult> Watched(E2eContext ctx, Func<E2eContext, CancellationToken, Task<SelfTestResult>> part,
        CancellationToken ct) =>
        E2eAudioFileWatch.RunAsync(_watchRoots(ctx.Request), () => part(ctx, ct));
}
