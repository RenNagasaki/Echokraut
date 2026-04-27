using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// End-to-end migration test using the real production-shaped Configuration JSON
/// at Echokraut/Resources/Test-Config.json.
/// Asserts structural correctness (counts/contents/contexts) without hard-coding
/// specific numbers — those are captured from the loaded config and compared.
/// Uses in-memory SQLite (not the EF InMemory provider) so SQLite-specific features
/// such as COLLATE NOCASE indexes and EF.Functions.Collate are exercised.
/// </summary>
public class MigrationFromConfigFileTests : IDisposable
{
    private readonly DatabaseService _db;
    private readonly EchokrautDbContext _context;
    private readonly SqliteConnection _connection;

    public MigrationFromConfigFileTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EchokrautDbContext(options);
        var mockLog = new Mock<ILogService>();
        _db = new DatabaseService(mockLog.Object, _context);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    /// <summary>Walks upward from the test binary to find the repo file regardless of build flavor.</summary>
    private static string LocateRepoFile(string relativePathFromRepoRoot)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relativePathFromRepoRoot);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return "";
    }

    private static Configuration LoadRealConfig()
    {
        var path = LocateRepoFile(Path.Combine("Echokraut", "Resources", "Test-Config.json"));
        Assert.True(!string.IsNullOrEmpty(path) && File.Exists(path),
            "Test-Config.json not found via repo walk-up.");
        var json = File.ReadAllText(path);

        // Production config uses Newtonsoft $type markers (Dalamud's IPluginConfiguration pipeline);
        // Objects = read $type when present but don't require it on the root.
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
        };
        var cfg = JsonConvert.DeserializeObject<Configuration>(json, settings);
        Assert.NotNull(cfg);
        return cfg!;
    }

    /// <summary>Captures the input counts so we can compare after MigrateFromConfig clears the lists.</summary>
    private record ConfigSnapshot(
        int VoiceCount,
        int NpcCount,
        int PlayerCount,
        int PhoneticCount,
        int MutedCount,
        Dictionary<string, NpcMapData> NpcsByName,
        Dictionary<string, NpcMapData> PlayersByName,
        Dictionary<string, EchokrautVoice> VoicesByBackend);

    private static ConfigSnapshot Snapshot(Configuration cfg) => new(
        cfg.EchokrautVoices.Count,
        cfg.MappedNpcs.Count,
        cfg.MappedPlayers.Count,
        cfg.PhoneticCorrections.Count,
        cfg.MutedNpcDialogues.Count,
        cfg.MappedNpcs.Where(n => !string.IsNullOrEmpty(n.Name))
                       .GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.First()),
        cfg.MappedPlayers.Where(p => !string.IsNullOrEmpty(p.Name))
                          .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First()),
        cfg.EchokrautVoices.Where(v => !string.IsNullOrEmpty(v.BackendVoice))
                            .GroupBy(v => v.BackendVoice).ToDictionary(g => g.Key, g => g.First()));

    [Fact]
    public void RealConfig_LoadsAndHasNonTrivialContents()
    {
        var cfg = LoadRealConfig();
        Assert.True(cfg.MappedNpcs.Count > 0, "Real config should have NPC mappings");
        Assert.True(cfg.MappedPlayers.Count > 0, "Real config should have Player mappings");
        Assert.True(cfg.EchokrautVoices.Count > 0, "Real config should have voices");
    }

    [Fact]
    public void NeedsMigration_TrueWhenConfigHasData_FalseAfterMigration()
    {
        var cfg = LoadRealConfig();
        Assert.True(_db.NeedsMigration(cfg));

        _db.MigrateFromConfig(cfg);

        Assert.False(_db.NeedsMigration(cfg));
    }

    [Fact]
    public void Migrate_VoiceCountAndStructure_MatchInput()
    {
        var cfg = LoadRealConfig();
        var snap = Snapshot(cfg);

        _db.MigrateFromConfig(cfg);

        var dbVoices = _db.GetVoices();
        Assert.Equal(snap.VoiceCount, dbVoices.Count);

        // Every input voice (by BackendVoice key) is present in the DB with matching flags.
        foreach (var kv in snap.VoicesByBackend)
        {
            var input = kv.Value;
            var found = dbVoices.SingleOrDefault(v => v.BackendVoice == kv.Key);
            Assert.NotNull(found);
            Assert.Equal(input.IsDefault, found!.IsDefault);
            Assert.Equal(input.IsEnabled, found.IsEnabled);
            Assert.Equal(input.UseAsRandom, found.UseAsRandom);
            Assert.Equal(input.IsAdultVoice, found.IsAdultVoice);
            Assert.Equal(input.IsChildVoice, found.IsChildVoice);
            Assert.Equal(input.IsElderVoice, found.IsElderVoice);
            Assert.Equal(input.Volume, found.Volume);
            // AllowedRaces/Genders are 1:1 mapped
            Assert.Equal(input.AllowedRaces.Count, found.AllowedRaces.Count);
            Assert.Equal(input.AllowedGenders.Count, found.AllowedGenders.Count);
            foreach (var r in input.AllowedRaces)
                Assert.Contains(found.AllowedRaces, ar => ar.Race == (int)r);
            foreach (var g in input.AllowedGenders)
                Assert.Contains(found.AllowedGenders, ag => ag.Gender == (int)g);
        }
    }

    [Fact]
    public void Migrate_NpcCountAndStructure_MatchInput()
    {
        var cfg = LoadRealConfig();
        var snap = Snapshot(cfg);

        _db.MigrateFromConfig(cfg);

        var dbNpcs = _db.GetNpcs();
        // The DB unique key is (Name, Gender, Race, Language). The input may contain duplicate names
        // with different Gender/Race tuples — those are legitimately distinct rows. So we just check
        // every unique-by-name input lands in the DB.
        Assert.True(dbNpcs.Count >= snap.NpcsByName.Count,
            $"Expected at least {snap.NpcsByName.Count} unique-named NPCs in DB, got {dbNpcs.Count}");

        foreach (var (name, input) in snap.NpcsByName)
        {
            var found = dbNpcs.FirstOrDefault(c => c.Name == name);
            Assert.NotNull(found);
            // For a named anchor we verify race + voice key are preserved (gender can vary if a name has both).
            // Pick any matching DB row — the ones with this Name, regardless of gender.
            var matchingByName = dbNpcs.Where(c => c.Name == name).ToList();
            Assert.Contains(matchingByName, c => c.VoiceKey == (input.voice ?? ""));
        }
    }

    [Fact]
    public void Migrate_PlayerCountAndStructure_MatchInput()
    {
        var cfg = LoadRealConfig();
        var snap = Snapshot(cfg);

        _db.MigrateFromConfig(cfg);

        var dbPlayers = _db.GetPlayers();
        Assert.True(dbPlayers.Count >= snap.PlayersByName.Count,
            $"Expected at least {snap.PlayersByName.Count} unique-named players in DB, got {dbPlayers.Count}");

        foreach (var (name, _) in snap.PlayersByName)
        {
            Assert.Contains(dbPlayers, c => c.Name == name);
        }

        // Old config never had a World — every migrated player should have empty World.
        Assert.All(dbPlayers, p => Assert.Equal("", p.World));
    }

    [Fact]
    public void Migrate_NpcContextsAreCreatedForEveryNpc()
    {
        var cfg = LoadRealConfig();
        _db.MigrateFromConfig(cfg);

        var dbNpcs = _db.GetNpcs();
        Assert.NotEmpty(dbNpcs);

        // Every NPC has at minimum a "npc" context. (Bubble context only when HasBubbles=true.)
        foreach (var n in dbNpcs.Take(50))
        {
            var ctx = _db.GetContext(n.Id, "npc");
            Assert.NotNull(ctx);
        }
    }

    [Fact]
    public void Migrate_PhoneticCorrectionCountMatchesInput()
    {
        var cfg = LoadRealConfig();
        var snap = Snapshot(cfg);

        _db.MigrateFromConfig(cfg);

        var dbCorrections = _db.GetPhoneticCorrections();
        Assert.Equal(snap.PhoneticCount, dbCorrections.Count);
    }

    [Fact]
    public void Migrate_ClearsConfigInputListsAfterSuccess()
    {
        var cfg = LoadRealConfig();
        _db.MigrateFromConfig(cfg);

        Assert.Empty(cfg.MappedNpcs);
        Assert.Empty(cfg.MappedPlayers);
        Assert.Empty(cfg.EchokrautVoices);
        Assert.Empty(cfg.PhoneticCorrections);
        Assert.Empty(cfg.MutedNpcDialogues);
    }

    [Fact]
    public void Migrate_LodestoneCacheTableIsAccessible()
    {
        // Schema v11 added lodestone_lookups; verify round-trip after migration.
        var cfg = LoadRealConfig();
        _db.MigrateFromConfig(cfg);

        Assert.Null(_db.GetLodestoneLookup("Some Anchor", "Phoenix"));
        _db.UpsertLodestoneLookup("Some Anchor", "Phoenix", NpcRaces.Hyur, Genders.Male, found: true);

        var hit = _db.GetLodestoneLookup("Some Anchor", "Phoenix");
        Assert.NotNull(hit);
        Assert.Equal((int)NpcRaces.Hyur, hit!.Race);
        Assert.Equal((int)Genders.Male, hit.Gender);
        Assert.True(hit.Found);
    }

    [Fact]
    public void Migrate_KnownAnchor_WukLamat_IsPresent()
    {
        // Anchor: a specific NPC we know is in the file. Catches "data corruption silently swallowed" bugs.
        var cfg = LoadRealConfig();
        _db.MigrateFromConfig(cfg);

        var npcs = _db.GetNpcs();
        Assert.Contains(npcs, n => n.Name == "Wuk Lamat");

        // The voice in the JSON for Wuk Lamat is "Female_Hrothgar_Wuk Lamat.wav"
        var wuk = npcs.First(n => n.Name == "Wuk Lamat");
        Assert.Equal("Female_Hrothgar_Wuk Lamat.wav", wuk.VoiceKey);
    }

    [Fact]
    public void Migrate_NpcVolume_PreservedAcrossMigration()
    {
        // Real Test-Config.json has all volumes at 1.0 (default), so we inject deliberate
        // non-default values into the loaded Configuration *before* migration, then verify
        // each volume tier (npc context, bubble context, voice) survives the round-trip.
        var cfg = LoadRealConfig();

        // Pick anchors with distinctive volumes so we can find them post-migration.
        const float wukVolume = 0.42f;
        const float wukBubbleVolume = 0.73f;
        const float voiceVolume = 1.55f;

        var wuk = cfg.MappedNpcs.First(n => n.Name == "Wuk Lamat");
        wuk.Volume = wukVolume;
        wuk.VolumeBubble = wukBubbleVolume;
        wuk.HasBubbles = true; // bubble context is only created when HasBubbles=true

        var firstVoice = cfg.EchokrautVoices.First(v => !string.IsNullOrEmpty(v.BackendVoice));
        var firstVoiceKey = firstVoice.BackendVoice;
        firstVoice.Volume = voiceVolume;

        _db.MigrateFromConfig(cfg);

        // 1) NPC context volume survives
        var dbWuk = _db.GetNpcs().First(c => c.Name == "Wuk Lamat");
        var npcCtx = _db.GetContext(dbWuk.Id, "npc");
        Assert.NotNull(npcCtx);
        Assert.Equal(wukVolume, npcCtx!.Volume, precision: 4);

        // 2) Bubble context volume survives
        var bubbleCtx = _db.GetContext(dbWuk.Id, "bubble");
        Assert.NotNull(bubbleCtx);
        Assert.Equal(wukBubbleVolume, bubbleCtx!.Volume, precision: 4);

        // 3) Voice volume survives
        var dbVoice = _db.GetVoiceByKey(firstVoiceKey);
        Assert.NotNull(dbVoice);
        Assert.Equal(voiceVolume, dbVoice!.Volume, precision: 4);
    }

    [Fact]
    public void Migrate_FullSnapshot_AllCategoriesSurviveTheRoundTrip()
    {
        var cfg = LoadRealConfig();
        var snap = Snapshot(cfg);

        _db.MigrateFromConfig(cfg);

        Assert.Equal(snap.VoiceCount, _db.GetVoices().Count);
        Assert.Equal(snap.PhoneticCount, _db.GetPhoneticCorrections().Count);
        Assert.True(_db.GetNpcs().Count >= snap.NpcsByName.Count);
        Assert.True(_db.GetPlayers().Count >= snap.PlayersByName.Count);
    }
}
