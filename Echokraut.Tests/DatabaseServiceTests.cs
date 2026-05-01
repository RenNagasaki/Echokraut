using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Echokraut.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly DatabaseService _db;
    private readonly EchokrautDbContext _context;
    private readonly SqliteConnection _connection;

    public DatabaseServiceTests()
    {
        // In-memory SQLite (not the EF InMemory provider) so SQLite-only constructs we use
        // in production — EF.Functions.Collate, COLLATE NOCASE indexes, schema migrations
        // via raw SQL — actually exercise.
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

    // ── Schema ──────────────────────────────────────────────

    [Fact]
    public void Constructor_CreatesAllTables()
    {
        Assert.NotNull(_context.Characters);
        Assert.NotNull(_context.CharacterContexts);
        Assert.NotNull(_context.CharacterInstances);
        Assert.NotNull(_context.VoiceClips);
        Assert.NotNull(_context.Voices);
        Assert.NotNull(_context.VoiceAllowedGenders);
        Assert.NotNull(_context.VoiceAllowedRaces);
        Assert.NotNull(_context.PhoneticCorrections);
    }

    // ── Character CRUD ──────────────────────────────────────

    [Fact]
    public void UpsertCharacter_InsertsNew()
    {
        var character = new CharacterEntity
        {
            Name = "Thancred",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
            BodyType = (int)BodyType.Adult,
            VoiceKey = "voice_male_01"
        };

        var result = _db.UpsertCharacter(character);
        Assert.True(result.Id > 0);
        Assert.Equal("Thancred", result.Name);
    }

    [Fact]
    public void UpsertCharacter_UpdatesExisting()
    {
        var character = new CharacterEntity
        {
            Name = "Thancred",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
            VoiceKey = "voice_01"
        };
        _db.UpsertCharacter(character);

        var updated = new CharacterEntity
        {
            Name = "Thancred",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
            VoiceKey = "voice_02"
        };
        _db.UpsertCharacter(updated);

        var found = _db.FindCharacter("Thancred", Genders.Male, NpcRaces.Hyur, language: 1);
        Assert.NotNull(found);
        Assert.Equal("voice_02", found!.VoiceKey);
    }

    [Fact]
    public void FindCharacter_ReturnsNullWhenNotFound()
    {
        Assert.Null(_db.FindCharacter("Nobody", Genders.Male, NpcRaces.Hyur, language: 1));
    }

    [Fact]
    public void FindCharacter_IsCaseInsensitive()
    {
        // Production scenario: harvest writes "stille Druidin" (lowercase from German [a]→"e"
        // adjective resolution), runtime later looks up by ObjectTable's "Stille Druidin".
        // Both must resolve to the same row — that's the COLLATE NOCASE contract.
        _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Stille Druidin",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Female,
            Language = 2,
        });

        var lower = _db.FindCharacter("stille Druidin", Genders.Female, NpcRaces.Elezen, language: 2);
        var upper = _db.FindCharacter("Stille Druidin", Genders.Female, NpcRaces.Elezen, language: 2);
        var mixed = _db.FindCharacter("STILLE druidin", Genders.Female, NpcRaces.Elezen, language: 2);

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.NotNull(mixed);
        Assert.Equal(lower!.Id, upper!.Id);
        Assert.Equal(lower.Id, mixed!.Id);
    }

    [Fact]
    public void UpsertCharacter_MergesCaseDifferentNames()
    {
        // Two writers with different casings hit the same row; the second write must
        // update the existing row, not insert a new one.
        var first = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Stille Druidin",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Female,
            Language = 2,
            VoiceKey = "voice_a",
        });
        var second = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "stille Druidin",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Female,
            Language = 2,
            VoiceKey = "voice_b",
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("voice_b", second.VoiceKey);
    }

    [Fact]
    public void DeleteCharacter_RemovesCharacter()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Temp",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Female,
            VoiceKey = ""
        });

        _db.DeleteCharacter(character.Id);
        Assert.Null(_db.FindCharacter("Temp", Genders.Female, NpcRaces.Elezen, language: 1));
    }

    // ── Character Contexts ──────────────────────────────────

    [Fact]
    public void UpsertContext_CreatesAndUpdates()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Test",
            Race = (int)NpcRaces.Miqote,
            Gender = (int)Genders.Female
        });

        var ctx = _db.UpsertContext(character.Id, "npc", true, 0.8f);
        Assert.True(ctx.IsEnabled);
        Assert.Equal(0.8f, ctx.Volume);

        _db.UpsertContext(character.Id, "npc", false, 0.5f);
        var updated = _db.GetContext(character.Id, "npc");
        Assert.NotNull(updated);
        Assert.False(updated!.IsEnabled);
        Assert.Equal(0.5f, updated.Volume);
    }

    // ── Character Instances ─────────────────────────────────

    [Fact]
    public void GetOrCreateInstance_CreatesNew()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Guard",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male
        });

        var instance = _db.GetOrCreateInstance(character.Id, 1006004);
        Assert.Equal(1006004L, instance.NpcBaseId);
        Assert.False(instance.IsMuted);
    }

    [Fact]
    public void MuteUnmuteInstance_Works()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Guard",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male
        });
        _db.GetOrCreateInstance(character.Id, 1006004);

        _db.MuteInstance(1006004);
        Assert.Contains((uint)1006004, _db.GetMutedBaseIds());

        _db.UnmuteInstance(1006004);
        Assert.DoesNotContain((uint)1006004, _db.GetMutedBaseIds());
    }

    [Fact]
    public void ClearInstanceMutes_ClearsAll()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Guard",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male
        });
        _db.GetOrCreateInstance(character.Id, 100);
        _db.GetOrCreateInstance(character.Id, 200);
        _db.MuteInstance(100);
        _db.MuteInstance(200);

        _db.ClearInstanceMutes();
        Assert.Empty(_db.GetMutedBaseIds());
    }

    // ── Voices ──────────────────────────────────────────────

    [Fact]
    public void UpsertVoice_InsertsWithJunctionTables()
    {
        var voice = new VoiceEntity
        {
            BackendVoice = "xtts_female_001",
            VoiceName = "Female_Hyur",
            IsEnabled = true,
            AllowedGenders = new List<VoiceAllowedGenderEntity>
            {
                new() { Gender = (int)Genders.Female }
            },
            AllowedRaces = new List<VoiceAllowedRaceEntity>
            {
                new() { Race = (int)NpcRaces.Hyur },
                new() { Race = (int)NpcRaces.Miqote }
            }
        };

        var result = _db.UpsertVoice(voice);
        Assert.True(result.Id > 0);

        var fetched = _db.GetVoiceByKey("xtts_female_001");
        Assert.NotNull(fetched);
        Assert.Single(fetched!.AllowedGenders);
        Assert.Equal(2, fetched.AllowedRaces.Count);
    }

    [Fact]
    public void DeleteVoice_RemovesVoice()
    {
        _db.UpsertVoice(new VoiceEntity { BackendVoice = "temp_voice", VoiceName = "Temp" });
        _db.DeleteVoice("temp_voice");
        Assert.Null(_db.GetVoiceByKey("temp_voice"));
    }

    // ── Phonetic Corrections ────────────────────────────────

    [Fact]
    public void PhoneticCorrection_CRUD()
    {
        _db.UpsertPhoneticCorrection("Miqo'te", "Mee-koh-teh");
        Assert.Single(_db.GetPhoneticCorrections());

        _db.UpsertPhoneticCorrection("Miqo'te", "Mee-ko-tay");
        Assert.Single(_db.GetPhoneticCorrections());
        Assert.Equal("Mee-ko-tay", _db.GetPhoneticCorrections()[0].CorrectedText);

        _db.DeletePhoneticCorrection("Miqo'te");
        Assert.Empty(_db.GetPhoneticCorrections());
    }

    // ── Voice Clips ──────────────────────────────────────────

    [Fact]
    public void LogVoiceClip_InsertsAndQueries()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Alphinaud",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Male
        });

        _db.LogVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 1001000,
            Timestamp = DateTime.UtcNow,
            TextSource = 2, // AddonTalk
            Language = 1,
            VoiceKey = "voice_01",
            OriginalText = "Hello, adventurer!",
            CleanedText = "Hello adventurer",
            BodyType = (int)BodyType.Adult
        });

        Assert.Equal(1, _db.GetVoiceClipCount());
        var voiceClips = _db.GetVoiceClips();
        Assert.Single(voiceClips);
        Assert.Equal("Hello, adventurer!", voiceClips[0].OriginalText);
        Assert.Equal("Alphinaud", voiceClips[0].Character?.Name);
    }

    [Fact]
    public void LogOrUpdateVoiceClip_ReturnsPersistedEntityWithId_OnInsert()
    {
        // Live path needs the Id back so AudioPlaybackService can later log a generation row.
        // Without this, live generations are invisible to the Voice Clip Manager.
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Y'shtola",
            Race = (int)NpcRaces.Miqote,
            Gender = (int)Genders.Female
        });

        var result = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 2002000,
            Timestamp = DateTime.UtcNow,
            TextSource = 2,
            Language = 1,
            OriginalText = "Stay your hand!",
            CleanedText = "Stay your hand"
        });

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("Stay your hand!", result.OriginalText);
    }

    [Fact]
    public void LogOrUpdateVoiceClip_ReturnsSameEntityWithId_OnUpdate()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Urianger",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Male
        });

        var first = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 3003000,
            Timestamp = DateTime.UtcNow,
            TextSource = 2,
            Language = 1,
            OriginalText = "By thy leave."
        });

        var second = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 3003000,
            Timestamp = DateTime.UtcNow,
            TextSource = 2,
            Language = 1,
            OriginalText = "By thy leave.",
            CleanedText = "By thy leave"
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _db.GetVoiceClipCount());
    }

    [Fact]
    public void LogOrUpdateVoiceClip_FollowedByLogGeneration_RoundTrips()
    {
        // Mirrors the live path: VoiceMessageProcessor logs the clip, AudioPlaybackService
        // later writes the audio file and logs the generation row using the returned Id.
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Estinien",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Male
        });

        var clip = _db.LogOrUpdateVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            NpcBaseId = 4004000,
            Timestamp = DateTime.UtcNow,
            TextSource = 2,
            Language = 1,
            OriginalText = "The Azure Dragoon stands ready."
        });

        const long playerContentId = 0x1122334455667788L;
        const string playerName = "Test Player";
        const string savePath = "C:/audio/test.wav";

        _db.LogVoiceClipGeneration(clip.Id, playerContentId, playerName, savePath);

        var gen = _db.GetVoiceClipGeneration(clip.Id, playerContentId);
        Assert.NotNull(gen);
        Assert.Equal(savePath, gen!.SavePath);
        Assert.Equal(playerName, gen.PlayerName);
        Assert.Equal(0, gen.AliasGender);
    }

    [Fact]
    public void WipeAll_FiresDatabaseWipedEvent()
    {
        // BackendService subscribes to this event to re-discover voices from the running TTS
        // backend after a wipe. Without it, the voices table stays empty until the user
        // restarts the plugin.
        var wipedFired = false;
        _db.DatabaseWiped += () => wipedFired = true;

        _db.WipeAll();

        Assert.True(wipedFired);
    }

    [Fact]
    public void ClearVoiceClips_RemovesAll()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Test",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male
        });
        _db.LogVoiceClip(new VoiceClipEntity
        {
            CharacterId = character.Id,
            Timestamp = DateTime.UtcNow,
            TextSource = 2,
            Language = 1,
            OriginalText = "Test"
        });

        _db.ClearVoiceClips();
        Assert.Equal(0, _db.GetVoiceClipCount());
    }

    // ── Migration ───────────────────────────────────────────

    [Fact]
    public void MigrateFromConfig_ImportsAllData()
    {
        var config = new Configuration
        {
            MappedNpcs = new List<NpcMapData>
            {
                new(ObjectKind.EventNpc)
                {
                    Name = "Thancred",
                    Race = NpcRaces.Hyur,
                    Gender = Genders.Male,
                    voice = "voice_01",
                    IsEnabled = true,
                    Volume = 0.9f,
                    HasBubbles = true,
                    IsEnabledBubble = false,
                    VolumeBubble = 0.5f
                }
            },
            MappedPlayers = new List<NpcMapData>
            {
                new(ObjectKind.Pc)
                {
                    Name = "Player",
                    Race = NpcRaces.Miqote,
                    Gender = Genders.Female,
                    voice = "voice_02",
                    IsEnabled = true,
                    Volume = 1.0f
                }
            },
            EchokrautVoices = new List<EchokrautVoice>
            {
                new()
                {
                    BackendVoice = "voice_01",
                    voiceName = "Male_Hyur",
                    IsEnabled = true,
                    AllowedGenders = new List<Genders> { Genders.Male },
                    AllowedRaces = new List<NpcRaces> { NpcRaces.Hyur }
                }
            },
            PhoneticCorrections = new List<PhoneticCorrection>
            {
                new("Eorzea", "Ay-or-zay-ah")
            }
        };

        Assert.True(_db.NeedsMigration(config));
        _db.MigrateFromConfig(config);

        // Config should be cleared
        Assert.Empty(config.MappedNpcs);
        Assert.Empty(config.MappedPlayers);
        Assert.Empty(config.EchokrautVoices);
        Assert.Empty(config.PhoneticCorrections);

        // DB should have data
        Assert.Single(_db.GetVoices());
        Assert.Single(_db.GetPhoneticCorrections());

        // Thancred should have NPC + bubble contexts
        var thancred = _db.FindCharacter("Thancred", Genders.Male, NpcRaces.Hyur, language: 1);
        Assert.NotNull(thancred);
        Assert.Equal("voice_01", thancred!.VoiceKey);

        var npcCtx = _db.GetContext(thancred.Id, "npc");
        Assert.NotNull(npcCtx);
        Assert.True(npcCtx!.IsEnabled);
        Assert.Equal(0.9f, npcCtx.Volume);

        var bubbleCtx = _db.GetContext(thancred.Id, "bubble");
        Assert.NotNull(bubbleCtx);
        Assert.False(bubbleCtx!.IsEnabled);
        Assert.Equal(0.5f, bubbleCtx.Volume);

        // Player
        var player = _db.FindCharacter("Player", Genders.Female, NpcRaces.Miqote, language: 1);
        Assert.NotNull(player);
    }

    [Fact]
    public void MigrateFromConfig_Idempotent()
    {
        var config = new Configuration
        {
            EchokrautVoices = new List<EchokrautVoice>
            {
                new() { BackendVoice = "v1", voiceName = "Test" }
            }
        };

        _db.MigrateFromConfig(config);
        // After migration, config is cleared — second call should detect no data
        Assert.False(_db.NeedsMigration(config));
    }

    // ── Cache ───────────────────────────────────────────────

    [Fact]
    public void GetNpcs_ReturnsCachedList()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "NpcTest",
            Race = (int)NpcRaces.Lalafell,
            Gender = (int)Genders.Female
        });
        _db.UpsertContext(character.Id, "npc");

        var npcs = _db.GetNpcs();
        Assert.Single(npcs);
        Assert.Equal("NpcTest", npcs[0].Name);
    }

    [Fact]
    public void GetPlayers_ReturnsCachedList()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "PlayerTest",
            Race = (int)NpcRaces.AuRa,
            Gender = (int)Genders.Male
        });
        _db.UpsertContext(character.Id, "player");

        var players = _db.GetPlayers();
        Assert.Single(players);
        Assert.Equal("PlayerTest", players[0].Name);
    }
}
