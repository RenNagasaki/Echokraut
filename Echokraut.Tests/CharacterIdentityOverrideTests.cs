using System;
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

/// <summary>
/// The user's gender/race correction (reported 2026-09-07: editing an NPC created a second entry
/// and the change never took effect).
///
/// <para>Root cause the design now works around: gender and race are part of the character's lookup
/// key AND are re-derived from the game object on every spoken line. An edit written into the key
/// therefore produces a row the lookup can never reach, and the next line recreates the original —
/// which is what the user saw. The correction is stored beside the key instead.</para>
/// </summary>
public class CharacterIdentityOverrideTests : IDisposable
{
    private readonly DatabaseService _db;
    private readonly EchokrautDbContext _context;
    private readonly SqliteConnection _connection;

    public CharacterIdentityOverrideTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<EchokrautDbContext>().UseSqlite(_connection).Options;
        _context = new EchokrautDbContext(options);
        _db = new DatabaseService(new Mock<ILogService>().Object, _context);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private CharacterEntity AddMaleScholarPet() => _db.UpsertCharacter(new CharacterEntity
    {
        Name = "Eos",
        Gender = (int)Genders.Male,
        Race = (int)NpcRaces.Hyur,
        RaceStr = NpcRaces.Hyur.ToString(),
        Language = 1,
        VoiceKey = "male_voice.wav",
        ObjectKind = (int)ObjectKind.BattleNpc,
    });

    [Fact]
    public void FreshCharacter_HasNoOverride()
    {
        var saved = AddMaleScholarPet();
        Assert.Null(saved.GenderOverride);
        Assert.Null(saved.RaceOverride);
    }

    [Fact]
    public void SetIdentityOverride_StoresTheCorrection()
    {
        var saved = AddMaleScholarPet();

        Assert.True(_db.SetIdentityOverride(saved.Id, (int)Genders.Female, null));

        var reread = _db.FindCharacter("Eos", Genders.Male, NpcRaces.Hyur, 1);
        Assert.NotNull(reread);
        Assert.Equal((int)Genders.Female, reread!.GenderOverride);
        Assert.Null(reread.RaceOverride);
    }

    [Fact]
    public void SetIdentityOverride_LeavesTheLookupKeyReachable()
    {
        // The whole point: the game keeps reporting male, so the row must still be found that way.
        var saved = AddMaleScholarPet();
        _db.SetIdentityOverride(saved.Id, (int)Genders.Female, null);

        Assert.NotNull(_db.FindCharacter("Eos", Genders.Male, NpcRaces.Hyur, 1));
    }

    [Fact]
    public void SetIdentityOverride_DoesNotCreateASecondRow()
    {
        // The reported symptom, asserted directly.
        var saved = AddMaleScholarPet();
        _db.SetIdentityOverride(saved.Id, (int)Genders.Female, null);

        Assert.Single(_context.Characters);
    }

    [Fact]
    public void SetIdentityOverride_NullClearsIt()
    {
        var saved = AddMaleScholarPet();
        _db.SetIdentityOverride(saved.Id, (int)Genders.Female, (int)NpcRaces.Elezen);

        _db.SetIdentityOverride(saved.Id, null, null);

        var reread = _db.FindCharacter("Eos", Genders.Male, NpcRaces.Hyur, 1);
        Assert.Null(reread!.GenderOverride);
        Assert.Null(reread.RaceOverride);
    }

    [Fact]
    public void SetIdentityOverride_UnknownCharacter_ReturnsFalse()
    {
        Assert.False(_db.SetIdentityOverride(9999, (int)Genders.Female, null));
    }

    [Fact]
    public void Upsert_FromTheGame_DoesNotEraseTheCorrection()
    {
        // This is the regression that would quietly undo the fix: UpsertCharacter runs on every
        // dialogue line with an entity rebuilt from the GAME, which carries no override. If it
        // copied that null across, the correction would survive exactly until the NPC next spoke.
        var saved = AddMaleScholarPet();
        _db.SetIdentityOverride(saved.Id, (int)Genders.Female, null);

        _db.UpsertCharacter(new CharacterEntity
        {
            Name = "Eos",
            Gender = (int)Genders.Male,
            Race = (int)NpcRaces.Hyur,
            RaceStr = NpcRaces.Hyur.ToString(),
            Language = 1,
            VoiceKey = "male_voice.wav",
            ObjectKind = (int)ObjectKind.BattleNpc,
        });

        var reread = _db.FindCharacter("Eos", Genders.Male, NpcRaces.Hyur, 1);
        Assert.Equal((int)Genders.Female, reread!.GenderOverride);
    }

    // ── NpcMapData: what voice selection reads ───────────────────────────────

    [Fact]
    public void EffectiveValues_FallBackToTheGame()
    {
        var data = new NpcMapData(ObjectKind.BattleNpc) { Gender = Genders.Male, Race = NpcRaces.Hyur };

        Assert.Equal(Genders.Male, data.EffectiveGender);
        Assert.Equal(NpcRaces.Hyur, data.EffectiveRace);
        Assert.False(data.HasIdentityOverride);
    }

    [Fact]
    public void EffectiveValues_PreferTheCorrection()
    {
        var data = new NpcMapData(ObjectKind.BattleNpc)
        {
            Gender = Genders.Male,
            Race = NpcRaces.Hyur,
            GenderOverride = Genders.Female,
        };

        Assert.Equal(Genders.Female, data.EffectiveGender);
        Assert.Equal(NpcRaces.Hyur, data.EffectiveRace); // untouched half still follows the game
        Assert.True(data.HasIdentityOverride);
    }

    [Fact]
    public void Override_DoesNotChangeTheIdentityFields()
    {
        // Gender/Race are the key; a correction must never be visible there.
        var data = new NpcMapData(ObjectKind.BattleNpc)
        {
            Gender = Genders.Male,
            Race = NpcRaces.Hyur,
            GenderOverride = Genders.Female,
            RaceOverride = NpcRaces.Elezen,
        };

        Assert.Equal(Genders.Male, data.Gender);
        Assert.Equal(NpcRaces.Hyur, data.Race);
    }
}
