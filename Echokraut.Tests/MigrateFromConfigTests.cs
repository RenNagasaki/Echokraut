using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.Game;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// Exercises the JSON-config → SQLite migration (<see cref="DatabaseService.MigrateFromConfig"/>)
/// against a curated fixture (<c>Resources/Test-Config-WithMappings.json</c>) drawn from a real
/// pre-DB user config. The fixture covers the migration's interesting paths:
/// case-only-different names, IsChild + child voices, empty-name entries (Amon),
/// players (ObjectKind=1), muted dialogue base IDs, voices with various
/// IsDefault / IsChildVoice / IsEnabled / AllowedRaces shapes, phonetic corrections.
///
/// Loaded via Newtonsoft.Json with TypeNameHandling.Auto — the same serializer Dalamud
/// uses for <c>SavePluginConfig</c>, so the test exercises the exact deserialization
/// path users hit on plugin start (NpcMapData's non-default constructor included).
/// </summary>
public class MigrateFromConfigTests : System.IDisposable
{
    private const string FixtureResourceName = "Echokraut.Tests.Resources.Test-Config-WithMappings.json";

    private readonly SqliteConnection _connection;
    private readonly EchokrautDbContext _context;
    private readonly DatabaseService _db;
    private readonly Configuration _config;

    public MigrateFromConfigTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<EchokrautDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new EchokrautDbContext(options);

        // Migration uses _clientLanguage for the per-row Language stamp. The fixture is a
        // German player's config (DE NPC names like "Schnäppchenjägerin"), but the
        // migration stamps them with the *current* session language. Use German here so
        // assertions read naturally.
        _db = new DatabaseService(new Mock<ILogService>().Object, _context, ClientLanguage.German);
        _config = LoadFixture();
        _db.MigrateFromConfig(_config);
    }

    public void Dispose()
    {
        _db.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    private static Configuration LoadFixture()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(FixtureResourceName)
            ?? throw new System.InvalidOperationException(
                $"Embedded resource not found: {FixtureResourceName}. " +
                $"Available: {string.Join(", ", assembly.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
        return JsonConvert.DeserializeObject<Configuration>(json, settings)
            ?? throw new System.InvalidOperationException("Failed to deserialize test config");
    }

    // ── Characters ──────────────────────────────────────────────────────

    [Fact]
    public void Characters_AreCreatedForEveryUniqueMapping()
    {
        // Fixture has 20 NpcMapData + 1 MappedPlayer. Two of the NPCs ("Y'shtola" and
        // "Y'shtolas Stimme") differ in name — case-only-different names would dedupe,
        // but these are genuinely different so both survive. No same-name collisions.
        var characters = _context.Characters.AsNoTracking().ToList();
        Assert.Equal(21, characters.Count);
    }

    [Fact]
    public void Characters_StampedWithSessionLanguage()
    {
        // Migration uses _clientLanguage at construction time — German in this test.
        var characters = _context.Characters.AsNoTracking().ToList();
        Assert.All(characters, c => Assert.Equal((int)ClientLanguage.German, c.Language));
    }

    [Fact]
    public void Characters_PreserveVoiceKey()
    {
        // Spot-check: a few entries with explicit voice keys land intact on the entity.
        var cid = _context.Characters.AsNoTracking().Single(c => c.Name == "Cid");
        Assert.Equal("Male_Hyur_Cid.wav", cid.VoiceKey);

        var khloe = _context.Characters.AsNoTracking().Single(c => c.Name == "Khloe Aliapoh");
        Assert.Equal("Female_All-Child_NPC001.wav", khloe.VoiceKey);

        var picotNoVoice = _context.Characters.AsNoTracking().Single(c => c.Name == "Picot");
        Assert.Equal("", picotNoVoice.VoiceKey);
    }

    [Fact]
    public void Characters_PreserveRaceAndGender()
    {
        var yshtola = _context.Characters.AsNoTracking().Single(c => c.Name == "Y'shtola");
        Assert.Equal(3, yshtola.Race);          // Miqote
        Assert.Equal(1, yshtola.Gender);        // Female
        Assert.Equal("Miqote", yshtola.RaceStr);

        var amon = _context.Characters.AsNoTracking().Single(c => c.Name == "");
        Assert.Equal(0, amon.Race);             // Unknown
        Assert.Equal(-1, amon.Gender);          // None / placeholder
        Assert.Equal("Male_Unknown_Amon.wav", amon.VoiceKey);
    }

    // ── Character contexts (npc / player / bubble) ──────────────────────

    [Fact]
    public void NpcMappings_CreateNpcContexts()
    {
        // Every MappedNpc → one "npc" context. None of the fixture NPCs has HasBubbles=true,
        // so no bubble contexts are created.
        var npcContexts = _context.CharacterContexts.AsNoTracking()
            .Where(cc => cc.ContextType == "npc").ToList();
        Assert.Equal(20, npcContexts.Count);
    }

    [Fact]
    public void PlayerMapping_CreatesPlayerContext()
    {
        // The single MappedPlayer in the fixture (Noctus Flatulentus) gets its own
        // "player" context — distinct from "npc" so the Voice Clip Manager can filter.
        var playerCtx = _context.CharacterContexts.AsNoTracking()
            .Where(cc => cc.ContextType == "player").ToList();
        Assert.Single(playerCtx);

        var player = _context.Characters.AsNoTracking()
            .Single(c => c.Id == playerCtx[0].CharacterId);
        Assert.Equal("Noctus Flatulentus", player.Name);
        Assert.Equal(1, player.ObjectKind);     // ObjectKind.Pc
    }

    [Fact]
    public void NpcContexts_PreserveIsEnabledAndVolume()
    {
        var yshtola = _context.Characters.AsNoTracking().Single(c => c.Name == "Y'shtola");
        var ctx = _context.CharacterContexts.AsNoTracking()
            .Single(cc => cc.CharacterId == yshtola.Id && cc.ContextType == "npc");
        Assert.True(ctx.IsEnabled);
        Assert.Equal(1.5f, ctx.Volume);         // Y'shtola has Volume=1.5 in the fixture
    }

    // ── Voices ──────────────────────────────────────────────────────────

    [Fact]
    public void Voices_AreUpserted()
    {
        // Fixture has 10 EchokrautVoices. Each lands as a voice row; allowed-races and
        // allowed-genders rows are created for each.
        var voices = _context.Voices.AsNoTracking().ToList();
        Assert.Equal(10, voices.Count);
    }

    [Fact]
    public void Voices_PreserveAllFlags()
    {
        var defaultVoice = _context.Voices.AsNoTracking()
            .Single(v => v.BackendVoice == "Female_All-Child_NPC001.wav");
        Assert.True(defaultVoice.IsDefault);
        Assert.True(defaultVoice.IsChildVoice);
        Assert.True(defaultVoice.UseAsRandom);
        Assert.Equal(2.0f, defaultVoice.Volume);

        var disabledRandom = _context.Voices.AsNoTracking()
            .Single(v => v.BackendVoice == "Male_AuRa-Elezen-Endless-Hrothgar-Hyur-Lupin-Miqote_NPC055.wav");
        Assert.False(disabledRandom.IsEnabled);
        Assert.True(disabledRandom.UseAsRandom);
    }

    [Fact]
    public void Voices_PopulateAllowedRacesAndGenders()
    {
        var npc080 = _context.Voices.AsNoTracking()
            .Single(v => v.BackendVoice == "Male_Frog-Gnath-Goblin-Hyur-Kobold-Lalafell-Moogle-Namazu-Pixie_NPC080.wav");
        var races = _context.VoiceAllowedRaces.AsNoTracking()
            .Where(r => r.VoiceId == npc080.Id).Select(r => r.Race).OrderBy(r => r).ToList();
        Assert.Equal(new[] { 1, 5, 13, 14, 23, 25, 30, 31, 32 }, races);

        var genders = _context.VoiceAllowedGenders.AsNoTracking()
            .Where(g => g.VoiceId == npc080.Id).Select(g => g.Gender).ToList();
        Assert.Equal(new[] { 0 }, genders);
    }

    // ── Phonetic corrections ────────────────────────────────────────────

    [Fact]
    public void PhoneticCorrections_AreImported()
    {
        // Fixture has 26 phonetic corrections; all should land in the DB.
        var pcs = _context.PhoneticCorrections.AsNoTracking().ToList();
        Assert.Equal(26, pcs.Count);

        Assert.Contains(pcs, p => p.OriginalText == "Viera" && p.CorrectedText == "Wie era");
        Assert.Contains(pcs, p => p.OriginalText == "Kostüm" && p.CorrectedText == "Kostühm");
    }

    // ── Muted dialogues ─────────────────────────────────────────────────

    [Fact]
    public void MutedDialogues_FlagOnlyExistingInstances()
    {
        // MutedNpcDialogues are bare ENpcBase IDs. With no character_instances yet
        // populated (nothing has been encountered in-game), none of the muted base IDs
        // have a row to flip. The migration should not crash and should not create
        // synthetic instances — the muted state propagates the next time the live
        // runtime creates an instance for one of those base IDs.
        var instances = _context.CharacterInstances.AsNoTracking().ToList();
        Assert.Empty(instances);
    }

    [Fact]
    public void MutedDialogues_PropagateToPreseededInstance()
    {
        // Pre-seed an instance for one of the muted base IDs to verify the migration
        // would have set IsMuted on it. Mirrors the post-migration scenario where the
        // user encounters the NPC and the live runtime upserts its instance.
        var character = _context.Characters.AsNoTracking().First();
        var instance = new CharacterInstanceEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 1046073,    // First entry in fixture's MutedNpcDialogues
            IsMuted = false,
        };
        _context.CharacterInstances.Add(instance);
        _context.SaveChanges();

        // Re-run migration via a fresh service. NeedsMigration is false now (lists were
        // cleared), so MigrateFromConfig won't auto-fire — call it explicitly to test
        // just the muted-flag propagation path against the new instance.
        _config.MutedNpcDialogues.Add(1046073);
        _db.MigrateFromConfig(_config);

        var refreshed = _context.CharacterInstances.AsNoTracking()
            .Single(ci => ci.NpcBaseId == 1046073);
        Assert.True(refreshed.IsMuted);
    }

    // ── Source list cleanup ─────────────────────────────────────────────

    [Fact]
    public void Migration_ClearsSourceLists()
    {
        // After a successful migration the source lists must be emptied so
        // NeedsMigration returns false on subsequent plugin starts.
        Assert.Empty(_config.MappedNpcs);
        Assert.Empty(_config.MappedPlayers);
        Assert.Empty(_config.EchokrautVoices);
        Assert.Empty(_config.PhoneticCorrections);
    }
}
