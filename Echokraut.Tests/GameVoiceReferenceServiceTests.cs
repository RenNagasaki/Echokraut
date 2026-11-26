using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Echokraut.Backend;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Helper.Functional.GameVoice;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using EchoScd;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// <see cref="GameVoiceReferenceService"/> against a fake game-data source (synthetic PCM, never
/// real game audio), a mocked EchokrauTTS client, an in-memory index store keyed like the real
/// table (language, token, epoch) and a settable story progress.
/// </summary>
public sealed class GameVoiceReferenceServiceTests : IDisposable
{
    private const int YshtolaId = 10;
    private const string Ysh = "yshtola";
    private static readonly EKEventId Ev = new(0, TextSource.None);

    private readonly Configuration _config = new()
    {
        BackendSelection = TTSBackends.EchokrauTTS,
        GameVoiceReferenceEnabled = true,
        GameVoiceRankOnConversationStart = false,
    };

    private readonly FakeData _data = new();
    private readonly Mock<IEchokrauTtsReferenceClient> _client = new();
    private readonly Mock<IEchokrauTtsInstanceService> _instance = new();
    private readonly Mock<IDatabaseService> _db = new();
    private readonly Mock<IStoryProgressService> _progress = new();
    private readonly Dictionary<(int Lang, string Token, string Epoch), List<GameVoiceIndexEntity>> _store = new();
    private readonly string _configDir = Path.Combine(Path.GetTempPath(), "ek-gvr-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _created = new();
    // The mapping service of the last Create(): tests drive alias changes through it.
    private GameVoiceMappingService _mapping = null!;
    // Waits a test is not meant to exceed. Never reached when the code under test is right.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private EchokrauTtsHealthResponse _health = new() { status = "ok", inline_reference = true, asr_check = true };
    // Story progress as a clip token: null = unknown; a boundary counts as reached when <= it.
    private string? _reached;
    // Line text → story context of its quest (the accepted quest it belongs to).
    private readonly Dictionary<string, StoryContext> _lineContexts = new();

    public GameVoiceReferenceServiceTests()
    {
        Directory.CreateDirectory(_configDir);
        _config.SaveToLocal = true;
        _config.LocalSaveLocation = _configDir;
        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _health);
        // Default ASR: hears exactly the game text of the clip (WER 0).
        _client.Setup(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EKEventId _, short[] pcm, string? _, CancellationToken _) => _data.TextForPcm(pcm));

        _db.Setup(d => d.GetGameVoiceIndex(It.IsAny<int>(), It.IsAny<string>()))
            .Returns((int l, string t) => _store.Where(kv => kv.Key.Lang == l && kv.Key.Token == t)
                .SelectMany(kv => kv.Value).ToList());
        _db.Setup(d => d.ReplaceGameVoiceIndex(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<GameVoiceIndexEntity>>()))
            .Callback((int l, string t, string e, IReadOnlyList<GameVoiceIndexEntity> rows) =>
            {
                lock (_store)
                {
                    if (rows.Count == 0) _store.Remove((l, t, e));
                    else _store[(l, t, e)] = rows.ToList();
                }
            });
        _db.Setup(d => d.GetGameVoiceIndexStates(It.IsAny<int>()))
            .Returns((int l) => _store.Where(kv => kv.Key.Lang == l)
                .Select(kv => new GameVoiceIndexState(kv.Key.Token, kv.Key.Epoch, kv.Value.Min(r => r.IndexVersion), kv.Value[0].LinesHash))
                .ToList());
        _db.Setup(d => d.GetInstancesForCharacter(It.IsAny<int>())).Returns(new List<CharacterInstanceEntity>());

        _progress.Setup(p => p.ReachedPatchKey).Returns(() => _reached);
        _progress.Setup(p => p.ContextFor(It.IsAny<string?>(), It.IsAny<ClientLanguage>()))
            .Returns((string? text, ClientLanguage _) =>
                text != null && _lineContexts.TryGetValue(text, out var ctx) ? ctx
                : _reached == null ? null : new StoryContext(_reached, StoryContextSource.Progress));
        _progress.Setup(p => p.IsReached(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string t, string key) => string.CompareOrdinal(t, key) <= 0);

        _data.EnglishNames[1] = "Y'shtola";
        _data.EnglishNames[2] = "Thancred";
        _data.EnglishNames[3] = "G'raha Tia";
    }

    public void Dispose()
    {
        foreach (var s in _created) s.Dispose();
        try { Directory.Delete(_configDir, true); } catch { /* best effort */ }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition());
    }

    /// <param name="time">Clock for the first-line budget; the real one unless a test steers it.</param>
    private GameVoiceReferenceService Create(TimeProvider? time = null)
    {
        var log = new Mock<ILogService>().Object;
        _mapping = new GameVoiceMappingService(log, _configDir, null, null);
        _created.Add(_mapping);
        var svc = new GameVoiceReferenceService(_config, log, _db.Object, _data, _client.Object, _mapping, _progress.Object, _instance.Object,
            time ?? TimeProvider.System);
        _created.Add(svc);
        return svc;
    }

    private static GameVoiceSpeaker Yshtola(ClientLanguage lang = ClientLanguage.English) =>
        new(YshtolaId, "Y'shtola", 0, lang);

    /// <summary>All stored rows of a token in English (every epoch), rank order.</summary>
    private List<GameVoiceIndexEntity> Stored(string token, ClientLanguage lang = ClientLanguage.English)
    {
        lock (_store)
            return _store.Where(kv => kv.Key.Lang == (int)lang && kv.Key.Token == token)
                .SelectMany(kv => kv.Value).OrderBy(r => r.Epoch).ThenBy(r => r.Rank).ToList();
    }

    private List<GameVoiceIndexEntity> Clips(string token, ClientLanguage lang = ClientLanguage.English) =>
        Stored(token, lang).Where(r => r.Rank > 0).ToList();

    private async Task WaitForStored(string token, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (Stored(token).Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
    }

    private void WriteSplits(string json) =>
        File.WriteAllText(Path.Combine(_configDir, GameVoiceMappingService.SplitsOverrideFile), json);

    private void WriteAliases(string json) =>
        File.WriteAllText(Path.Combine(_configDir, GameVoiceMappingService.AliasOverrideFile), json);

    /// <summary>Text with the English median rate for <paramref name="seconds"/> (tempo score ≈ 1).</summary>
    private static string Words(double seconds, string seed) =>
        string.Join(" ", Enumerable.Range(0, (int)Math.Round(seconds * 2.6)).Select(i => seed + i));

    private async Task<GameVoiceReferenceService> AvailableService(TimeProvider? time = null)
    {
        var svc = Create(time);
        Assert.Equal(GameVoiceAvailability.Available, await svc.RefreshAvailabilityAsync(Ev));
        return svc;
    }

    // ── Availability (§5.3) ─────────────────────────────────────────────

    [Fact]
    public void Disabled_ByDefaultConfig()
    {
        Assert.False(new Configuration().GameVoiceReferenceEnabled);
        _config.GameVoiceReferenceEnabled = false;
        var svc = Create();
        Assert.Equal(GameVoiceAvailability.Disabled, svc.Availability);
        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
        _db.Verify(d => d.GetGameVoiceIndex(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AllTalk_IsNeverSupported_AndHealthIsNotAsked()
    {
        _config.BackendSelection = TTSBackends.Alltalk;
        var svc = Create();
        Assert.Equal(GameVoiceAvailability.WrongBackend, await svc.RefreshAvailabilityAsync(Ev));
        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
        _client.Verify(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task HealthWithoutInlineReference_FallsBackToVoicePack(bool? flag)
    {
        _health = new EchokrauTtsHealthResponse { status = "ok", inline_reference = flag };
        var svc = Create();
        Assert.Equal(GameVoiceAvailability.ServerUnsupported, await svc.RefreshAvailabilityAsync(Ev));
        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    [Fact]
    public async Task UnreachableServer_StaysUnknown_AndTheNextLineAsksAgain()
    {
        // A restarting wrapper is not one without game voices: no ProbeTtl of voice pack.
        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EchokrauTtsHealthResponse?)null);
        var svc = Create();
        Assert.Equal(GameVoiceAvailability.Unknown, await svc.RefreshAvailabilityAsync(Ev));

        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EchokrauTtsHealthResponse { inline_reference = true });
        Assert.Null(svc.TryGetReference(Yshtola(), Ev)); // starts the new probe
        await WaitUntil(() => svc.Availability == GameVoiceAvailability.Available);
    }

    [Fact]
    public async Task InstanceReady_ProbesAgain_SoTheNewEnginesLimitApplies()
    {
        // Qwen (20 s) → Pocket (15 s): the first line after the switch must not send 20 s.
        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EchokrauTtsHealthResponse { inline_reference = true, inline_ref_max_seconds = 20 });
        var svc = Create();
        await svc.RefreshAvailabilityAsync(Ev);

        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EchokrauTtsHealthResponse { inline_reference = true, inline_ref_max_seconds = 15 });
        _instance.Raise(i => i.OnInstanceReady += null);

        await WaitUntil(() => svc.MaxReferenceSeconds == 15);
        _client.Verify(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReportReferenceRejected_ResetsToUnknown()
    {
        var svc = await AvailableService();
        var raised = new List<GameVoiceAvailability>();
        svc.AvailabilityChanged += raised.Add;
        svc.ReportReferenceRejected(Ev);
        Assert.Equal(GameVoiceAvailability.Unknown, svc.Availability);
        Assert.Contains(GameVoiceAvailability.Unknown, raised);
    }

    [Fact]
    public void UnknownProbe_ReturnsNull_WithoutWaiting()
    {
        var svc = Create(); // never probed
        Assert.Equal(GameVoiceAvailability.Unknown, svc.Availability);
        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    // ── Candidates: same NPC only (§2.2, §3, F2) ────────────────────────

    [Fact]
    public async Task Rank_StoresOnlyClipsOfTheSameNpc_UnderItsToken()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(Yshtola(), Ev));

        var row = Assert.Single(Stored(Ysh));
        Assert.Equal("vo_voiceman_06006_000010", row.AudioBase);
        Assert.Equal(GameVoiceRanking.IndexVersion, row.IndexVersion);
        Assert.Equal(1, row.Rank);
        Assert.Equal("", row.Epoch);
        Assert.Empty(Stored("thancred"));
    }

    [Fact]
    public async Task Rank_UsesAliasList()
    {
        // MYSTERYVOICE matches no NPC name; the embedded GameVoiceAliases.json maps it to G'raha Tia.
        _data.AddLine("TEXT_VOICEMAN_04004_000010_MYSTERYVOICE", 8, Words(8, "g"));
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(new GameVoiceSpeaker(20, "G'raha Tia", 0, ClientLanguage.English), Ev));
        Assert.Equal(0, await svc.RankAsync(Yshtola(), Ev));
        Assert.Single(Clips("mysteryvoice"));
    }

    [Fact]
    public async Task Rank_DisabledAlias_GivesNoGameVoice()
    {
        WriteAliases("{ \"version\": 1, \"aliases\": [ { \"ShortName\": \"YSHTOLA\", \"Disabled\": true } ] }");
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();

        Assert.Equal(0, await svc.RankAsync(Yshtola(), Ev));
    }

    [Fact]
    public async Task Rank_NonEnglishClient_ResolvesNameThroughClientLanguageSheet()
    {
        _data.ClientNames[ClientLanguage.German] = new Dictionary<uint, string> { [1] = "Y'shtola" };
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"), ClientLanguage.German);
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(Yshtola(ClientLanguage.German), Ev));
        Assert.Single(Clips(Ysh, ClientLanguage.German));
    }

    [Fact]
    public async Task Rank_DropsShortNonSpeechAndUndecodableClips_AndRemembersThat()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 2, Words(2, "a"));          // too short
        _data.AddLine("TEXT_VOICEMAN_06006_000020_YSHTOLA", 8, "Hahahaha!");             // not speech
        _data.AddLine("TEXT_VOICEMAN_06006_000030_YSHTOLA", 8, Words(8, "c"), decodable: false);
        var svc = await AvailableService();

        Assert.Equal(0, await svc.RankAsync(Yshtola(), Ev));
        // "Ranked, nothing usable" marker: a resumed batch does not rank it again.
        var marker = Assert.Single(Stored(Ysh));
        Assert.Equal(0, marker.Rank);
        Assert.Equal("", marker.AudioBase);
        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    [Fact]
    public async Task Rank_KeepsAtMostThree_BestFirst_HighWerLoses()
    {
        for (var i = 1; i <= 5; i++)
            _data.AddLine($"TEXT_VOICEMAN_06006_0000{i}0_YSHTOLA", 5 + i, Words(5 + i, "w" + i));
        // The newest clip (highest audio base) is heard as something else entirely.
        var bad = "vo_voiceman_06006_000050";
        _client.Setup(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EKEventId _, short[] pcm, string? _, CancellationToken _) =>
                _data.AudioBaseForPcm(pcm) == bad ? "completely different words here" : _data.TextForPcm(pcm));
        var svc = await AvailableService();

        Assert.Equal(GameVoiceRanking.MaxReferences, await svc.RankAsync(Yshtola(), Ev));

        var rows = Clips(Ysh);
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Rank));
        Assert.True(rows[0].Score >= rows[1].Score && rows[1].Score >= rows[2].Score);
        Assert.DoesNotContain(rows, r => r.AudioBase == bad && r.Rank == 1);
    }

    [Fact]
    public async Task Rank_WithoutAsrEndpoint_StillRanks()
    {
        _health = new EchokrauTtsHealthResponse { status = "ok", inline_reference = true, asr_check = false };
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(Yshtola(), Ev));
        _client.Verify(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.EndsWith(GameVoiceReferenceService.NoAsrHashSuffix, Stored(Ysh)[0].LinesHash);
    }

    // ── Voice-actor epochs by story progress (§11.4) ─────────────────────

    private const string Splits6100 =
        "{ \"version\": 2, \"splits\": [ { \"npcName\": \"Y'shtola\", \"language\": \"EN\", \"boundaryPatches\": [\"06100\"] } ] }";

    private void AddOldAndNewYshtola()
    {
        WriteSplits(Splits6100);
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "old"));
        _data.AddLine("TEXT_VOICEMAN_06100_000010_YSHTOLA", 8, Words(8, "new"));
    }

    [Fact]
    public async Task Epoch_UnknownProgress_UsesTheNewestActor()
    {
        AddOldAndNewYshtola();
        var svc = await AvailableService();

        await svc.RankAsync(Yshtola(), Ev);

        var row = Assert.Single(Clips(Ysh));
        Assert.Equal("vo_voiceman_06100_000010", row.AudioBase);
        Assert.Equal("Post06100", row.Epoch);
    }

    [Fact]
    public async Task Epoch_PlayerBeforeTheBoundary_HearsTheOldActor()
    {
        AddOldAndNewYshtola();
        _reached = "06006";
        var svc = await AvailableService();

        await svc.RankAsync(Yshtola(), Ev);

        var row = Assert.Single(Clips(Ysh));
        Assert.Equal("vo_voiceman_06006_000010", row.AudioBase);
        Assert.Equal("Pre06100", row.Epoch);
        Assert.Equal("vo_voiceman_06006_000010", Assert.Single(svc.TryGetReference(Yshtola(), Ev)!.ClipIds));
    }

    [Fact]
    public async Task Epoch_ProgressChanges_NextLinePicksTheNewEpochWithoutReRanking()
    {
        AddOldAndNewYshtola();
        _reached = "06006";
        var svc = await AvailableService();
        await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null); // both epochs
        Assert.Equal(2, Clips(Ysh).Count);
        var old = svc.TryGetReference(Yshtola(), Ev);

        _reached = "06100";
        var now = svc.TryGetReference(Yshtola(), Ev);

        Assert.Equal("vo_voiceman_06006_000010", Assert.Single(old!.ClipIds));
        Assert.Equal("vo_voiceman_06100_000010", Assert.Single(now!.ClipIds));
    }

    [Fact]
    public async Task Epoch_LineOfAReplayedQuest_HearsItsEpoch_NotTheFurthestProgress()
    {
        // New Game+ / a late old side quest: the player is past 6.1, but this line belongs to a 6.0
        // quest → the old actor, matching that quest's voiced cutscenes. Other lines stay new.
        AddOldAndNewYshtola();
        _reached = "06100";
        _lineContexts["Replayed line"] = new StoryContext("06006", StoryContextSource.LineQuest, 70000);
        var svc = await AvailableService();
        await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null);

        var replayed = svc.TryGetReference(Yshtola() with { LineText = "Replayed line" }, Ev);
        var current = svc.TryGetReference(Yshtola() with { LineText = "Some current line" }, Ev);

        Assert.Equal("vo_voiceman_06006_000010", Assert.Single(replayed!.ClipIds));
        Assert.Equal("vo_voiceman_06100_000010", Assert.Single(current!.ClipIds));
    }

    [Fact]
    public async Task Epoch_QuickStage_StaysInsideTheChosenEpoch()
    {
        // The Zwischenlösung's flaw: quick stage (newest 10) and refine stage (newest 40) could pick
        // different actors. Now both run inside one epoch.
        WriteSplits(Splits6100);
        for (var i = 1; i <= 12; i++)
            _data.AddLine($"TEXT_VOICEMAN_06006_{i * 10:000000}_YSHTOLA", 8, Words(8, "old" + i));
        _data.AddLine("TEXT_VOICEMAN_06100_000010_YSHTOLA", 8, Words(8, "new"));
        _reached = "06006";
        _config.GameVoiceRankOnConversationStart = true;
        var svc = await AvailableService();
        svc.FirstLineWait = TimeSpan.FromSeconds(10);

        var first = await svc.GetReferenceAsync(Yshtola(), Ev);
        await WaitForStored(Ysh);

        Assert.All(first!.ClipIds, id => Assert.StartsWith("vo_voiceman_06006_", id));
        Assert.All(Clips(Ysh), r => Assert.Equal("Pre06100", r.Epoch));
    }

    // ── Live path (§2.1, §11.1) ─────────────────────────────────────────

    [Fact]
    public async Task TryGetReference_AfterRanking_ReturnsGameTextAndIds_ThenServesFromMemory()
    {
        var text = Words(8, "y");
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, text);
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);
        var decodesAfterRanking = _data.DecodeCount;

        var reference = svc.TryGetReference(Yshtola(), Ev);

        Assert.NotNull(reference);
        Assert.Equal(text, reference!.Text);
        Assert.Equal(new[] { "vo_voiceman_06006_000010" }, reference.ClipIds);
        Assert.Equal(8.0, reference.Seconds, 2);
        Assert.Same(reference, svc.TryGetReference(Yshtola(), Ev));
        Assert.Equal(decodesAfterRanking + 1, _data.DecodeCount);
    }

    [Theory]
    [InlineData(null, 1)]   // older wrapper, no limit reported: 15 s → 8 + 0.3 + 7.5 s does not fit
    [InlineData(20.0, 2)]   // Qwen reports 20 s: both clips
    public async Task TryGetReference_SizesTheReferenceByTheWrapperLimit(double? limit, int clips)
    {
        _health = new EchokrauTtsHealthResponse { status = "ok", inline_reference = true, asr_check = true, inline_ref_max_seconds = limit };
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_YSHTOLA", 7.5, Words(8, "z"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);

        Assert.Equal(clips, svc.TryGetReference(Yshtola(), Ev)!.ClipIds.Count);
    }

    [Fact]
    public async Task TryGetReference_IndexSurvivesAMappingReset_AndServesUnmetNpcs()
    {
        // Live 2026-11-15: Papalymo was character 7, 11 and 15 — the per-character index was lost
        // every time. Per token, any character id (or none) finds it.
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);

        Assert.Equal(new[] { "vo_voiceman_06006_000010" },
            svc.TryGetReference(new GameVoiceSpeaker(99, "Y'shtola", 0, ClientLanguage.English), Ev)?.ClipIds);
        Assert.Equal(new[] { "vo_voiceman_06006_000010" },
            svc.TryGetReference(new GameVoiceSpeaker(0, "Y'shtola", 0, ClientLanguage.English), Ev)?.ClipIds);
    }

    [Fact]
    public async Task TryGetReference_ClipPatchedAway_DropsIndex()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);

        _data.Undecodable.Add("vo_voiceman_06006_000010");

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
        Assert.Empty(Stored(Ysh));
    }

    [Fact]
    public async Task TryGetReference_OlderIndexVersion_IsNotUsed()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);
        foreach (var r in Stored(Ysh)) r.IndexVersion = GameVoiceRanking.IndexVersion - 1;

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    [Fact]
    public async Task TryGetReference_ChangedGameText_IsNotUsed()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);
        foreach (var r in Stored(Ysh)) r.LinesHash = "patched";

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    [Fact]
    public async Task TryGetReference_NoIndex_RanksInBackgroundWhenEnabled()
    {
        _config.GameVoiceRankOnConversationStart = true;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev);

        Assert.Null(svc.TryGetReference(Yshtola(), Ev)); // this line: voice pack

        await WaitForStored(Ysh);
        Assert.Single(Clips(Ysh));
    }

    [Fact]
    public async Task TryGetReference_UserOverride_NoGameVoice()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);

        _config.GameVoiceNpcOverrides[GameVoiceReferenceService.OverrideKey(ClientLanguage.English, "Y'shtola")] = "";

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    [Fact]
    public async Task TryGetReference_UserOverride_OtherToken()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        var svc = await AvailableService();
        await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null);

        _config.GameVoiceNpcOverrides[GameVoiceReferenceService.OverrideKey(ClientLanguage.English, "Some Stranger")] = "THANCRED";

        var reference = svc.TryGetReference(new GameVoiceSpeaker(0, "Some Stranger", 0, ClientLanguage.English), Ev);
        Assert.Equal("vo_voiceman_06006_000020", Assert.Single(reference!.ClipIds));
    }

    [Fact]
    public async Task LocalAliasChange_TakesEffectWithoutReRanking()
    {
        _data.AddLine("TEXT_VOICEMAN_04004_000010_STRANGEHOODED", 8, Words(8, "h"));
        var svc = await AvailableService();
        await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null);
        var stranger = new GameVoiceSpeaker(0, "Thancred", 0, ClientLanguage.English);
        Assert.Null(svc.TryGetReference(stranger, Ev));
        var decodes = _data.DecodeCount;

        // What the maintenance window does: a local alias, then the mapping service raises Changed.
        _mapping.SetLocalAlias(new GameVoiceAliasEntry { ShortName = "STRANGEHOODED", NpcName = "Thancred" });

        Assert.Equal(new[] { "vo_voiceman_04004_000010" }, svc.TryGetReference(stranger, Ev)?.ClipIds);
        Assert.Equal(decodes + 1, _data.DecodeCount); // decoded the stored clip, no ranking
        _db.Verify(d => d.ReplaceGameVoiceIndex(It.IsAny<int>(), "strangehooded", It.IsAny<string>(), It.IsAny<IReadOnlyList<GameVoiceIndexEntity>>()), Times.Once);
    }

    // ── First line of a new NPC (2026-11-15): wait for the quick stage, refine with Whisper later ──

    [Fact]
    public async Task GetReferenceAsync_FirstLine_GetsGameVoiceBeforeWhisperFinishes()
    {
        _config.GameVoiceRankOnConversationStart = true;
        var text = Words(8, "y");
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, text);
        var asrRelease = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Setup(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(asrRelease.Task);
        var svc = await AvailableService();
        svc.FirstLineWait = TimeSpan.FromSeconds(10);

        var reference = await svc.GetReferenceAsync(Yshtola(), Ev);

        Assert.NotNull(reference);                         // the FIRST line already has the game voice
        Assert.Equal(text, reference!.Text);
        Assert.Empty(Stored(Ysh));                         // Whisper still running: nothing stored yet

        asrRelease.SetResult(text);
        await WaitForStored(Ysh);
        Assert.Single(Clips(Ysh));
        Assert.Equal(text, svc.TryGetReference(Yshtola(), Ev)!.Text); // refined index serves the next line
    }

    [Fact]
    public async Task GetReferenceAsync_QuickStageTooSlow_FallsBackToVoicePack()
    {
        _config.GameVoiceRankOnConversationStart = true;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var time = new FakeTimeProvider();
        var svc = await AvailableService(time);
        svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev); // the quick stage is what waits
        svc.FirstLineWait = TimeSpan.FromMilliseconds(100);
        using var block = new ManualResetEventSlim(false);
        _data.OnDecode = _ => block.Wait(TimeSpan.FromSeconds(10));

        var lookup = svc.GetReferenceAsync(Yshtola(), Ev);
        time.Advance(svc.FirstLineWait);                    // the budget runs out, the decode is still stuck

        Assert.Null(await lookup.WaitAsync(Deadline));
        block.Set();
    }

    [Fact]
    public async Task GetReferenceAsync_FirstLineAfterPluginStart_WaitsForTheHealthProbeAndTheTextIndex()
    {
        // Live 2026-11-15: the very first line after a (re)load came before /health was answered,
        // so nothing was ranked and the line spoke with the voice pack.
        _config.GameVoiceRankOnConversationStart = true;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = Create(); // no RefreshAvailabilityAsync: availability still Unknown
        svc.FirstLineWait = TimeSpan.FromSeconds(10);

        Assert.Equal(new[] { "vo_voiceman_06006_000010" }, (await svc.GetReferenceAsync(Yshtola(), Ev))?.ClipIds);
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("index")]
    [InlineData("quick")]
    public async Task GetReferenceAsync_StepFinishesBeforeThePendingCheck_StillWaitsForTheNextStep(string firstStep)
    {
        // CI 2026-11-23 (voice pack after 14 ms): a background step finished between the lookup and
        // the pending check, so nothing looked pending although the lookup had not started the next
        // step yet. The seam lets every step finish in exactly that window.
        _config.GameVoiceRankOnConversationStart = true;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = firstStep == "probe" ? Create() : await AvailableService();
        if (firstStep == "quick") svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev);
        svc.FirstLineWait = TimeSpan.FromSeconds(10);
        svc.BeforePendingCheck = () =>
            Assert.True(SpinWait.SpinUntil(() => svc.PendingWorkFor(Yshtola()) == null, TimeSpan.FromSeconds(5)));

        Assert.Equal(new[] { "vo_voiceman_06006_000010" }, (await svc.GetReferenceAsync(Yshtola(), Ev))?.ClipIds);
    }

    [Fact]
    public async Task GetReferenceAsync_QuickStage_DoesNotNeedTheOlderCandidates()
    {
        // Live 2026-11-15: decoding all 40 candidates took 4.1 s and missed the budget. The quick
        // stage decodes only the newest QuickStageCandidates; older ones are for the refine stage.
        _config.GameVoiceRankOnConversationStart = true;
        var total = GameVoiceReferenceService.QuickStageCandidates + 5;
        for (var i = 1; i <= total; i++)
            _data.AddLine($"TEXT_VOICEMAN_06006_{i * 10:000000}_YSHTOLA", 8, Words(8, "y" + i));
        using var block = new ManualResetEventSlim(false);
        var oldest = Enumerable.Range(1, 5).Select(i => $"vo_voiceman_06006_{i * 10:000000}").ToHashSet();
        _data.OnDecode = b => { if (oldest.Contains(b)) block.Wait(TimeSpan.FromSeconds(10)); };
        var svc = await AvailableService(new FakeTimeProvider()); // the budget never runs out on its own
        svc.FirstLineWait = TimeSpan.FromSeconds(10);

        // Waiting for the blocked old clips would hit the deadline: the quick stage must not need them.
        var reference = await svc.GetReferenceAsync(Yshtola(), Ev).WaitAsync(Deadline);

        Assert.NotEmpty(reference!.ClipIds);
        Assert.DoesNotContain(reference.ClipIds, id => oldest.Contains(id));
        block.Set();
    }

    [Fact]
    public async Task GetReferenceAsync_NothingQueued_DoesNotWait()
    {
        // Option off: no ranking is queued, so there is nothing to wait for (after the text index).
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService(new FakeTimeProvider()); // the budget never runs out on its own
        svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev);
        svc.FirstLineWait = TimeSpan.FromSeconds(30);

        // Any wait would hit the deadline, since the fake clock never ends the budget.
        Assert.Null(await svc.GetReferenceAsync(Yshtola(), Ev).WaitAsync(Deadline));
    }

    [Fact]
    public async Task GetReferenceAsync_SecondNewNpc_DoesNotWaitForTheFirstOnesWhisper()
    {
        _config.GameVoiceRankOnConversationStart = true;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        var asrRelease = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.Setup(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(asrRelease.Task);
        var svc = await AvailableService();
        svc.FirstLineWait = TimeSpan.FromSeconds(10);
        var thancred = new GameVoiceSpeaker(11, "Thancred", 0, ClientLanguage.English);

        // Y'shtola's Whisper stage now parks
        Assert.Equal(new[] { "vo_voiceman_06006_000010" }, (await svc.GetReferenceAsync(Yshtola(), Ev))?.ClipIds);
        // separate gate: Thancred is not stuck behind it
        Assert.Equal(new[] { "vo_voiceman_06006_000020" }, (await svc.GetReferenceAsync(thancred, Ev))?.ClipIds);

        asrRelease.SetResult(null);
    }

    [Fact]
    public async Task RankAsync_Batch_DoesNotTouchTheLiveMemory()
    {
        // No line waits → no provisional reference: the batch must not evict the current conversation's NPCs.
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev);
        var decodes = _data.DecodeCount;

        await svc.RankAsync(Yshtola(), Ev);

        Assert.Equal(decodes + 2, _data.DecodeCount); // pre-score + Whisper re-decode, no provisional decode
    }

    [Fact]
    public async Task TryGetReference_NoIndex_DoesNotRankWhenOptionOff()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev);

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
        // A queued ranking registers its quick stage before it starts: nothing pending = nothing queued.
        Assert.Null(svc.PendingWorkFor(Yshtola()));
        _db.Verify(d => d.ReplaceGameVoiceIndex(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<GameVoiceIndexEntity>>()), Times.Never);
    }

    // ── Batch (§11.2) ───────────────────────────────────────────────────

    [Fact]
    public async Task BuildIndex_RanksEveryVoicedToken_MetOrNot()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        _data.AddLine("TEXT_VOICEMAN_06006_000030_NOBODYKNOWS", 8, Words(8, "n"));
        var svc = await AvailableService();
        var progress = new List<GameVoiceBatchProgress>();

        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), new SyncProgress<GameVoiceBatchProgress>(progress.Add));

        Assert.Equal(3, result.Tokens);
        Assert.Equal(3, result.Ranked);
        Assert.Equal(3, result.WithReference);
        Assert.Equal((3, 3), (progress.Last().Done, progress.Last().Total));
        Assert.False(svc.IsIndexBuildRunning);
        _db.Verify(d => d.GetNpcs(), Times.Never); // not driven by the DB's NPCs any more
    }

    [Fact]
    public async Task BuildIndex_IsResumable_SkipsCurrentTokens()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);

        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null);

        Assert.Equal(1, result.Ranked);
        Assert.Equal(1, result.UpToDate);
        Assert.Equal(0, await svc.CountOutdatedTokensAsync(ClientLanguage.English));
    }

    [Fact]
    public async Task BuildIndex_FastWithoutWhisper_LaterWhisperRunRanksAgain()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();

        await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(WithAsr: false), null);
        _client.Verify(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(svc.TryGetReference(Yshtola(), Ev)); // the fast index is usable live

        var again = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(WithAsr: true), null);
        Assert.Equal(1, again.Ranked);
    }

    [Fact]
    public async Task BuildIndex_Cancelled_ReportsItAndStops()
    {
        for (var i = 1; i <= 5; i++) _data.AddLine($"TEXT_VOICEMAN_06006_0000{i}0_SPEAKER{i}", 8, Words(8, "s" + i));
        var svc = await AvailableService();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress<GameVoiceBatchProgress>(p => { if (p.Done == 2) cts.Cancel(); });

        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), progress, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Ranked);
    }

    [Fact]
    public async Task BuildIndex_ServerUnsupported_DoesNothing()
    {
        _health = new EchokrauTtsHealthResponse { status = "ok", inline_reference = false };
        var svc = Create();
        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null);
        Assert.NotNull(result.NotRunReason);
        _db.Verify(d => d.ReplaceGameVoiceIndex(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<GameVoiceIndexEntity>>()), Times.Never);
    }

    [Fact]
    public async Task BuildIndex_WritesTheMatchingReport()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCREDX", 8, Words(8, "t"));   // no NPC of that name
        var svc = await AvailableService();
        svc.TryGetReference(new GameVoiceSpeaker(0, "Tataru", 0, ClientLanguage.English), Ev); // met, no token
        svc.GetOrBuildIndex(ClientLanguage.English, CancellationToken.None, Ev);
        svc.TryGetReference(new GameVoiceSpeaker(0, "Tataru", 0, ClientLanguage.English), Ev);

        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(), null);

        Assert.Equal(1, result.Unmatched);
        var report = JsonSerializer.Deserialize<GameVoiceUnmatchedReport>(File.ReadAllText(result.ReportPath!))!;
        var entry = Assert.Single(report.Unmatched);
        Assert.Equal("thancredx", entry.Token);
        Assert.Equal(1, entry.Clips);
        Assert.Equal("Thancred", entry.Suggestions.First().NpcName);
        Assert.Contains("Tataru", report.SeenWithoutToken);
        Assert.StartsWith(Path.Combine(_configDir, "harvest"), result.ReportPath);
    }

    [Fact]
    public async Task BuildIndex_DetectVoiceChanges_WritesSuggestionsInTheSplitFormat()
    {
        _health = new EchokrauTtsHealthResponse
        {
            status = "ok", inline_reference = true, asr_check = true, speaker_embedding = true, speaker_encoder = "xtts-v2-speaker",
        };
        // Four patches of Y'shtola: two with one voice, two with another; Thancred steady.
        var patches = new[] { "05000", "05100", "06100", "06200" };
        foreach (var p in patches)
            for (var i = 1; i <= 2; i++)
            {
                _data.AddLine($"TEXT_VOICEMAN_{p}_0000{i}0_YSHTOLA", 8, Words(8, "y" + p + i));
                _data.AddLine($"TEXT_VOICEMAN_{p}_0001{i}0_THANCRED", 8, Words(8, "t" + p + i));
            }
        _client.Setup(c => c.SpeakerEmbeddingAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EKEventId _, short[] pcm, CancellationToken _) =>
            {
                var b = _data.AudioBaseForPcm(pcm)!;
                var newVoice = b.Contains("_061") || b.Contains("_062");
                var isYshtola = b.EndsWith("_000010") || b.EndsWith("_000020");
                float[] v = isYshtola
                    ? (newVoice ? new[] { 0f, 1f, 0f, 0.1f } : new[] { 1f, 0f, 0f, 0.1f })
                    : new[] { 0f, 0f, 1f, 0.1f };
                return new SpeakerEmbedding(v, "xtts-v2-speaker");
            });
        var svc = await AvailableService();

        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(DetectVoiceChanges: true), null);

        Assert.Equal(1, result.Suggestions);
        var file = JsonSerializer.Deserialize<VoiceActorSplitsFile>(File.ReadAllText(result.SuggestionsPath!),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var split = Assert.Single(file.Splits);
        Assert.Equal("Y'shtola", split.NpcName);
        Assert.Equal(new[] { "06100" }, split.BoundaryPatches);
        Assert.Contains("listen before committing", split.Comment);
        // Suggestions are never applied: the index still has one epoch.
        Assert.All(Stored(Ysh), r => Assert.Equal("", r.Epoch));
    }

    [Fact]
    public async Task BuildIndex_DetectVoiceChanges_WithoutEncoder_IsSkipped()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();

        var result = await svc.BuildIndexAsync(ClientLanguage.English, new GameVoiceBatchOptions(DetectVoiceChanges: true), null);

        Assert.Null(result.SuggestionsPath);
        _client.Verify(c => c.SpeakerEmbeddingAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── In-game self-test ────────────────────────────────────────────────────

    [Fact]
    public async Task SelfCheck_Disabled_ReadsNoGameAudio()
    {
        _config.GameVoiceReferenceEnabled = false;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));

        var check = await Create().SelfCheckAsync(ClientLanguage.English);

        Assert.Equal(new GameVoiceSelfCheck(0, 0, null, 0), check);
        Assert.Equal(0, _data.DecodeCount);
    }

    [Fact]
    public async Task SelfCheck_DecodesTheNewestClipThatExists()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_07000_000010_THANCRED", 5, Words(8, "t"), decodable: false);

        var check = await Create().SelfCheckAsync(ClientLanguage.English);

        Assert.Equal(2, check.Lines);
        Assert.Equal(2, check.Tokens);
        Assert.NotNull(check.DecodedClip);
        Assert.Contains("06006", check.DecodedClip);
        Assert.Equal(8, check.DecodedSeconds, 1);
    }

    [Fact]
    public async Task SelfCheck_NothingDecodes_ReportsNoClip()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"), decodable: false);

        var check = await Create().SelfCheckAsync(ClientLanguage.English);

        Assert.Equal(1, check.Lines);
        Assert.Null(check.DecodedClip);
    }

    [Fact]
    public async Task SampleVoicedNpcName_Disabled_ReturnsNull()
    {
        _config.GameVoiceReferenceEnabled = false;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));

        Assert.Empty(await Create().SampleVoicedNpcsAsync(ClientLanguage.English, 1));
        Assert.Equal(0, _data.DecodeCount);
    }

    [Fact]
    public async Task SampleVoicedNpcName_PicksTheNpcWithTheMostLines_WithoutDecoding()
    {
        // Audio bases must differ: the key's speaker is not part of it, a repeated number is dropped.
        _data.AddLine("TEXT_VOICEMAN_06006_000010_GRAHA", 5, Words(8, "g"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        _data.AddLine("TEXT_VOICEMAN_06006_000030_THANCRED", 8, Words(8, "u"));

        Assert.Equal(new GameVoiceSampleNpc("Thancred", "thancred", "thancred", 2),
            Assert.Single(await Create().SampleVoicedNpcsAsync(ClientLanguage.English, 1)));
        Assert.Equal(0, _data.DecodeCount);
    }

    [Fact]
    public async Task SampleVoicedNpcName_SkipsAnNpcWithAVoiceActorChange()
    {
        // Two epochs: which one speaks depends on the story context.
        WriteSplits(Splits6100);
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06100_000010_YSHTOLA", 8, Words(8, "z"));
        _data.AddLine("TEXT_VOICEMAN_06100_000020_YSHTOLA", 8, Words(8, "x"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));

        Assert.Equal("Thancred", Assert.Single(await Create().SampleVoicedNpcsAsync(ClientLanguage.English, 5)).Name);
    }

    [Fact]
    public async Task SampleVoicedNpcName_TokenWithoutOwner_ReturnsNull()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_NOBODYKNOWN", 8, Words(8, "n"));

        Assert.Empty(await Create().SampleVoicedNpcsAsync(ClientLanguage.English, 1));
    }

    [Fact]
    public async Task SampleVoicedNpcs_Count_GivesSeveralNpcs_MostLinesFirst()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_GRAHA", 5, Words(8, "g"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        _data.AddLine("TEXT_VOICEMAN_06006_000030_THANCRED", 8, Words(8, "u"));
        _data.AddLine("TEXT_VOICEMAN_06006_000040_YSHTOLA", 8, Words(8, "y"));

        var npcs = await Create().SampleVoicedNpcsAsync(ClientLanguage.English, 2);

        Assert.Equal(new[] { "Thancred", "G'raha Tia" }, npcs.Select(n => n.Name));
    }

    [Fact]
    public async Task SampleVoicedNpcs_UnindexedOnly_SkipsATokenWithAnIndexRow()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        _data.AddLine("TEXT_VOICEMAN_06006_000030_THANCRED", 8, Words(8, "u"));
        _data.AddLine("TEXT_VOICEMAN_06006_000040_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(new GameVoiceSpeaker(0, "Thancred", 0, ClientLanguage.English), Ev);

        var npcs = await svc.SampleVoicedNpcsAsync(ClientLanguage.English, 2, unindexedOnly: true);

        Assert.Equal("Y'shtola", Assert.Single(npcs).Name);
    }

    [Fact]
    public async Task Rank_RaisesPairRanked_WithTokenAndStoredClips()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        var timings = new List<GameVoiceRankTiming>();
        svc.PairRanked += timings.Add;

        await svc.RankAsync(Yshtola(), Ev);

        var t = Assert.Single(timings);
        Assert.Equal(("yshtola", 1, true), (t.Token, t.Stored, t.WithAsr));
        Assert.True(t.TotalMs >= t.QuickMs);
    }

    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    /// <summary>Synthetic game data. Each clip is a sine whose first sample encodes its id.</summary>
    private sealed class FakeData : IGameVoiceDataSource
    {
        private readonly Dictionary<ClientLanguage, List<CutsceneTextRow>> _rows = new();
        private readonly Dictionary<string, (double Seconds, string Text)> _clips = new();
        private readonly List<string> _order = new();

        public Dictionary<uint, string> EnglishNames { get; } = new();
        public Dictionary<ClientLanguage, Dictionary<uint, string>> ClientNames { get; } = new();
        public HashSet<string> Undecodable { get; } = new();
        public int DecodeCount { get; private set; }

        public void AddLine(string key, double seconds, string text, ClientLanguage lang = ClientLanguage.English, bool decodable = true)
        {
            if (!_rows.TryGetValue(lang, out var list)) _rows[lang] = list = new List<CutsceneTextRow>();
            list.Add(new CutsceneTextRow(key, text, false));
            VoiceExtractKeyAudioBase(key, out var audioBase);
            _clips[audioBase] = (seconds, text);
            _order.Add(audioBase);
            if (!decodable) Undecodable.Add(audioBase);
        }

        private static void VoiceExtractKeyAudioBase(string key, out string audioBase) =>
            Echokraut.Helper.Functional.VoiceExtractKey.TryParse(key, out _, out audioBase);

        public IEnumerable<CutsceneTextRow> EnumerateCutsceneRows(ClientLanguage language, CancellationToken ct) =>
            _rows.TryGetValue(language, out var list) ? list : Enumerable.Empty<CutsceneTextRow>();

        public IReadOnlyDictionary<uint, string> GetNpcNames(ClientLanguage language) =>
            language == ClientLanguage.English ? EnglishNames
            : ClientNames.TryGetValue(language, out var names) ? names : new Dictionary<uint, string>();

        /// <summary>Runs before every decode with the audio base (tests block ranking stages with it).</summary>
        public Action<string>? OnDecode { get; set; }

        public PcmBuffer? TryDecodeVoice(string audioBase, ClientLanguage language)
        {
            OnDecode?.Invoke(audioBase);
            DecodeCount++;
            if (Undecodable.Contains(audioBase) || !_clips.TryGetValue(audioBase, out var clip)) return null;
            var pcm = GameVoiceRankingTests.Sine(0.3, clip.Seconds, 180 + 10 * _order.IndexOf(audioBase));
            pcm.Samples[0] = (short)(1000 + _order.IndexOf(audioBase)); // id marker
            return pcm;
        }

        public string? AudioBaseForPcm(short[] pcm)
        {
            var idx = pcm[0] - 1000;
            return idx >= 0 && idx < _order.Count ? _order[idx] : null;
        }

        public string TextForPcm(short[] pcm) =>
            AudioBaseForPcm(pcm) is { } b ? _clips[b].Text : "";
    }
}
