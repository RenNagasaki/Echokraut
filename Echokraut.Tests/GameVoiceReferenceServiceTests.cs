using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// <see cref="GameVoiceReferenceService"/> against a fake game-data source (synthetic PCM, never
/// real game audio), a mocked EchokrauTTS client and an in-memory index store.
/// </summary>
public sealed class GameVoiceReferenceServiceTests : IDisposable
{
    private const int YshtolaId = 10;
    private static readonly EKEventId Ev = new(0, TextSource.None);

    private readonly Configuration _config = new()
    {
        BackendSelection = TTSBackends.EchokrauTTS,
        GameVoiceReferenceEnabled = true,
        GameVoiceRankOnConversationStart = false,
    };

    private readonly FakeData _data = new();
    private readonly Mock<IEchokrauTtsReferenceClient> _client = new();
    private readonly Mock<IDatabaseService> _db = new();
    private readonly Dictionary<(int, int), List<GameVoiceReferenceEntity>> _store = new();
    private readonly string _configDir = Path.Combine(Path.GetTempPath(), "ek-gvr-" + Guid.NewGuid().ToString("N"));
    private readonly List<GameVoiceReferenceService> _created = new();
    private EchokrauTtsHealthResponse _health = new() { status = "ok", inline_reference = true, asr_check = true };

    public GameVoiceReferenceServiceTests()
    {
        Directory.CreateDirectory(_configDir);
        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _health);
        // Default ASR: hears exactly the game text of the clip (WER 0).
        _client.Setup(c => c.AsrCheckAsync(It.IsAny<EKEventId>(), It.IsAny<short[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EKEventId _, short[] pcm, string? _, CancellationToken _) => _data.TextForPcm(pcm));

        _db.Setup(d => d.GetGameVoiceReferences(It.IsAny<int>(), It.IsAny<int>()))
            .Returns((int c, int l) => _store.TryGetValue((c, l), out var rows) ? rows.ToList() : new List<GameVoiceReferenceEntity>());
        _db.Setup(d => d.ReplaceGameVoiceReferences(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<GameVoiceReferenceEntity>>()))
            .Callback((int c, int l, IReadOnlyList<GameVoiceReferenceEntity> rows) => _store[(c, l)] = rows.ToList());
        _db.Setup(d => d.GetInstancesForCharacter(It.IsAny<int>())).Returns(new List<CharacterInstanceEntity>());

        _data.EnglishNames[1] = "Y'shtola";
        _data.EnglishNames[2] = "Thancred";
        _data.EnglishNames[3] = "G'raha Tia";
    }

    public void Dispose()
    {
        foreach (var s in _created) s.Dispose();
        try { Directory.Delete(_configDir, true); } catch { /* best effort */ }
    }

    private GameVoiceReferenceService Create()
    {
        var svc = new GameVoiceReferenceService(_config, new Mock<ILogService>().Object, _db.Object, _data, _client.Object, _configDir);
        _created.Add(svc);
        return svc;
    }

    private static GameVoiceSpeaker Yshtola(ClientLanguage lang = ClientLanguage.English) =>
        new(YshtolaId, "Y'shtola", 0, lang);

    /// <summary>Text with the English median rate for <paramref name="seconds"/> (tempo score ≈ 1).</summary>
    private static string Words(double seconds, string seed) =>
        string.Join(" ", Enumerable.Range(0, (int)Math.Round(seconds * 2.6)).Select(i => seed + i));

    private async Task<GameVoiceReferenceService> AvailableService()
    {
        var svc = Create();
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
        _db.Verify(d => d.GetGameVoiceReferences(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
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
    public async Task UnreachableServer_IsUnsupported()
    {
        _client.Setup(c => c.GetHealthAsync(It.IsAny<EKEventId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EchokrauTtsHealthResponse?)null);
        Assert.Equal(GameVoiceAvailability.ServerUnsupported, await Create().RefreshAvailabilityAsync(Ev));
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
    public async Task Rank_StoresOnlyClipsOfTheSameNpc()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _data.AddLine("TEXT_VOICEMAN_06006_000020_THANCRED", 8, Words(8, "t"));
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(Yshtola(), Ev));

        var rows = _store[(YshtolaId, (int)ClientLanguage.English)];
        Assert.Equal("vo_voiceman_06006_000010", Assert.Single(rows).AudioBase);
        Assert.Equal(GameVoiceRanking.IndexVersion, rows[0].IndexVersion);
        Assert.Equal(1, rows[0].Rank);
    }

    [Fact]
    public async Task Rank_UsesAliasList()
    {
        // MYSTERYVOICE matches no NPC name; the embedded GameVoiceAliases.json maps it to G'raha Tia.
        _data.AddLine("TEXT_VOICEMAN_04004_000010_MYSTERYVOICE", 8, Words(8, "g"));
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(new GameVoiceSpeaker(20, "G'raha Tia", 0, ClientLanguage.English), Ev));
        Assert.Equal(0, await svc.RankAsync(Yshtola(), Ev));
    }

    [Fact]
    public async Task Rank_NonEnglishClient_ResolvesNameThroughClientLanguageSheet()
    {
        _data.ClientNames[ClientLanguage.German] = new Dictionary<uint, string> { [1] = "Y'shtola" };
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"), ClientLanguage.German);
        var svc = await AvailableService();

        Assert.Equal(1, await svc.RankAsync(Yshtola(ClientLanguage.German), Ev));
        Assert.True(_store.ContainsKey((YshtolaId, (int)ClientLanguage.German)));
    }

    [Fact]
    public async Task Rank_DropsShortNonSpeechAndUndecodableClips()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 2, Words(2, "a"));          // too short
        _data.AddLine("TEXT_VOICEMAN_06006_000020_YSHTOLA", 8, "Hahahaha!");             // not speech
        _data.AddLine("TEXT_VOICEMAN_06006_000030_YSHTOLA", 8, Words(8, "c"), decodable: false);
        var svc = await AvailableService();

        Assert.Equal(0, await svc.RankAsync(Yshtola(), Ev));
        Assert.Empty(_store[(YshtolaId, (int)ClientLanguage.English)]);
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

        var rows = _store[(YshtolaId, (int)ClientLanguage.English)];
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
    }

    [Fact]
    public async Task Rank_VoiceActorSplit_UsesNewestEpochOnly()
    {
        File.WriteAllText(Path.Combine(_configDir, GameVoiceReferenceService.SplitsOverrideFile),
            "{ \"version\": 2, \"splits\": [ { \"npcName\": \"Y'shtola\", \"language\": \"EN\", \"boundaryPatches\": [\"06010\"] } ] }");
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "old"));
        _data.AddLine("TEXT_VOICEMAN_06010_000010_YSHTOLA", 8, Words(8, "new"));
        var svc = await AvailableService();

        await svc.RankAsync(Yshtola(), Ev);

        var row = Assert.Single(_store[(YshtolaId, (int)ClientLanguage.English)]);
        Assert.Equal("vo_voiceman_06010_000010", row.AudioBase);
        Assert.Equal("Post06010", row.Epoch);
    }

    // ── Live path (§2.1) ────────────────────────────────────────────────

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

    [Fact]
    public async Task TryGetReference_ClipPatchedAway_DropsIndex()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();
        await svc.RankAsync(Yshtola(), Ev);

        _data.Undecodable.Add("vo_voiceman_06006_000010");

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
        Assert.Empty(_store[(YshtolaId, (int)ClientLanguage.English)]);
    }

    [Fact]
    public async Task TryGetReference_OlderIndexVersion_IsNotUsed()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _store[(YshtolaId, (int)ClientLanguage.English)] = new List<GameVoiceReferenceEntity>
        {
            new() { CharacterId = YshtolaId, AudioBase = "vo_voiceman_06006_000010", Rank = 1, IndexVersion = GameVoiceRanking.IndexVersion - 1 },
        };
        var svc = await AvailableService();

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
    }

    [Fact]
    public async Task TryGetReference_NoIndex_RanksInBackgroundWhenEnabled()
    {
        _config.GameVoiceRankOnConversationStart = true;
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();

        Assert.Null(svc.TryGetReference(Yshtola(), Ev)); // this line: voice pack

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!_store.ContainsKey((YshtolaId, (int)ClientLanguage.English)) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Single(_store[(YshtolaId, (int)ClientLanguage.English)]);
    }

    [Fact]
    public async Task TryGetReference_NoIndex_DoesNotRankWhenOptionOff()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        var svc = await AvailableService();

        Assert.Null(svc.TryGetReference(Yshtola(), Ev));
        await Task.Delay(100);
        _db.Verify(d => d.ReplaceGameVoiceReferences(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<GameVoiceReferenceEntity>>()), Times.Never);
    }

    [Fact]
    public async Task TryGetReference_CharacterWithoutDbRow_IsNull()
    {
        var svc = await AvailableService();
        Assert.Null(svc.TryGetReference(new GameVoiceSpeaker(0, "Y'shtola", 0, ClientLanguage.English), Ev));
    }

    // ── Batch (F5) ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuildIndex_RanksNpcsWithoutCurrentIndex()
    {
        _data.AddLine("TEXT_VOICEMAN_06006_000010_YSHTOLA", 8, Words(8, "y"));
        _db.Setup(d => d.GetNpcs()).Returns(new List<CharacterEntity>
        {
            new() { Id = YshtolaId, Name = "Y'shtola", Language = (int)ClientLanguage.English },
            new() { Id = 11, Name = "Nobody", Language = (int)ClientLanguage.English },
            new() { Id = 12, Name = "Y'shtola", Language = (int)ClientLanguage.German },
        });
        var svc = await AvailableService();
        var progress = new List<(int, int)>();

        var indexed = await svc.BuildIndexAsync(ClientLanguage.English, new SyncProgress(progress.Add));

        Assert.Equal(1, indexed);
        Assert.Equal((2, 2), progress.Last());
        Assert.False(svc.IsIndexBuildRunning);
    }

    [Fact]
    public async Task BuildIndex_ServerUnsupported_DoesNothing()
    {
        _health = new EchokrauTtsHealthResponse { status = "ok", inline_reference = false };
        var svc = Create();
        Assert.Equal(0, await svc.BuildIndexAsync(ClientLanguage.English, null));
        _db.Verify(d => d.GetNpcs(), Times.Never);
    }

    private sealed class SyncProgress(Action<(int Done, int Total)> onReport) : IProgress<(int Done, int Total)>
    {
        public void Report((int Done, int Total) value) => onReport(value);
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

        public PcmBuffer? TryDecodeVoice(string audioBase, ClientLanguage language)
        {
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
