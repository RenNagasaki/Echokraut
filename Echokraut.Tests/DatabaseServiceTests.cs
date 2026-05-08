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

    [Fact]
    public void EnsureContext_CreatesWithDefaultsAndPreservesExisting()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "PreserveTest",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Female,
        });

        // First call creates the row with defaults.
        var created = _db.EnsureContext(character.Id, "npc");
        Assert.True(created.IsEnabled);
        Assert.Equal(1.0f, created.Volume);

        // User mutates settings via the UI path.
        _db.UpsertContext(character.Id, "npc", false, 0.25f);

        // Subsequent EnsureContext (e.g. a re-harvest) must NOT stomp the user's values.
        var preserved = _db.EnsureContext(character.Id, "npc");
        Assert.False(preserved.IsEnabled);
        Assert.Equal(0.25f, preserved.Volume);

        var fromDb = _db.GetContext(character.Id, "npc");
        Assert.NotNull(fromDb);
        Assert.False(fromDb!.IsEnabled);
        Assert.Equal(0.25f, fromDb.Volume);
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

        _db.LogVoiceClipGeneration(clip.Id, playerContentId, playerName, savePath, voiceKey: "");

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

    // ── Speaker aliases ─────────────────────────────────────

    [Fact]
    public void UpsertSpeakerAlias_InsertsAndIsCaseInsensitive()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Y'shtola Rhul",
            Race = (int)NpcRaces.Miqote,
            Gender = (int)Genders.Female,
            Language = 2, // German
        });
        _db.UpsertSpeakerAlias(character.Id, 2, "Geheimnisvolle Dame");

        // Lookup uses case-insensitive normalized key.
        Assert.Equal(character.Id, _db.FindCharacterIdByAlias("geheimnisvolle dame", 2));
        Assert.Equal(character.Id, _db.FindCharacterIdByAlias("GEHEIMNISVOLLE DAME", 2));
        Assert.Equal(character.Id, _db.FindCharacterIdByAlias("  Geheimnisvolle Dame  ", 2));
    }

    [Fact]
    public void UpsertSpeakerAlias_DeduplicatesOnRepeatedCall()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Tataru Taru",
            Race = (int)NpcRaces.Lalafell,
            Gender = (int)Genders.Female,
            Language = 1,
        });
        _db.UpsertSpeakerAlias(character.Id, 1, "Energetic Lalafell");
        _db.UpsertSpeakerAlias(character.Id, 1, "Energetic Lalafell"); // dup
        _db.UpsertSpeakerAlias(character.Id, 1, "energetic lalafell"); // case-only dup

        var aliases = _db.GetSpeakerAliases(character.Id);
        Assert.Single(aliases);
    }

    [Fact]
    public void FindCharacterIdByAlias_ReturnsNullForUnknown()
    {
        Assert.Null(_db.FindCharacterIdByAlias("Nobody", 1));
        Assert.Null(_db.FindCharacterIdByAlias("", 1));
    }

    [Fact]
    public void FindCharacterIdByAlias_ReturnsNullWhenAmbiguous()
    {
        // Two characters share the alias "???" — the convenience getter must return null
        // so callers know they need disambiguation. The full multi-match list is exposed
        // via FindCharacterIdsByAlias for runtime resolution.
        var charA = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "MaskedA", Race = (int)NpcRaces.Hyur, Gender = (int)Genders.Male, Language = 1,
        });
        var charB = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "MaskedB", Race = (int)NpcRaces.Elezen, Gender = (int)Genders.Female, Language = 1,
        });
        _db.UpsertSpeakerAlias(charA.Id, 1, "???");
        _db.UpsertSpeakerAlias(charB.Id, 1, "???");

        Assert.Null(_db.FindCharacterIdByAlias("???", 1));

        var ids = _db.FindCharacterIdsByAlias("???", 1);
        Assert.Equal(2, ids.Count);
        Assert.Contains(charA.Id, ids);
        Assert.Contains(charB.Id, ids);
    }

    [Fact]
    public void FindCharacterIdsByAlias_ReturnsEmptyForUnknown()
    {
        Assert.Empty(_db.FindCharacterIdsByAlias("Nobody", 1));
        Assert.Empty(_db.FindCharacterIdsByAlias("", 1));
    }

    [Fact]
    public void FindCharacterIdsByAlias_ReturnsCopy_NotInternalReference()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Tataru Taru", Race = (int)NpcRaces.Lalafell, Gender = (int)Genders.Female, Language = 1,
        });
        _db.UpsertSpeakerAlias(character.Id, 1, "Energetic Lalafell");

        var first = _db.FindCharacterIdsByAlias("Energetic Lalafell", 1);
        first.Add(99999); // mutate caller's copy

        var second = _db.FindCharacterIdsByAlias("Energetic Lalafell", 1);
        Assert.Single(second);
        Assert.DoesNotContain(99999, second);
    }

    [Fact]
    public void FindCharacterIdByAlias_ScopedByLanguage()
    {
        // Same fakename string in two languages must resolve independently. Live runtime
        // queries by the client-language locale so cross-language collisions would be a bug.
        var charDe = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Y'shtola Rhul DE", Race = (int)NpcRaces.Miqote, Gender = (int)Genders.Female, Language = 2,
        });
        var charEn = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Y'shtola Rhul EN", Race = (int)NpcRaces.Miqote, Gender = (int)Genders.Female, Language = 1,
        });
        _db.UpsertSpeakerAlias(charDe.Id, 2, "Mysterious Lady");
        _db.UpsertSpeakerAlias(charEn.Id, 1, "Mysterious Lady");

        Assert.Equal(charDe.Id, _db.FindCharacterIdByAlias("Mysterious Lady", 2));
        Assert.Equal(charEn.Id, _db.FindCharacterIdByAlias("Mysterious Lady", 1));
        Assert.Null(_db.FindCharacterIdByAlias("Mysterious Lady", 3)); // FR
    }

    [Fact]
    public void WipeAll_ClearsAliasCache()
    {
        var character = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Doomed", Race = (int)NpcRaces.Hyur, Gender = (int)Genders.Male, Language = 1,
        });
        _db.UpsertSpeakerAlias(character.Id, 1, "Faceless");
        Assert.Equal(character.Id, _db.FindCharacterIdByAlias("Faceless", 1));

        _db.WipeAll();

        Assert.Null(_db.FindCharacterIdByAlias("Faceless", 1));
        Assert.Empty(_db.GetSpeakerAliases(character.Id));
    }

    // ── UpsertVoice burst-insertion (regression: ChangeTracker collision) ────

    [Fact]
    public void UpsertVoice_BurstInsertWithCompositeKeyChildren_DoesNotCollide()
    {
        // Regression: BackendService.MapVoices upserts every backend voice in a tight loop
        // after AllTalk starts. Each VoiceEntity ships with VoiceAllowedGenderEntity children
        // that have a composite PK (VoiceId, Gender). Earlier the second iteration threw
        // "another instance with the same key value for {'VoiceId', 'Gender'} is already
        // being tracked" because EF's identity map didn't shed iteration-1's children before
        // iteration 2 attached its own (also-with-VoiceId=0) children. Verify the fix holds
        // for a typical voice-list size.
        var voiceCount = 50;
        for (var i = 0; i < voiceCount; i++)
        {
            var voice = new VoiceEntity
            {
                BackendVoice = $"voice_{i:D3}.wav",
                VoiceName = $"Voice {i}",
                AllowedGenders = new List<VoiceAllowedGenderEntity>
                {
                    new() { Gender = (int)Genders.Male },
                    new() { Gender = (int)Genders.Female },
                },
                AllowedRaces = new List<VoiceAllowedRaceEntity>
                {
                    new() { Race = (int)NpcRaces.Hyur },
                    new() { Race = (int)NpcRaces.Elezen },
                },
            };
            _db.UpsertVoice(voice); // Must NOT throw.
        }

        Assert.Equal(voiceCount, _db.GetVoices().Count);
    }

    [Fact]
    public void UpsertVoice_DuplicateChildrenInInput_AreDeduped()
    {
        // Real-world case: voice filenames sometimes carry the same race twice
        // ("Male_Roegadyn-Roegadyn_NPC123.wav") and the parser would push Roegadyn onto
        // AllowedRaces twice. Without dedup at UpsertVoice the tracker fails on the
        // (VoiceId, Race) composite key. Dedup must produce a single row per (Race) for
        // this voice.
        var voice = new VoiceEntity
        {
            BackendVoice = "Male_Roegadyn-Roegadyn_NPC001.wav",
            VoiceName = "RoegadynNpc",
            AllowedGenders = new List<VoiceAllowedGenderEntity>
            {
                new() { Gender = (int)Genders.Male },
                new() { Gender = (int)Genders.Male }, // duplicate
            },
            AllowedRaces = new List<VoiceAllowedRaceEntity>
            {
                new() { Race = (int)NpcRaces.Roegadyn },
                new() { Race = (int)NpcRaces.Roegadyn }, // duplicate
            },
        };

        var result = _db.UpsertVoice(voice); // Must NOT throw on the composite key.

        var saved = _db.GetVoices().Single(v => v.BackendVoice == voice.BackendVoice);
        Assert.Single(saved.AllowedGenders);
        Assert.Single(saved.AllowedRaces);
        Assert.Equal((int)Genders.Male, saved.AllowedGenders[0].Gender);
        Assert.Equal((int)NpcRaces.Roegadyn, saved.AllowedRaces[0].Race);
    }

    [Fact]
    public void UpsertVoice_RepeatedSameBackendKey_UpdatesInPlace()
    {
        // Update branch: same BackendVoice on second call should refresh children, not throw.
        for (var i = 0; i < 5; i++)
        {
            _db.UpsertVoice(new VoiceEntity
            {
                BackendVoice = "shared.wav",
                VoiceName = $"Iteration {i}",
                AllowedGenders = new List<VoiceAllowedGenderEntity>
                {
                    new() { Gender = (int)Genders.Male },
                },
                AllowedRaces = new List<VoiceAllowedRaceEntity>
                {
                    new() { Race = (int)NpcRaces.Hyur },
                },
            });
        }
        var voices = _db.GetVoices();
        Assert.Single(voices);
        Assert.Equal("Iteration 4", voices[0].VoiceName);
    }

    [Fact]
    public void WipeAll_ClearsCharacterCaches()
    {
        // Regression: WipeAll used to clear voices/phonetics/muted caches but forgot the
        // character caches — GetNpcs() / GetPlayers() kept returning the pre-wipe lists,
        // so NpcDataService's count-diff guard short-circuited the reload and the VC
        // Manager kept showing wiped NPCs.
        var npc = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "DoomedNpc",
            Race = (int)NpcRaces.Hyur,
            Gender = (int)Genders.Male,
            Language = 1,
            ObjectKind = (int)ObjectKind.BattleNpc,
        });
        _db.EnsureContext(npc.Id, "npc");
        var player = _db.UpsertCharacter(new CharacterEntity
        {
            Name = "DoomedPlayer",
            Race = (int)NpcRaces.Elezen,
            Gender = (int)Genders.Female,
            Language = 1,
            ObjectKind = (int)ObjectKind.Pc,
        });
        _db.EnsureContext(player.Id, "player");

        Assert.Single(_db.GetNpcs());
        Assert.Single(_db.GetPlayers());

        _db.WipeAll();

        Assert.Empty(_db.GetNpcs());
        Assert.Empty(_db.GetPlayers());
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
        _db.EnsureContext(character.Id, "npc");

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
        _db.EnsureContext(character.Id, "player");

        var players = _db.GetPlayers();
        Assert.Single(players);
        Assert.Equal("PlayerTest", players[0].Name);
    }
}
