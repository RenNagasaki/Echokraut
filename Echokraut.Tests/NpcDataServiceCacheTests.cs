using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Enums;
using Echokraut.DataClasses;
using Echokraut.DataClasses.Database;
using Echokraut.Enums;
using Echokraut.Services;
using Echotools.Logging.DataClasses;
using Echotools.Logging.Enums;
using Echotools.Logging.Services;
using Moq;
using Xunit;

namespace Echokraut.Tests;

/// <summary>
/// The in-memory NPC/player caches in <see cref="NpcDataService"/>: copy-on-write semantics,
/// find-or-add, and that removal keeps DB and cache in step.
/// </summary>
public class NpcDataServiceCacheTests
{
    private static NpcDataService Build(out Mock<IDatabaseService> db, out Mock<IBackendService> backend)
    {
        db = new Mock<IDatabaseService>();
        db.Setup(d => d.GetNpcs()).Returns(new List<CharacterEntity>());
        db.Setup(d => d.GetPlayers()).Returns(new List<CharacterEntity>());
        db.Setup(d => d.GetVoices()).Returns(new List<VoiceEntity>());
        db.Setup(d => d.UpsertCharacter(It.IsAny<CharacterEntity>()))
          .Returns((CharacterEntity e) => { e.Id = 1; return e; });

        backend = new Mock<IBackendService>();

        var jsonData = new Mock<IJsonDataService>();
        jsonData.Setup(j => j.ModelGenderMap).Returns(new List<NpcGenderRaceMap>());

        return new NpcDataService(new Mock<ILogService>().Object, db.Object, jsonData.Object);
    }

    private static NpcMapData Npc(string name, ObjectKind kind = ObjectKind.EventNpc) => new(kind)
    {
        Name = name,
        Race = NpcRaces.Hyur,
        RaceStr = "Hyur",
        Gender = Genders.Female,
        Language = ClientLanguage.English,
    };

    private static EKEventId TalkEvent() => new(1, TextSource.AddonTalk);

    // ── find-or-add ───────────────────────────────────────────────────────────

    [Fact]
    public void GetAddCharacterMapData_AddsUnknownNpcToCache()
    {
        var svc = Build(out _, out var backend);

        var result = svc.GetAddCharacterMapData(Npc("Alphinaud"), TalkEvent(), backend.Object);

        Assert.Equal("Alphinaud", result.Name);
        Assert.Single(svc.MappedNpcs);
        backend.Verify(b => b.GetVoiceOrRandom(It.IsAny<EKEventId>(), It.IsAny<NpcMapData>()), Times.Once);
    }

    [Fact]
    public void GetAddCharacterMapData_SecondCall_ReusesEntryAndDoesNotDuplicate()
    {
        var svc = Build(out _, out var backend);

        var first = svc.GetAddCharacterMapData(Npc("Alphinaud"), TalkEvent(), backend.Object);
        var second = svc.GetAddCharacterMapData(Npc("Alphinaud"), TalkEvent(), backend.Object);

        Assert.Same(first, second);
        Assert.Single(svc.MappedNpcs);
        // Voice assignment only happens for genuinely new entries.
        backend.Verify(b => b.GetVoiceOrRandom(It.IsAny<EKEventId>(), It.IsAny<NpcMapData>()), Times.Once);
    }

    [Fact]
    public void GetAddCharacterMapData_ChatSource_FilesUnderPlayers()
    {
        var svc = Build(out _, out var backend);

        svc.GetAddCharacterMapData(Npc("Someone", ObjectKind.Pc), new EKEventId(1, TextSource.Chat), backend.Object);

        Assert.Single(svc.MappedPlayers);
        Assert.Empty(svc.MappedNpcs);
    }

    [Fact]
    public void GetAddCharacterMapData_KnownRace_SupersedesRacelessEntryAndInheritsItsVoice()
    {
        var svc = Build(out var db, out var backend);
        // The service refills Voices from the DB whenever it files a new entry, so the voice has
        // to exist there — otherwise NpcMapData.Voice can't resolve the key and the inheritance
        // below has nothing to carry over.
        db.Setup(d => d.GetVoices()).Returns(new List<VoiceEntity>
        {
            new() { BackendVoice = "Female_Hyur_NPC001.wav", VoiceName = "Female_Hyur_NPC001" }
        });

        var raceless = Npc("Mystery");
        raceless.Race = NpcRaces.Unknown;
        svc.GetAddCharacterMapData(raceless, TalkEvent(), backend.Object);
        raceless.voice = "Female_Hyur_NPC001.wav";

        var withRace = Npc("Mystery"); // Race = Hyur
        var result = svc.GetAddCharacterMapData(withRace, TalkEvent(), backend.Object);

        Assert.Equal(NpcRaces.Hyur, result.Race);
        Assert.Equal("Female_Hyur_NPC001.wav", result.voice);   // voice carried over
        Assert.Single(svc.MappedNpcs);                          // raceless entry is gone
    }

    // ── removal keeps DB and cache in step ────────────────────────────────────

    [Fact]
    public void RemoveCharacter_DropsCacheEntryAndDeletesFromDb()
    {
        var svc = Build(out var db, out var backend);
        db.Setup(d => d.FindCharacter(It.IsAny<string>(), It.IsAny<Genders>(), It.IsAny<NpcRaces>(), It.IsAny<int>()))
          .Returns(new CharacterEntity { Id = 7 });

        var npc = svc.GetAddCharacterMapData(Npc("Doomed"), TalkEvent(), backend.Object);
        Assert.Single(svc.MappedNpcs);

        svc.RemoveCharacter(npc);

        Assert.Empty(svc.MappedNpcs);
        db.Verify(d => d.DeleteCharacter(7), Times.Once);
    }

    [Fact]
    public void ClearMappedCaches_EmptiesBoth()
    {
        var svc = Build(out _, out var backend);
        svc.GetAddCharacterMapData(Npc("A"), TalkEvent(), backend.Object);
        svc.GetAddCharacterMapData(Npc("B", ObjectKind.Pc), new EKEventId(1, TextSource.Chat), backend.Object);

        svc.ClearMappedCaches();

        Assert.Empty(svc.MappedNpcs);
        Assert.Empty(svc.MappedPlayers);
    }

    // ── the actual point: snapshots don't blow up under concurrent mutation ───

    [Fact]
    public async Task IteratingTheCacheWhileItIsMutated_DoesNotThrow()
    {
        // This is the regression the copy-on-write rewrite is for. With the old shared
        // List<NpcMapData>, a reader walking MappedNpcs while the dialogue path added to it
        // threw "Collection was modified; enumeration operation may not execute".
        var svc = Build(out var db, out var backend);
        db.Setup(d => d.FindCharacter(It.IsAny<string>(), It.IsAny<Genders>(), It.IsAny<NpcRaces>(), It.IsAny<int>()))
          .Returns(new CharacterEntity { Id = 1 });

        for (var i = 0; i < 50; i++)
            svc.GetAddCharacterMapData(Npc($"Seed{i}"), TalkEvent(), backend.Object);

        using var stop = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));

        var reader = Task.Run(() =>
        {
            var seen = 0;
            while (!stop.IsCancellationRequested)
                foreach (var npc in svc.MappedNpcs)
                    seen += npc.Name.Length;   // touch it so the loop isn't optimised away
            return seen;
        });

        var writer = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                var added = svc.GetAddCharacterMapData(Npc($"Churn{i++}"), TalkEvent(), backend.Object);
                svc.RemoveCharacter(added);
            }
        });

        // The assertion is that neither task faults.
        await Task.WhenAll(reader, writer);
        Assert.True(reader.IsCompletedSuccessfully);
        Assert.True(writer.IsCompletedSuccessfully);
    }

    [Fact]
    public void MappedNpcs_ReturnsAStableSnapshot()
    {
        // A snapshot taken before a mutation must not observe it — that is what lets callers
        // iterate safely without copying first.
        var svc = Build(out _, out var backend);
        svc.GetAddCharacterMapData(Npc("First"), TalkEvent(), backend.Object);

        var snapshot = svc.MappedNpcs;
        Assert.Single(snapshot);

        svc.GetAddCharacterMapData(Npc("Second"), TalkEvent(), backend.Object);

        Assert.Single(snapshot);            // old snapshot unchanged
        Assert.Equal(2, svc.MappedNpcs.Count); // fresh read sees both
    }
}
